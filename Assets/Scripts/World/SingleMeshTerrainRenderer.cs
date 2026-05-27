using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
        [SerializeField] private int _bufferCells = 2;
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Color _shimmerHighlightColor = Color.white;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -1000;
        [SerializeField] private int _viewportPadding = 2;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Camera _mainCamera;

        // Pre-allocated arrays for mesh data
        private Vector3[] _vertices;
        private Vector2[] _uvs;
        private Color[] _colors;
        private Vector4[] _subAtlasRects;
        private Vector4[] _tileSizeUVs; // xy: tileSizeUV, zw: frame metadata (z: frameCount, w: frameHeightTiles)
        private Vector4[] _worldPositions;
        private Vector4[] _animationData;
        private Vector4[] _packedReliefShadowLocalUV; // xy: shadowRelief, zw: localUV

        private Vector2Int _lastMinVisible = new Vector2Int(-1, -1);
        private Vector2Int _lastMaxVisible = new Vector2Int(-1, -1);
        private float _lastRebuildTime = 0;
        private bool _needsRebuild = false;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private float _lastOrthoSize;
        private float _lastAspect;
        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _lastPreloadChunkPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;

        // Optimized viewport cache
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
        private int _cacheMinX, _cacheMinY;
        private int _cacheWidth, _cacheHeight;
        private CachedCellData _fallbackCacheEntry;

        // Pre-calculation buffers
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
        private readonly Dictionary<CellType, CellMetadata> _metadataCache = new();

        private CellType[,] _bgMapBuffer;
        private readonly List<(int x, int y)> _pass2Cells = new();
        private readonly Queue<(int x, int y)> _floodFillQueue = new();
        private static readonly Vector2[] _localUVsBuffer = {
            new(-0.70710678f, -0.70710678f), new(0.70710678f, -0.70710678f),
            new(0.70710678f, 0.70710678f), new(-0.70710678f, 0.70710678f)
        };

        private bool _needsRefresh = false;
        private bool _useColorLod = false;

        private void OnValidate()
        {
            if (!Application.isPlaying && _materials != null)
            {
                foreach (var mat in _materials)
                    if (mat != null) mat.SetColor("_ShimmerColor", _shimmerHighlightColor);
            }
        }

        private void Awake()
        {
            InitializeShader();
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = Camera.main;

            _mesh = new Mesh();
            _mesh.name = "TerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
            gameObject.layer = 0;

            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
            if (MapManager.Instance != null)
                MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;

            MapStorage.Instance.OnChunkLoaded += () => _needsRefresh = true;
        }

        private void OnDestroy()
        {
            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded -= OnTextureLoaded;
            if (MapManager.Instance != null)
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            CleanupMaterials();
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
                if (_terrainShader == null)
                    _terrainShader = Resources.Load<Shader>("Shaders/Terrain");
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

        private void OnMapChunkLoaded()
{
    _needsRefresh = true;
}

        private void LateUpdate()
        {
            PreloadVisibleChunks();
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            int targetWidth = Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) + _viewportPadding * 2;
            int targetHeight = Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) + _viewportPadding * 2;

            // Force even dimensions to stabilize centering logic
            if (targetWidth % 2 != 0) targetWidth++;
            if (targetHeight % 2 != 0) targetHeight++;

            bool dimensionsChanged = targetWidth != _meshWidth || targetHeight != _meshHeight;

            if (dimensionsChanged || !_isInitialized)
            {
                _meshWidth = targetWidth;
                _meshHeight = targetHeight;
                _lastOrthoSize = _mainCamera.orthographicSize;
                _lastAspect = _mainCamera.aspect;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);

                EnsureBuffersCapacity();
                _mesh.bounds = new Bounds(new Vector3(_meshWidth * _cellSize * 0.5f, _meshHeight * _cellSize * 0.5f, 0), new Vector3(_meshWidth * _cellSize, _meshHeight * _cellSize, 10));
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int currentGridPos = new Vector2Int(
                Mathf.FloorToInt(camPos.x / _cellSize) - _meshWidth / 2,
                Mathf.FloorToInt(camPos.y / _cellSize) - _meshHeight / 2
            );

            if (currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
                _needsRefresh = false;
            }
        }

        private void EnsureBuffersCapacity()
        {
            int quadCount = _meshWidth * _meshHeight * 2;
            int vertCount = quadCount * 4;

            if (_vertices == null || _vertices.Length != vertCount)
            {
                _vertices = new Vector3[vertCount]; _uvs = new Vector2[vertCount]; _colors = new Color[vertCount];
                _subAtlasRects = new Vector4[vertCount]; _tileSizeUVs = new Vector4[vertCount]; _worldPositions = new Vector4[vertCount];
                _animationData = new Vector4[vertCount]; _packedReliefShadowLocalUV = new Vector4[vertCount];
            }

            if (_bgMapBuffer == null || _bgMapBuffer.GetLength(0) != _meshWidth || _bgMapBuffer.GetLength(1) != _meshHeight)
                _bgMapBuffer = new CellType[_meshWidth, _meshHeight];

            _cacheWidth = _meshWidth + 2; _cacheHeight = _meshHeight + 2;
            if (_cellCache == null || _cellCache.GetLength(0) != _cacheWidth || _cellCache.GetLength(1) != _cacheHeight)
                _cellCache = new CachedCellData[_cacheWidth, _cacheHeight];

            int gw = _meshWidth + 1, gh = _meshHeight + 1;
            if (_gridVertexOffsets == null || _gridVertexOffsets.GetLength(0) != gw || _gridVertexOffsets.GetLength(1) != gh)
            {
                _gridVertexOffsets = new Vector3[gw, gh];
                _gridShadowValues = new float[gw, gh];
            }

            if (_cellTilingDescriptors == null || _cellTilingDescriptors.GetLength(0) != _meshWidth || _cellTilingDescriptors.GetLength(1) != _meshHeight)
            {
                _cellTilingDescriptors = new int[_meshWidth, _meshHeight];
                _cellReliefMasks = new byte[_meshWidth, _meshHeight];
                _cellIsRelief = new bool[_meshWidth, _meshHeight];
            }
        }

        private CellMetadata GetMetadata(CellType type, List<TextureAtlas> atlases)
        {
            if (_metadataCache.TryGetValue(type, out var meta)) return meta;
            var config = MapManager.Instance.GetCellConfig(type);
            int atlasIndex = 0;
            for (int i = 0; i < atlases.Count; i++) if (atlases[i].ContainsCell(type)) { atlasIndex = i; break; }
            Vector4 atlasRect = WorldTextureManager.Instance.GetCellFrameRect(type);
            int frameCount = WorldTextureManager.Instance.GetAnimationFrameCount(type);
            int frameSize = WorldTextureManager.Instance.GetFrameSize(type);

            meta = new CellMetadata
            {
                Properties = config.Properties,
                ReliefGroup = config.ReliefGroup,
                Distortion = config.Distortion,
                HasTileGroup = MapManager.Instance.TryGetTileGroup(type, out int gid),
                TileGroupId = gid,
                MinimapColor = MapManager.Instance.GetCellMinimapColor(type),
                Animation = config.Animation,
                AnimationSpeed = WorldTextureManager.Instance.GetAnimationSpeedForCell(type),
                AtlasRect = atlasRect,
                AtlasIndex = atlasIndex,
                UVTileSize = atlases.Count > atlasIndex ? (float)RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size : 0,
                AnimationFrameCount = frameCount,
                FrameHeightTiles = (float)frameSize / RenderingConstants.CELL_SIZE,
                IsTextureReady = atlasRect.z > 0.0001f
            };
            _metadataCache[type] = meta;
            return meta;
        }

        private void PopulateCellCache(int minX, int minY)
        {
            _cacheMinX = minX - 1; _cacheMinY = minY - 1;
            int worldWidth = MapManager.Instance.WorldWidth, worldHeight = MapManager.Instance.WorldHeight;
            var atlases = WorldTextureManager.Instance.GetAllAtlases();

            for (int y = 0; y < _cacheHeight; y++)
            {
                for (int x = 0; x < _cacheWidth; x++)
                {
                    int gridX = _cacheMinX + x, unityY = _cacheMinY + y;

                    CellType type;
                    if (gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight)
                    {
                        if (gridX < 0 || gridX >= worldWidth || unityY < 0)
                            type = (CellType)0;   // бока/низ — порода из 0.gif
                        else
                            type = CellType.Unloaded;  // верх — рисуется SurfaceRenderer
                    }
                    else
                    {
                        int serverY = worldHeight - 1 - unityY;
                        type = MapStorage.Instance.GetCell(gridX, serverY);
                    }

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
                    if (type != CellType.Unloaded && !meta.IsTextureReady)
                        WorldTextureManager.Instance.RequestTexture(type);
                }
            }

            WorldTextureManager.Instance.RequestTexture((CellType)0);
        }

        private void PrecalculateData()
        {
            int gw = _meshWidth + 1, gh = _meshHeight + 1;
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++)
                {
                    int cx = x + 1, cy = y + 1;
                    CachedCellData tl = _cellCache[cx - 1, cy], tr = _cellCache[cx, cy], bl = _cellCache[cx - 1, cy - 1], br = _cellCache[cx, cy - 1];
                    if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block || bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block) _gridVertexOffsets[x, y] = Vector3.zero;
                    else
                    {
                        int xSign = 0, ySign = 0;
                        if (bl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
                        if (br.Distortion == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
                        if (tl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
                        if (tr.Distortion == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }
                        if (xSign == 0 && ySign == 0) _gridVertexOffsets[x, y] = Vector3.zero;
                        else
                        {
                            uint seed = (uint)((_cacheMinX + cx) * 374761397 + (_cacheMinY + cy) * 668265263);
                            seed = (seed ^ (seed >> 13)) * 1274126177; seed = seed ^ (seed >> 16);
                            float r = ((seed % 4) + 1) * 0.0625f;
                            uint seed2 = seed * 2654435761u; float ry = ((seed2 % 4) + 1) * 0.0625f;
                            _gridVertexOffsets[x, y] = new Vector3(xSign > 0 ? r : (xSign < 0 ? -r : 0), ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
                        }
                    }
                    bool hasC = (bl.Properties & CellConfigProperties.DropsShadow) != 0 || (br.Properties & CellConfigProperties.DropsShadow) != 0 || (tl.Properties & CellConfigProperties.DropsShadow) != 0 || (tr.Properties & CellConfigProperties.DropsShadow) != 0;
                    bool hasR = (bl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (br.Properties & CellConfigProperties.ReceivesShadow) != 0 || (tl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (tr.Properties & CellConfigProperties.ReceivesShadow) != 0;
                    _gridShadowValues[x, y] = (hasC && hasR) ? 0.7f : 0.0f;
                }
            }
            for (int y = 0; y < _meshHeight; y++)
            {
                for (int x = 0; x < _meshWidth; x++)
                {
                    int cx = x + 1, cy = y + 1; var data = _cellCache[cx, cy];
                    if (data.HasTileGroup)
                    {
                        byte m = 0;
                        // Neighbor bits (counter-clockwise from L): L(0), BL(1), B(2), BR(3), R(4), TR(5), T(6), TL(7)
                        // Unity Y axis is Up (+1 for Top), Server Y axis is Down (+1 for Bottom)
                        // Local cache Y index matches Unity Y: 0 (bottom-most), cacheWidth-1 (top-most)
                        if (_cellCache[cx - 1, cy].HasTileGroup && _cellCache[cx - 1, cy].TileGroupId == data.TileGroupId) m |= (1 << 0); // L
                        if (_cellCache[cx - 1, cy - 1].HasTileGroup && _cellCache[cx - 1, cy - 1].TileGroupId == data.TileGroupId) m |= (1 << 1); // BL
                        if (_cellCache[cx, cy - 1].HasTileGroup && _cellCache[cx, cy - 1].TileGroupId == data.TileGroupId) m |= (1 << 2); // B
                        if (_cellCache[cx + 1, cy - 1].HasTileGroup && _cellCache[cx + 1, cy - 1].TileGroupId == data.TileGroupId) m |= (1 << 3); // BR
                        if (_cellCache[cx + 1, cy].HasTileGroup && _cellCache[cx + 1, cy].TileGroupId == data.TileGroupId) m |= (1 << 4); // R
                        if (_cellCache[cx + 1, cy + 1].HasTileGroup && _cellCache[cx + 1, cy + 1].TileGroupId == data.TileGroupId) m |= (1 << 5); // TR
                        if (_cellCache[cx, cy + 1].HasTileGroup && _cellCache[cx, cy + 1].TileGroupId == data.TileGroupId) m |= (1 << 6); // T
                        if (_cellCache[cx - 1, cy + 1].HasTileGroup && _cellCache[cx - 1, cy + 1].TileGroupId == data.TileGroupId) m |= (1 << 7); // TL
                        _cellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
                    }
                    else _cellTilingDescriptors[x, y] = 0;
                    byte rm = 0; bool isR = false;
                    if (_cellCache[cx, cy + 1].ReliefGroup >= data.ReliefGroup) rm |= 1; else isR = true;
                    if (_cellCache[cx - 1, cy].ReliefGroup >= data.ReliefGroup) rm |= 2; else isR = true;
                    if (_cellCache[cx, cy - 1].ReliefGroup >= data.ReliefGroup) rm |= 4; else isR = true;
                    if (_cellCache[cx + 1, cy].ReliefGroup >= data.ReliefGroup) rm |= 8; else isR = true;
                    _cellReliefMasks[x, y] = rm; _cellIsRelief[x, y] = isR;
                }
            }
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) { _mesh.Clear(); return; }

            _mesh.Clear();

            bool materialsChanged = false;
            if (_subMeshIndices.Length != atlases.Count)
            {
                CleanupMaterials(); _subMeshIndices = new List<int>[atlases.Count]; _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++) { _subMeshIndices[i] = new(); _materials[i] = new Material(_terrainShader); }
                materialsChanged = true;
            }
            foreach (var list in _subMeshIndices) list.Clear();

            PopulateCellCache(minX, minY);
            PrecalculateData();
            ComputeBackgroundMap();

            int vIdx = 0;
            for (int y = 0; y < _meshHeight; y++)
            {
                for (int x = 0; x < _meshWidth; x++)
                {
                    FillQuadData(x, y, minX + x, minY + y, true, ref vIdx, atlases);
                    FillQuadData(x, y, minX + x, minY + y, false, ref vIdx, atlases);
                }
            }

            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uvs); _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _subAtlasRects); _mesh.SetUVs(2, _tileSizeUVs); _mesh.SetUVs(3, _worldPositions);
            _mesh.SetUVs(4, _animationData); _mesh.SetUVs(5, _packedReliefShadowLocalUV);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                var atlasTex = atlases[i].Texture;
                if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                {
                    var flowMapCoord = WorldTextureManager.Instance.GetFlowMapCoordinate(atlases[i]); Rect r = flowMapCoord.UVRect;
                    _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                    _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                    _materials[i].SetTexture("_BaseMap", atlasTex);
                }
                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
            }

            _mesh.bounds = new Bounds(new Vector3(_meshWidth * _cellSize * 0.5f, _meshHeight * _cellSize * 0.5f, 0), new Vector3(_meshWidth * _cellSize, _meshHeight * _cellSize, 10));
            _mesh.UploadMeshData(false);
            if (materialsChanged) _meshRenderer.sharedMaterials = _materials;
        }

        private void FillQuadData(int x, int y, int gridX, int unityY, bool isBackground, ref int vIdx, List<TextureAtlas> atlases)
        {
            if (unityY >= MapManager.Instance.WorldHeight) { vIdx += 4; return; }
            int cx = x + 1, cy = y + 1;
            int serverY = (MapManager.Instance.WorldHeight - 1 - unityY) % MapManager.Instance.WorldHeight;
            if (serverY < 0) serverY += MapManager.Instance.WorldHeight;

            CellType cellType = isBackground ? _bgMapBuffer[x, y] : _cellCache[cx, cy].Type;
            if (isBackground && (cellType == _cellCache[cx, cy].Type || cellType == 0)) cellType = CellType.Unloaded;

            ref CachedCellData data = ref (cellType == _cellCache[cx, cy].Type ? ref _cellCache[cx, cy] : ref GetNeighborCacheEntry(cellType, cx, cy, atlases));
            int atlasIndex = data.AtlasIndex;

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize, ly = y * _cellSize;

            _vertices[vIdx + 0] = new Vector3(lx, ly, zOffset) + _gridVertexOffsets[x, y];
            _vertices[vIdx + 1] = new Vector3(lx + _cellSize, ly, zOffset) + _gridVertexOffsets[x + 1, y];
            _vertices[vIdx + 2] = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + _gridVertexOffsets[x + 1, y + 1];
            _vertices[vIdx + 3] = new Vector3(lx, ly + _cellSize, zOffset) + _gridVertexOffsets[x, y + 1];

            _uvs[vIdx + 0] = new Vector2(0, 0); _uvs[vIdx + 1] = new Vector2(1, 0); _uvs[vIdx + 2] = new Vector2(1, 1); _uvs[vIdx + 3] = new Vector2(0, 1);

            int descriptor = (cellType == _cellCache[cx, cy].Type) ? _cellTilingDescriptors[x, y] : 0;
            int worldWidth = MapManager.Instance.WorldWidth;
            int worldHeight = MapManager.Instance.WorldHeight;
            bool isOffWorld = gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight;
            float packedW = (data.HasTileGroup ? 1f : 0f);

            if (data.HasTileGroup && descriptor != 0)
            {
                if ((descriptor & 0x40) != 0) { (_uvs[vIdx + 0].x, _uvs[vIdx + 1].x) = (_uvs[vIdx + 1].x, _uvs[vIdx + 0].x); (_uvs[vIdx + 3].x, _uvs[vIdx + 2].x) = (_uvs[vIdx + 2].x, _uvs[vIdx + 3].x); }
                if ((descriptor & 0x20) != 0) { (_uvs[vIdx + 0].y, _uvs[vIdx + 3].y) = (_uvs[vIdx + 3].y, _uvs[vIdx + 0].y); (_uvs[vIdx + 1].y, _uvs[vIdx + 2].y) = (_uvs[vIdx + 2].y, _uvs[vIdx + 1].y); }
                if ((descriptor & 0x80) != 0) { Vector2 t = _uvs[vIdx + 0]; _uvs[vIdx + 0] = _uvs[vIdx + 1]; _uvs[vIdx + 1] = _uvs[vIdx + 2]; _uvs[vIdx + 2] = _uvs[vIdx + 3]; _uvs[vIdx + 3] = t; }
            }

            bool useFallback = _useColorLod || data.AtlasRect.z < 0.0001f;
            Color color = useFallback ? data.MinimapColor : _shimmerHighlightColor;
            float animOffset = 0f;
            if (!useFallback && data.Animation == CellAnimationType.Blinking)
            {
                uint seed = (uint)(gridX * 374761397 + serverY * 668265263);
                seed = (seed ^ (seed >> 13)) * 1274126177; seed = seed ^ (seed >> 16);
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
                _colors[vIdx + i] = color; _subAtlasRects[vIdx + i] = data.AtlasRect;
                _tileSizeUVs[vIdx + i] = tileSizeVec; _worldPositions[vIdx + i] = worldPosVec;
                _animationData[vIdx + i] = animDataVec;

                float shadowVal = _gridShadowValues[x + (i == 1 || i == 2 ? 1 : 0), y + (i == 2 || i == 3 ? 1 : 0)];
                _packedReliefShadowLocalUV[vIdx + i] = new Vector4(textureType, isRelief ? reliefMask : shadowVal, _localUVsBuffer[i].x, _localUVsBuffer[i].y);
            }

            _subMeshIndices[atlasIndex].Add(vIdx + 0); _subMeshIndices[atlasIndex].Add(vIdx + 3); _subMeshIndices[atlasIndex].Add(vIdx + 2);
            _subMeshIndices[atlasIndex].Add(vIdx + 2); _subMeshIndices[atlasIndex].Add(vIdx + 1); _subMeshIndices[atlasIndex].Add(vIdx + 0);
            vIdx += 4;
        }

        private ref CachedCellData GetNeighborCacheEntry(CellType type, int cx, int cy, List<TextureAtlas> atlases)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (_cellCache[cx + dx, cy + dy].Type == type) return ref _cellCache[cx + dx, cy + dy];

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
            Array.Clear(_bgMapBuffer, 0, _bgMapBuffer.Length); _floodFillQueue.Clear();
            for (int y = 0; y < _meshHeight; y++)
            {
                for (int x = 0; x < _meshWidth; x++)
                {
                    var cell = _cellCache[x + 1, y + 1];
                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        _bgMapBuffer[x, y] = cell.Type; _floodFillQueue.Enqueue((x, y));
                    }
                }
            }
            _pass2Cells.Clear(); Span<TypeCount> typeCounts = stackalloc TypeCount[8];
            for (int y = 0; y < _meshHeight; y++)
            {
                for (int x = 0; x < _meshWidth; x++)
                {
                    if (_bgMapBuffer[x, y] != 0) continue;
                    int distinctCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= _meshWidth || ny < 0 || ny >= _meshHeight) continue;
                            var n = _cellCache[nx + 1, ny + 1];
                            if ((n.Properties & CellConfigProperties.Passable) != 0)
                            {
                                bool found = false;
                                for (int i = 0; i < distinctCount; i++) if (typeCounts[i].type == n.Type) { typeCounts[i].count++; found = true; break; }
                                if (!found && distinctCount < 8) typeCounts[distinctCount++] = new TypeCount { type = n.Type, count = 1 };
                            }
                        }
                    }
                    if (distinctCount > 0)
                    {
                        CellType mostFrequent = typeCounts[0].type; int maxC = typeCounts[0].count;
                        for (int i = 1; i < distinctCount; i++) if (typeCounts[i].count > maxC) { maxC = typeCounts[i].count; mostFrequent = typeCounts[i].type; }
                        _bgMapBuffer[x, y] = mostFrequent; _pass2Cells.Add((x, y));
                    }
                }
            }
            foreach (var cell in _pass2Cells) _floodFillQueue.Enqueue(cell);
            while (_floodFillQueue.Count > 0)
            {
                var (x, y) = _floodFillQueue.Dequeue();
                CellType currentBg = _bgMapBuffer[x, y];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= _meshWidth || ny < 0 || ny >= _meshHeight) continue;
                        if (_bgMapBuffer[nx, ny] == CellType.Unloaded) { _bgMapBuffer[nx, ny] = currentBg; _floodFillQueue.Enqueue((nx, ny)); }
                    }
                }
            }
            for (int y = 0; y < _meshHeight; y++)
                for (int x = 0; x < _meshWidth; x++)
                    if (_bgMapBuffer[x, y] == CellType.Unloaded) _bgMapBuffer[x, y] = CellType.Empty;
        }

        private struct TypeCount { public CellType type; public int count; }

        private void CleanupMaterials()
        {
            if (_materials != null) foreach (var mat in _materials) if (mat != null) { if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat); }
        }

        public void SetColorLod(bool enabled)
        {
            if (_useColorLod == enabled) return;
            _useColorLod = enabled;
            _metadataCache.Clear();
            _needsRefresh = true;
        }

        private void PreloadVisibleChunks()
        {
            if (MapStorage.Instance?.cellLayer == null) return;

            int cs = 32;
            int chunkX = Mathf.FloorToInt(transform.position.x / cs + _meshWidth / 2f / cs);
            int chunkY = Mathf.FloorToInt(transform.position.y / cs + _meshHeight / 2f / cs);
            var chunkPos = new Vector2Int(chunkX, chunkY);

            if (chunkPos == _lastPreloadChunkPos) return;
            _lastPreloadChunkPos = chunkPos;

            int radius = 4;
            int hc = MapStorage.Instance.cellLayer._heightChunks;

            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int wx = (chunkX + dx) * cs;
                    int wy = (chunkY + dy) * cs;
                    MapStorage.Instance.PreloadChunkAtCoordinateAsync(wx, wy).Forget();
                }
        }

    }
}
