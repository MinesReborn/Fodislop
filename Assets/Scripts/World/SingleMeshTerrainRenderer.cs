using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Utils;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Scripts.World
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    [ExecuteAlways]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = GameConstants.World.CELL_SIZE;

        [SerializeField]
        private Shader _terrainShader;

        [SerializeField]
        private Color _shimmerHighlightColor = Color.white;

        [SerializeField]
        private string _sortingLayerName = "Default";

        [SerializeField]
        private int _sortingOrder = -1000;

        [SerializeField]
        private int _viewportPadding = 2;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Camera _mainCamera;

        // Pre-allocated arrays for mesh data
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private Color[] _colors;
        private Vector4[] _subAtlasRects;
        private Vector4[] _tileSizeUVs;
        private Vector4[] _worldPositions;
        private Vector4[] _animationData;
        private Vector4[] _packedReliefShadowLocalUV;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;

        private struct CachedCellData
        {
            public CellType Type;
            public CellConfigProperties Properties;
            public byte ReliefGroup;
            public CellDistortionType Distortion;
            public bool HasTileGroup;
            public int TileGroupId;
            public Color MinimapColor;
            public CellAnimationType Animation;
            public float AnimationSpeed;
            public Vector4 AtlasRect;
            public int AtlasIndex;
            public float UVTileSize;
            public int AnimationFrameCount;
            public float FrameHeightTiles;
        }

        private CachedCellData[,] _cellCache;
        private int _cacheMinX;
        private int _cacheMinY;
        private int _cacheWidth;
        private int _cacheHeight;
        private CachedCellData _fallbackCacheEntry;

        private Vector3[,] _gridVertexOffsets;
        private float[,] _gridShadowValues;
        private int[,] _cellTilingDescriptors;
        private byte[,] _cellReliefMasks;
        private bool[,] _cellIsRelief;

        private struct CellMetadata
        {
            public CellConfigProperties Properties;
            public byte ReliefGroup;
            public CellDistortionType Distortion;
            public bool HasTileGroup;
            public int TileGroupId;
            public Color MinimapColor;
            public CellAnimationType Animation;
            public float AnimationSpeed;
            public Vector4 AtlasRect;
            public int AtlasIndex;
            public float UVTileSize;
            public int AnimationFrameCount;
            public float FrameHeightTiles;
            public bool IsTextureReady;
        }
        private static readonly Vector2[] _localUVsBuffer =
        {
            new(-0.70710678f, -0.70710678f),
            new(0.70710678f, -0.70710678f),
            new(0.70710678f, 0.70710678f),
            new(-0.70710678f, 0.70710678f),
        };

        private readonly Dictionary<CellType, CellMetadata> _metadataCache = new();
        private readonly List<(int X, int Y)> _pass2Cells = new();
        private readonly Queue<(int X, int Y)> _floodFillQueue = new();

        private CellType[,] _bgMapBuffer;
        private bool _needsRefresh = false;

        protected void OnValidate()
        {
            if (!Application.isPlaying && _materials != null)
            {
                foreach (var mat in _materials)
                {
                    if (mat != null)
                    {
                        mat.SetColor("_ShimmerColor", _shimmerHighlightColor);
                    }
                }
            }
        }

        protected void Awake()
        {
            InitializeShader();
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "TerrainMesh";
                _mesh.MarkDynamic();
                _mesh.indexFormat = IndexFormat.UInt32;
                _meshFilter.mesh = _mesh;
            }

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;

            if (WorldTextureManager.Instance != null)
            {
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
            }

            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
            }
        }

        protected void OnDestroy()
        {
            if (WorldTextureManager.InstanceIfExists != null)
            {
                WorldTextureManager.InstanceIfExists.OnTextureLoaded -= OnTextureLoaded;
            }

            if (MapManager.InstanceIfExists != null)
            {
                MapManager.InstanceIfExists.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }

            CleanupMaterials();
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
                if (_terrainShader == null)
                {
                    _terrainShader = Resources.Load<Shader>("Shaders/Terrain");
                }

                if (_terrainShader == null)
                {
                    Debug.LogWarning("[SingleMeshTerrainRenderer] Custom Terrain shader not found! Falling back to standard URP Lit shader.");
                    _terrainShader = Shader.Find("Universal Render Pipeline/Lit");
                }

                if (_terrainShader == null)
                {
                    _terrainShader = Shader.Find("Sprites/Default");
                }
            }
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            InitializeShader();
            _metadataCache.Clear();
            _needsRefresh = true;
        }

        private void OnWorldDataLoaded()
        {
            _needsRefresh = true;
        }

        protected void LateUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                MapStorage.Instance?.EnsureEditorInitialized();
            }
#endif
            var mm = MapManager.Instance;
            if (mm == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                return;
            }

            EnsureMesh();

            if (_mainCamera == null)
            {
                _mainCamera = mm.MainCamera;
            }

            if (_mainCamera == null)
            {
                return;
            }

            int targetWidth = Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) + (_viewportPadding * 2);
            int targetHeight = Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) + (_viewportPadding * 2);

            targetWidth = Mathf.Clamp(targetWidth, 2, 256);
            targetHeight = Mathf.Clamp(targetHeight, 2, 256);

            if (targetWidth % 2 != 0)
            {
                targetWidth++;
            }

            if (targetHeight % 2 != 0)
            {
                targetHeight++;
            }

            bool dimensionsChanged = targetWidth != _meshWidth || targetHeight != _meshHeight;

            if (dimensionsChanged || !_isInitialized)
            {
                _meshWidth = targetWidth;
                _meshHeight = targetHeight;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
                EnsureBuffersCapacity();
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int currentGridPos = new Vector2Int(
                Mathf.FloorToInt(camPos.x / _cellSize) - (_meshWidth / 2),
                Mathf.FloorToInt(camPos.y / _cellSize) - (_meshHeight / 2)
            );

            if (currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
                _needsRefresh = false;
            }
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            if (!Application.isPlaying || _mainCamera == null)
            {
                return;
            }

            Gizmos.color = new Color(1, 0, 0, 0.5f);
            Gizmos.DrawSphere(new Vector3(_lastGridPos.x * _cellSize, _lastGridPos.y * _cellSize, 0), 0.1f);
        }

        protected void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || _mainCamera == null)
            {
                return;
            }

            Vector3 center = new Vector3(
                (_lastGridPos.x * _cellSize) + (_meshWidth * _cellSize * 0.5f),
                (_lastGridPos.y * _cellSize) + (_meshHeight * _cellSize * 0.5f),
                0);
            Vector3 viewportSize = new Vector3(_meshWidth * _cellSize, _meshHeight * _cellSize, 0);
            Utils.FodislopGizmos.DrawSolidRect(center, (Vector2)viewportSize, new Color(0, 0.5f, 1f, 0.03f), new Color(0, 0.5f, 1f, 0.3f));
        }
#endif

        private void EnsureBuffersCapacity()
        {
            int quadCount = _meshWidth * _meshHeight * 2;
            int vertCount = quadCount * 4;

            if (_vertices == null || _vertices.Length != vertCount)
            {
                _vertices = new Vector3[vertCount];
                _uvs = new Vector2[vertCount];
                _colors = new Color[vertCount];
                _subAtlasRects = new Vector4[vertCount];
                _tileSizeUVs = new Vector4[vertCount];
                _worldPositions = new Vector4[vertCount];
                _animationData = new Vector4[vertCount];
                _packedReliefShadowLocalUV = new Vector4[vertCount];
            }

            _bgMapBuffer = new CellType[_meshWidth, _meshHeight];
            _cacheWidth = _meshWidth + 2;
            _cacheHeight = _meshHeight + 2;
            _cellCache = new CachedCellData[_cacheWidth, _cacheHeight];
            _gridVertexOffsets = new Vector3[_meshWidth + 1, _meshHeight + 1];
            _gridShadowValues = new float[_meshWidth + 1, _meshHeight + 1];
            _cellTilingDescriptors = new int[_meshWidth, _meshHeight];
            _cellReliefMasks = new byte[_meshWidth, _meshHeight];
            _cellIsRelief = new bool[_meshWidth, _meshHeight];
        }

        private CellMetadata GetMetadata(CellType type, List<TextureAtlas> atlases)
        {
            if (_metadataCache.TryGetValue(type, out var meta)) return meta;

            var mm = MapManager.Instance;
            var wtm = WorldTextureManager.Instance;
            if (mm == null || wtm == null) return default;

            var config = mm.GetCellConfig(type);
            int atlasIndex = 0;
            for (int i = 0; i < atlases.Count; i++)
            {
                if (atlases[i].ContainsCell(type))
                {
                    atlasIndex = i;
                    break;
                }
            }

            Vector4 atlasRect = wtm.GetCellFrameRect(type);
            int frameCount = wtm.GetAnimationFrameCount(type);
            int frameSize = wtm.GetFrameSize(type);

            meta = new CellMetadata
            {
                Properties = config.Properties,
                ReliefGroup = config.ReliefGroup,
                Distortion = config.Distortion,
                HasTileGroup = mm.TryGetTileGroup(type, out int gid),
                TileGroupId = gid,
                MinimapColor = mm.GetCellMinimapColor(type),
                Animation = config.Animation,
                AnimationSpeed = wtm.GetAnimationSpeedForCell(type),
                AtlasRect = atlasRect,
                AtlasIndex = atlasIndex,
                UVTileSize = (atlases.Count > atlasIndex) ? (float)RenderingConstants.CellSize / atlases[atlasIndex].Size : 0,
                AnimationFrameCount = frameCount,
                FrameHeightTiles = (float)frameSize / RenderingConstants.CellSize,
                IsTextureReady = atlasRect.z > 0.0001f
            };
            _metadataCache[type] = meta;
            return meta;
        }

        private void PopulateCellCache(int minX, int minY)
        {
            var mm = MapManager.Instance;
            var mapStorage = MapStorage.Instance;
            if (mm == null || mapStorage == null || !mapStorage.IsReady) return;

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            var atlases = WorldTextureManager.Instance?.GetAllAtlases();
            var layer = mapStorage.CellLayer;
            if (layer == null || atlases == null)
            {
                return;
            }

            _cacheMinX = minX - 1;
            _cacheMinY = minY - 1;

            if (_cellCache == null || _cellCache.GetLength(0) != _cacheWidth || _cellCache.GetLength(1) != _cacheHeight)
            {
                EnsureBuffersCapacity();
            }

            for (int x = 0; x < _cacheWidth; x++)
            {
                int worldX = _cacheMinX + x;
                int lastChunkIndex = -1;
                CellType[] currentChunk = null;

                for (int y = 0; y < _cacheHeight; y++)
                {
                    int serverY = CoordinateUtils.UnityToServerY(_cacheMinY + y, worldHeight);

                    if (!layer.GetChunkIndexAndLocal(worldX, serverY, out int chunkIndex, out int localIndex)) continue;

                    if (chunkIndex != lastChunkIndex)
                    {
                        currentChunk = layer.GetChunk(chunkIndex, false, true);
                        lastChunkIndex = chunkIndex;
                    }

                    CellType type = currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
                    var meta = GetMetadata(type, atlases);
                    _cellCache[x, y] = new CachedCellData
                    {
                        Type = type,
                        Properties = meta.Properties,
                        ReliefGroup = meta.ReliefGroup,
                        Distortion = meta.Distortion,
                        HasTileGroup = meta.HasTileGroup,
                        TileGroupId = meta.TileGroupId,
                        MinimapColor = meta.MinimapColor,
                        Animation = meta.Animation,
                        AnimationSpeed = meta.AnimationSpeed,
                        AtlasRect = meta.AtlasRect,
                        AtlasIndex = meta.AtlasIndex,
                        UVTileSize = meta.UVTileSize,
                        AnimationFrameCount = meta.AnimationFrameCount,
                        FrameHeightTiles = meta.FrameHeightTiles
                    };

                    if (Application.isPlaying && type != CellType.Unloaded && !meta.IsTextureReady)
                    {
                        WorldTextureManager.Instance?.RequestTexture(type);
                    }
                }
            }
        }

        private void PrecalculateData()
        {
            int gw = _meshWidth + 1;
            int gh = _meshHeight + 1;
            for (int x = 0; x < gw; x++)
            {
                for (int y = 0; y < gh; y++)
                {
                    int cx = x + 1;
                    int cy = y + 1;
                    CachedCellData tl = _cellCache[x, cy];
                    CachedCellData tr = _cellCache[cx, cy];
                    CachedCellData bl = _cellCache[x, y];
                    CachedCellData br = _cellCache[cx, y];

                    if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block ||
                        bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block)
                    {
                        _gridVertexOffsets[x, y] = Vector3.zero;
                    }
                    else
                    {
                        int xSign = 0;
                        int ySign = 0;
                        if (bl.Distortion == CellDistortionType.Cause)
                        {
                            xSign -= 1;
                            ySign += 1;
                        }

                        if (br.Distortion == CellDistortionType.Cause)
                        {
                            xSign += 1;
                            ySign += 1;
                        }

                        if (tl.Distortion == CellDistortionType.Cause)
                        {
                            xSign -= 1;
                            ySign -= 1;
                        }

                        if (tr.Distortion == CellDistortionType.Cause)
                        {
                            xSign += 1;
                            ySign -= 1;
                        }

                        if (xSign == 0 && ySign == 0)
                        {
                            _gridVertexOffsets[x, y] = Vector3.zero;
                        }
                        else
                        {
                            uint seed = (uint)((_cacheMinX + cx) * 374761397 + (_cacheMinY + cy) * 668265263);
                            seed = (seed ^ (seed >> 13)) * 1274126177;
                            seed = seed ^ (seed >> 16);
                            float r = ((seed % 4) + 1) * 0.0625f;
                            uint seed2 = seed * 2654435761u;
                            float ry = ((seed2 % 4) + 1) * 0.0625f;
                            _gridVertexOffsets[x, y] = new Vector3(xSign > 0 ? r : (xSign < 0 ? -r : 0), ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
                        }
                    }

                    bool hasC = (bl.Properties & CellConfigProperties.DropsShadow) != 0 || (br.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                (tl.Properties & CellConfigProperties.DropsShadow) != 0 || (tr.Properties & CellConfigProperties.DropsShadow) != 0;
                    bool hasR = (bl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (br.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                (tl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (tr.Properties & CellConfigProperties.ReceivesShadow) != 0;
                    _gridShadowValues[x, y] = (hasC && hasR) ? 0.7f : 0.0f;
                }
            }

            for (int x = 0; x < _meshWidth; x++)
            {
                for (int y = 0; y < _meshHeight; y++)
                {
                    int cx = x + 1;
                    int cy = y + 1;
                    var data = _cellCache[cx, cy];
                    if (data.HasTileGroup)
                    {
                        byte m = 0;
                        if (_cellCache[cx - 1, cy].HasTileGroup && _cellCache[cx - 1, cy].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 0;
                        }

                        if (_cellCache[cx - 1, cy - 1].HasTileGroup && _cellCache[cx - 1, cy - 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 1;
                        }

                        if (_cellCache[cx, cy - 1].HasTileGroup && _cellCache[cx, cy - 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 2;
                        }

                        if (_cellCache[cx + 1, cy - 1].HasTileGroup && _cellCache[cx + 1, cy - 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 3;
                        }

                        if (_cellCache[cx + 1, cy].HasTileGroup && _cellCache[cx + 1, cy].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 4;
                        }

                        if (_cellCache[cx + 1, cy + 1].HasTileGroup && _cellCache[cx + 1, cy + 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 5;
                        }

                        if (_cellCache[cx, cy + 1].HasTileGroup && _cellCache[cx, cy + 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 6;
                        }

                        if (_cellCache[cx - 1, cy + 1].HasTileGroup && _cellCache[cx - 1, cy + 1].TileGroupId == data.TileGroupId)
                        {
                            m |= 1 << 7;
                        }

                        _cellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
                    }
                    else
                    {
                        _cellTilingDescriptors[x, y] = 0;
                    }

                    byte rm = 0;
                    bool isR = false;
                    if (_cellCache[cx, cy + 1].ReliefGroup >= data.ReliefGroup)
                    {
                        rm |= 1;
                    }
                    else
                    {
                        isR = true;
                    }

                    if (_cellCache[cx - 1, cy].ReliefGroup >= data.ReliefGroup)
                    {
                        rm |= 2;
                    }
                    else
                    {
                        isR = true;
                    }

                    if (_cellCache[cx, cy - 1].ReliefGroup >= data.ReliefGroup)
                    {
                        rm |= 4;
                    }
                    else
                    {
                        isR = true;
                    }

                    if (_cellCache[cx + 1, cy].ReliefGroup >= data.ReliefGroup)
                    {
                        rm |= 8;
                    }
                    else
                    {
                        isR = true;
                    }

                    _cellReliefMasks[x, y] = rm;
                    _cellIsRelief[x, y] = isR;
                }
            }
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            var wtm = WorldTextureManager.Instance;
            var mm = MapManager.Instance;
            if (wtm == null || mm == null)
            {
                return;
            }

            var atlases = wtm.GetAllAtlases();
            if (atlases == null)
            {
                return;
            }

            bool anySubMeshIndexNull = _subMeshIndices == null || _subMeshIndices.Length != atlases.Count;
            if (!anySubMeshIndexNull)
            {
                for (int i = 0; i < _subMeshIndices.Length; i++)
                {
                    if (_subMeshIndices[i] == null)
                    {
                        anySubMeshIndexNull = true;
                        break;
                    }
                }
            }

            bool anyMaterialNull = _materials == null || _materials.Length != atlases.Count;
            if (!anyMaterialNull)
            {
                for (int i = 0; i < _materials.Length; i++)
                {
                    if (_materials[i] == null)
                    {
                        anyMaterialNull = true;
                        break;
                    }
                }
            }

            bool materialsChanged = false;
            if (anySubMeshIndexNull || anyMaterialNull)
            {
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new List<int>();
                    _materials[i] = new Material(_terrainShader);
                }

                materialsChanged = true;
            }

            foreach (var list in _subMeshIndices)
            {
                list.Clear();
            }

            PopulateCellCache(minX, minY);
            PrecalculateData();
            ComputeBackgroundMap();

            int vIdx = 0;
            int worldHeight = mm.WorldHeight;
            for (int x = 0; x < _meshWidth; x++)
            {
                int gridX = minX + x;
                for (int y = 0; y < _meshHeight; y++)
                {
                    int unityY = minY + y;
                    FillQuadData(x, y, gridX, unityY, worldHeight, true, ref vIdx, atlases);
                    FillQuadData(x, y, gridX, unityY, worldHeight, false, ref vIdx, atlases);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _subAtlasRects);
            _mesh.SetUVs(2, _tileSizeUVs);
            _mesh.SetUVs(3, _worldPositions);
            _mesh.SetUVs(4, _animationData);
            _mesh.SetUVs(5, _packedReliefShadowLocalUV);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                var atlasTex = atlases[i].Texture;
                if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                {
                    var flowMapCoord = wtm.GetFlowMapCoordinate(atlases[i]);
                    Rect r = flowMapCoord.UVRect;
                    _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                    _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                    _materials[i].SetTexture("_BaseMap", atlasTex);
                }

                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
            }

            _mesh.UploadMeshData(false);

            bool needReassignMaterials = materialsChanged;
            if (!needReassignMaterials)
            {
                var sharedMats = _meshRenderer.sharedMaterials;
                if (sharedMats == null || sharedMats.Length != _materials.Length)
                {
                    needReassignMaterials = true;
                }
                else
                {
                    for (int i = 0; i < _materials.Length; i++)
                    {
                        if (sharedMats[i] != _materials[i])
                        {
                            needReassignMaterials = true;
                            break;
                        }
                    }
                }
            }

            if (needReassignMaterials)
            {
                _meshRenderer.sharedMaterials = _materials;
            }
        }

        private void FillQuadData(int x, int y, int gridX, int unityY, int worldHeight, bool isBackground, ref int vIdx, List<TextureAtlas> atlases)
        {
            int cx = x + 1;
            int cy = y + 1;
            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

            CellType cellType = isBackground ? _bgMapBuffer[x, y] : _cellCache[cx, cy].Type;
            if (isBackground && (cellType == _cellCache[cx, cy].Type || cellType == 0))
            {
                cellType = CellType.Unloaded;
            }

            ref CachedCellData data = ref (cellType == _cellCache[cx, cy].Type ? ref _cellCache[cx, cy] : ref GetNeighborCacheEntry(cellType, cx, cy, atlases));
            int atlasIndex = data.AtlasIndex;

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize;
            float ly = y * _cellSize;

            _vertices[vIdx + 0] = new Vector3(lx, ly, zOffset) + _gridVertexOffsets[x, y];
            _vertices[vIdx + 1] = new Vector3(lx + _cellSize, ly, zOffset) + _gridVertexOffsets[x + 1, y];
            _vertices[vIdx + 2] = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + _gridVertexOffsets[x + 1, y + 1];
            _vertices[vIdx + 3] = new Vector3(lx, ly + _cellSize, zOffset) + _gridVertexOffsets[x, y + 1];

            _uvs[vIdx + 0] = new Vector2(0, 0);
            _uvs[vIdx + 1] = new Vector2(1, 0);
            _uvs[vIdx + 2] = new Vector2(1, 1);
            _uvs[vIdx + 3] = new Vector2(0, 1);

            int descriptor = (cellType == _cellCache[cx, cy].Type) ? _cellTilingDescriptors[x, y] : 0;
            int worldWidth = MapManager.Instance.WorldWidth;
            bool isOffWorld = gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight;
            float packedW = (data.HasTileGroup ? 1f : 0f) + (isOffWorld ? 2f : 0f);

            if (data.HasTileGroup && descriptor != 0)
            {
                if ((descriptor & 0x40) != 0)
                {
                    (_uvs[vIdx + 0].x, _uvs[vIdx + 1].x) = (_uvs[vIdx + 1].x, _uvs[vIdx + 0].x);
                    (_uvs[vIdx + 3].x, _uvs[vIdx + 2].x) = (_uvs[vIdx + 2].x, _uvs[vIdx + 3].x);
                }

                if ((descriptor & 0x20) != 0)
                {
                    (_uvs[vIdx + 0].y, _uvs[vIdx + 3].y) = (_uvs[vIdx + 3].y, _uvs[vIdx + 0].y);
                    (_uvs[vIdx + 1].y, _uvs[vIdx + 2].y) = (_uvs[vIdx + 2].y, _uvs[vIdx + 1].y);
                }

                if ((descriptor & 0x80) != 0)
                {
                    Vector2 t = _uvs[vIdx + 0];
                    _uvs[vIdx + 0] = _uvs[vIdx + 1];
                    _uvs[vIdx + 1] = _uvs[vIdx + 2];
                    _uvs[vIdx + 2] = _uvs[vIdx + 3];
                    _uvs[vIdx + 3] = t;
                }
            }

            bool useFallback = data.AtlasRect.z < 0.0001f;
            Color color = useFallback ? data.MinimapColor : _shimmerHighlightColor;
            float animOffset = 0f;
            if (!useFallback && data.Animation == CellAnimationType.Blinking)
            {
                uint seed = (uint)(gridX * 374761397 + serverY * 668265263);
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                animOffset = (seed % 6283) / 1000f;
            }

            Vector4 animDataVec = new Vector4((float)data.Animation, (float)data.AnimationSpeed, animOffset, 0f);
            Vector4 tileSizeVec = new Vector4(data.UVTileSize, data.UVTileSize, (float)data.AnimationFrameCount, data.FrameHeightTiles);
            Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, packedW);

            bool isRelief = (cellType == _cellCache[cx, cy].Type) && _cellIsRelief[x, y];
            byte reliefMask = (cellType == _cellCache[cx, cy].Type) ? _cellReliefMasks[x, y] : (byte)0;
            float textureType = isRelief ? 1.0f : 0.0f;

            for (int i = 0; i < 4; i++)
            {
                _colors[vIdx + i] = color;
                _subAtlasRects[vIdx + i] = data.AtlasRect;
                _tileSizeUVs[vIdx + i] = tileSizeVec;
                _worldPositions[vIdx + i] = worldPosVec;
                _animationData[vIdx + i] = animDataVec;
                float shadowVal = _gridShadowValues[x + (i == 1 || i == 2 ? 1 : 0), y + (i == 2 || i == 3 ? 1 : 0)];
                _packedReliefShadowLocalUV[vIdx + i] = new Vector4(textureType, isRelief ? reliefMask : shadowVal, _localUVsBuffer[i].x, _localUVsBuffer[i].y);
            }

            _subMeshIndices[atlasIndex].Add(vIdx + 0);
            _subMeshIndices[atlasIndex].Add(vIdx + 3);
            _subMeshIndices[atlasIndex].Add(vIdx + 2);
            _subMeshIndices[atlasIndex].Add(vIdx + 2);
            _subMeshIndices[atlasIndex].Add(vIdx + 1);
            _subMeshIndices[atlasIndex].Add(vIdx + 0);
            vIdx += 4;
        }

        private ref CachedCellData GetNeighborCacheEntry(CellType type, int cx, int cy, List<TextureAtlas> atlases)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (_cellCache[cx + dx, cy + dy].Type == type)
                    {
                        return ref _cellCache[cx + dx, cy + dy];
                    }
                }
            }

            var meta = GetMetadata(type, atlases);
            _fallbackCacheEntry = new CachedCellData
            {
                Type = type,
                Properties = meta.Properties,
                ReliefGroup = meta.ReliefGroup,
                Distortion = meta.Distortion,
                HasTileGroup = meta.HasTileGroup,
                TileGroupId = meta.TileGroupId,
                MinimapColor = meta.MinimapColor,
                Animation = meta.Animation,
                AnimationSpeed = meta.AnimationSpeed,
                AtlasRect = meta.AtlasRect,
                AtlasIndex = meta.AtlasIndex,
                UVTileSize = meta.UVTileSize,
                AnimationFrameCount = meta.AnimationFrameCount,
                FrameHeightTiles = meta.FrameHeightTiles
            };
            return ref _fallbackCacheEntry;
        }

        private void ComputeBackgroundMap()
        {
            Array.Clear(_bgMapBuffer, 0, _bgMapBuffer.Length);
            _floodFillQueue.Clear();
            for (int x = 0; x < _meshWidth; x++)
            {
                for (int y = 0; y < _meshHeight; y++)
                {
                    var cell = _cellCache[x + 1, y + 1];
                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        _bgMapBuffer[x, y] = cell.Type;
                        _floodFillQueue.Enqueue((x, y));
                    }
                }
            }

            _pass2Cells.Clear();
            Span<TypeCount> typeCounts = stackalloc TypeCount[8];
            for (int x = 0; x < _meshWidth; x++)
            {
                for (int y = 0; y < _meshHeight; y++)
                {
                    if (_bgMapBuffer[x, y] != 0)
                    {
                        continue;
                    }

                    int distinctCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }

                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || nx >= _meshWidth || ny < 0 || ny >= _meshHeight)
                            {
                                continue;
                            }

                            var n = _cellCache[nx + 1, ny + 1];
                            if ((n.Properties & CellConfigProperties.Passable) != 0)
                            {
                                bool found = false;
                                for (int i = 0; i < distinctCount; i++)
                                {
                                    if (typeCounts[i].Type == n.Type)
                                    {
                                        typeCounts[i].Count++;
                                        found = true;
                                        break;
                                    }
                                }

                                if (!found && distinctCount < 8)
                                {
                                    typeCounts[distinctCount++] = new TypeCount { Type = n.Type, Count = 1 };
                                }
                            }
                        }
                    }

                    if (distinctCount > 0)
                    {
                        CellType mostFrequent = typeCounts[0].Type;
                        int maxC = typeCounts[0].Count;
                        for (int i = 1; i < distinctCount; i++)
                        {
                            if (typeCounts[i].Count > maxC)
                            {
                                maxC = typeCounts[i].Count;
                                mostFrequent = typeCounts[i].Type;
                            }
                        }

                        _bgMapBuffer[x, y] = mostFrequent;
                        _pass2Cells.Add((x, y));
                    }
                }
            }

            foreach (var cell in _pass2Cells)
            {
                _floodFillQueue.Enqueue(cell);
            }

            while (_floodFillQueue.Count > 0)
            {
                var (x, y) = _floodFillQueue.Dequeue();
                CellType currentBg = _bgMapBuffer[x, y];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= _meshWidth || ny < 0 || ny >= _meshHeight)
                        {
                            continue;
                        }

                        if (_bgMapBuffer[nx, ny] == CellType.Unloaded)
                        {
                            _bgMapBuffer[nx, ny] = currentBg;
                            _floodFillQueue.Enqueue((nx, ny));
                        }
                    }
                }
            }

            for (int x = 0; x < _meshWidth; x++)
            {
                for (int y = 0; y < _meshHeight; y++)
                {
                    if (_bgMapBuffer[x, y] == CellType.Unloaded)
                    {
                        _bgMapBuffer[x, y] = CellType.Empty;
                    }
                }
            }
        }

        private void CleanupMaterials()
        {
            if (_materials != null)
            {
                foreach (var mat in _materials)
                {
                    if (mat != null)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(mat);
                        }
                        else
                        {
                            DestroyImmediate(mat);
                        }
                    }
                }
            }
        }

        private void EnsureMesh()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
            }

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "TerrainMesh";
                _mesh.MarkDynamic();
                _mesh.indexFormat = IndexFormat.UInt32;
            }

            if (_meshFilter != null && _meshFilter.sharedMesh != _mesh)
            {
                _meshFilter.sharedMesh = _mesh;
            }
        }

        private struct TypeCount
        {
            public CellType Type;
            public int Count;
        }
    }
}
