using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        // Pre-allocated interleaved vertex buffer (replaces 7 separate arrays)
        private TerrainVertex[] _vertexBuffer;

        // Mesh update flags to skip validation on every frame upload.
        // DontRecalculateBounds is omitted because _mesh.Clear() resets bounds to zero,
        // which would cause the mesh to be frustum-culled. Bounds must auto-recalculate.
        private const MeshUpdateFlags UploadFlags =
            MeshUpdateFlags.DontValidateIndices;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _lastPreloadChunkPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;
        private float _targetSimpleGraphics;

        /// <summary>
        /// Interleaved vertex format matching the terrain shader's inputs.
        /// Replaces 8 separate arrays with a single blittable struct for efficient GPU upload.
        /// Layout via LayoutKind.Explicit ensures no padding and exact stride matching
        /// the VertexAttributeDescriptor array.
        /// </summary>
        /// <summary>
        /// Vertex attribute layout descriptor for SetVertexBufferParams.
        /// Must match TerrainVertex field order and sizes exactly.
        /// </summary>
        /// <summary>
        /// Vertex attribute order must match TerrainVertex field order exactly:
        /// Position → Color → TexCoord0 → TexCoord1 → TexCoord2 → TexCoord3 → TexCoord4 → TexCoord5.
        /// This is Unity's preferred vertex attribute order (submitting in any other order
        /// triggers a reordering warning and causes GPU buffer misalignment).
        /// </summary>
        private static readonly VertexAttributeDescriptor[] VertexLayout = new VertexAttributeDescriptor[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4),
        };

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
        private int _cacheMinX = int.MinValue;
        private int _cacheMinY = int.MinValue;
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

        // FBPW (Frontier-Based Parallel Wavefront) state for lock-free parallel flood fill.
        // _fbpwGeneration is a generation-counter barrier array used with Interlocked.CompareExchange
        // to claim cells for the current wave without locks. _fbpwCurrentGen increments each wave,
        // so a cell is "unclaimed" in wave N if _fbpwGeneration[cell] < N.
        private int[] _fbpwGeneration;
        private int _fbpwCurrentGen = 1;
        private readonly List<(int X, int Y)> _fbpwFrontier = new(64);
        private readonly List<(int X, int Y)> _fbpwNextFrontier = new(64);
        private readonly object _fbpwLock = new();

        private CellType[,] _bgMapBuffer;
        private bool _needsRefresh = false;
        private bool _useColorLod = false;

        // Atlas index cache: maps CellType → atlas index for O(1) lookup.
        // Invalidated when atlas count changes.
        private readonly Dictionary<CellType, int> _atlasIndexCache = new();
        private int _lastAtlasCount = -1;

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
            _targetSimpleGraphics = PlayerPrefs.GetInt("SimpleGraphics", 0) == 1 ? 1f : 0f;
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
                _cacheMinX = int.MinValue;
                _cacheMinY = int.MinValue;
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

            // (Note: _WorldOffset uniform removed — UV3 now carries worldPos directly)
            // Rebuild happens every frame the grid position changes (no throttle),
            // matching the original code behavior. The incremental scroll + border-only
            // fill keeps the per-frame cost well below the old full rebuild.
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

            if (_vertexBuffer == null || _vertexBuffer.Length != vertCount)
            {
                _vertexBuffer = new TerrainVertex[vertCount];
            }

            _bgMapBuffer = new CellType[_meshWidth, _meshHeight];
            _cacheWidth = _meshWidth + 2;
            _cacheHeight = _meshHeight + 2;
            _cellCache = new CachedCellData[_cacheWidth, _cacheHeight];
            _fbpwGeneration = new int[_meshWidth * _meshHeight];
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

            // Use cached atlas index to avoid scanning atlas list.
            // Cache is invalidated in UpdateVertexAttributes when atlas count changes.
            if (!_atlasIndexCache.TryGetValue(type, out int atlasIndex))
            {
                for (int i = 0; i < atlases.Count; i++)
                {
                    if (atlases[i].ContainsCell(type))
                    {
                        atlasIndex = i;
                        _atlasIndexCache[type] = i;
                        break;
                    }
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
                int gridX = _cacheMinX + x;
                int lastChunkIndex = -1;
                CellType[] currentChunk = null;

                for (int y = 0; y < _cacheHeight; y++)
                {
                    int unityY = _cacheMinY + y;

                    CellType type;
                    if (gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight)
                    {
                        // Outside world: sides/bottom render as rock, top is drawn by SurfaceRenderer
                        if (gridX < 0 || gridX >= worldWidth || unityY < 0)
                            type = (CellType)0;
                        else
                            type = CellType.Unloaded;
                    }
                    else
                    {
                        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

                        if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
                        {
                            type = CellType.Unloaded;
                        }
                        else
                        {
                            if (chunkIndex != lastChunkIndex)
                            {
                                currentChunk = layer.GetChunk(chunkIndex, false, true);
                                lastChunkIndex = chunkIndex;
                            }

                            type = currentChunk != null ? currentChunk[localIndex] : CellType.Unloaded;
                        }
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

                    if (Application.isPlaying && type != CellType.Unloaded && !meta.IsTextureReady)
                    {
                        WorldTextureManager.Instance?.RequestTexture(type);
                    }
                }
            }

            WorldTextureManager.Instance.RequestTexture((CellType)0);
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

            bool materialsChanged = false;

            // Skip material/submesh validity checks when atlas count hasn't changed
            // (the common case after initial setup).
            if (atlases.Count != _lastAtlasCount)
            {
                _lastAtlasCount = atlases.Count;

                // Atlas count changed — invalidate cache since atlas indices may have shifted.
                _atlasIndexCache.Clear();

                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                int estimatedPerAtlas = (_meshWidth * _meshHeight * 2 * 6 / atlases.Count) + 16;
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new List<int>(estimatedPerAtlas);
                    _materials[i] = new Material(_terrainShader);
                }

                materialsChanged = true;
            }
            else
            {
                // Reset indices lists (Clear is allocation-free, just resets internal _size).
                // Growth-only capacity policy: never shrink, only grow when estimate increases.
                int estimatedPerAtlas = (_meshWidth * _meshHeight * 2 * 6 / _subMeshIndices.Length) + 16;
                for (int i = 0; i < _subMeshIndices.Length; i++)
                {
                    var list = _subMeshIndices[i];
                    list.Clear();
                    if (list.Capacity < estimatedPerAtlas)
                    {
                        list.Capacity = estimatedPerAtlas;
                    }
                }
            }

            // Ensure all pending atlas texture uploads are committed to the GPU
            // before building mesh data that references atlas coordinates.
            // This replaces 80+ sequential full-atlas Apply() calls with at most one per frame.
            wtm.FlushDirtyAtlases();

            // Decide: incremental scroll or full rebuild.
            // Full rebuild is needed on first frame, when textures reload, or when the camera
            // jumped by more than half the viewport.
            // _cacheMinX/Y is set to (minX - 1, minY - 1) by PopulateCellCache (padding offset),
            // so compute scroll delta in cache-coordinate space.
            // Only allow incremental scroll if the cell cache has been fully populated at least once.
            // _cacheMinX starts as int.MinValue and gets set to a real cache coordinate by PopulateCellCache
            // (called from FullRebuild). On the very first frame, before any FullRebuild, _cacheMinX is still
            // int.MinValue, so canScroll is false and a FullRebuild is forced — populating the cache correctly.
            bool canScroll = _cacheMinX != int.MinValue && !_needsRefresh;
            int dx = 0, dy = 0;
            if (canScroll)
            {
                int newCacheMinX = minX - 1;
                int newCacheMinY = minY - 1;
                dx = newCacheMinX - _cacheMinX;
                dy = newCacheMinY - _cacheMinY;
                canScroll = Mathf.Abs(dx) < _cacheWidth && Mathf.Abs(dy) < _cacheHeight;
            }

            if (canScroll)
            {
                IncrementalUpdate(minX, minY, dx, dy, atlases);
            }
            else
            {
                FullRebuild(minX, minY, atlases);
            }

            // Material reassignment (only when changed).
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

        /// <summary>
        /// Full terrain rebuild. Reads every visible cell from WorldLayer and recomputes
        /// all derived data. Runs on first frame or after large camera jumps / texture reloads.
        /// </summary>
        private void FullRebuild(int minX, int minY, List<TextureAtlas> atlases)
        {
            PopulateCellCache(minX, minY);
            PrecalculateData();
            ComputeBackgroundMap();
            BuildMeshData(minX, minY, atlases, clearMesh: true);
        }

        /// <summary>
        /// Incremental update when the camera scrolls by a small offset (dx, dy).
        /// Scrolls existing cell cache to avoid re-reading visible cells from WorldLayer,
        /// then incrementally recomputes derived data — only the border region that
        /// actually changed (|dx| × |dy| cells instead of the full viewport).
        /// </summary>
        private void IncrementalUpdate(int minX, int minY, int dx, int dy, List<TextureAtlas> atlases)
        {
            // Scroll cell cache by (dx, dy) in-place, filling new edge cells from WorldLayer.
            ScrollCellCache(dx, dy, atlases);

            // Incrementally recompute derived data: scroll existing buffers, then
            // only recompute the |dx|/|dy|-cell border on each exposed edge.
            // Interior cells were scrolled together — their neighborhood relationships
            // are preserved, so their tiling, relief, and background values are still valid.
            IncrementalPrecalculateData(dx, dy);
            IncrementalComputeBackgroundMap(dx, dy);
            BuildMeshDataIncremental(minX, minY, dx, dy, atlases);
        }

        /// <summary>
        /// Scrolls a 2D array in-place by (dx, dy) cells. Direction-safe iteration
        /// ensures source data is never overwritten before being read.
        /// </summary>
        private static void Scroll2DArray<T>(T[,] buffer, int w, int h, int dx, int dy)
        {
            if (dx > 0)
            {
                for (int x = 0; x < w - dx; x++)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++)
                            buffer[x, y] = buffer[srcX, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--)
                            buffer[x, y] = buffer[srcX, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                            buffer[x, y] = buffer[srcX, y];
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = w - 1; x >= -dx; x--)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < h - dy; y++)
                            buffer[x, y] = buffer[srcX, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = h - 1; y >= -dy; y--)
                            buffer[x, y] = buffer[srcX, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                            buffer[x, y] = buffer[srcX, y];
                    }
                }
            }
            else if (dy != 0)
            {
                if (dy > 0)
                {
                    for (int x = 0; x < w; x++)
                        for (int y = 0; y < h - dy; y++)
                            buffer[x, y] = buffer[x, y + dy];
                }
                else
                {
                    for (int x = 0; x < w; x++)
                        for (int y = h - 1; y >= -dy; y--)
                            buffer[x, y] = buffer[x, y + dy];
                }
            }
        }

        /// <summary>
        /// Incremental PrecalculateData: scrolls existing vertex/cell arrays by (dx, dy)
        /// then only recomputes the |dx|/|dy|-cell border on each exposed edge where new
        /// cell data was just fetched. Eliminates ~98% of per-frame vertex precomputation.
        ///
        /// The scroll preserves neighborhood relationships in the interior, so previously
        /// computed vertex offsets, shadow values, tiling descriptors, and relief masks
        /// remain valid for the scrolled portions.
        /// </summary>
        private void IncrementalPrecalculateData(int dx, int dy)
        {
            int gw = _meshWidth + 1;
            int gh = _meshHeight + 1;

            // Scroll vertex arrays (gw × gh) by (dx, dy)
            Scroll2DArray(_gridVertexOffsets, gw, gh, dx, dy);
            Scroll2DArray(_gridShadowValues, gw, gh, dx, dy);

            // Scroll cell arrays (_meshWidth × _meshHeight) by (dx, dy)
            Scroll2DArray(_cellTilingDescriptors, _meshWidth, _meshHeight, dx, dy);
            Scroll2DArray(_cellReliefMasks, _meshWidth, _meshHeight, dx, dy);
            Scroll2DArray(_cellIsRelief, _meshWidth, _meshHeight, dx, dy);

            // --- Determine vertex border range ---
            // The scroll copies w - |dx| entries, leaving the last |dx| entries with
            // stale values. These stale entries are exactly the new edge region.
            int vxStart = 0, vxLen = 0, vyStart = 0, vyLen = 0;
            if (dx > 0) { vxStart = gw - dx; vxLen = dx; }
            else if (dx < 0) { vxStart = 0; vxLen = -dx; }

            if (dy > 0) { vyStart = gh - dy; vyLen = dy; }
            else if (dy < 0) { vyStart = 0; vyLen = -dy; }

            // --- Recompute vertex border ---
            // Vertices at the exposed edge depend on newly fetched cells.
            // Only the last |dx| columns / |dy| rows of the vertex grid are stale.
            bool hasVBorder = vxLen > 0 || vyLen > 0;
            if (hasVBorder)
            {
                // Full-height x-border columns
                if (vxLen > 0)
                {
                    for (int x = vxStart; x < vxStart + vxLen; x++)
                    {
                        for (int y = 0; y < gh; y++)
                        {
                            int cx = x + 1;
                            int cy = y + 1;
                            var tl = _cellCache[x, cy];
                            var tr = _cellCache[cx, cy];
                            var bl = _cellCache[x, y];
                            var br = _cellCache[cx, y];

                            if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block ||
                                bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block)
                            {
                                _gridVertexOffsets[x, y] = Vector3.zero;
                            }
                            else
                            {
                                int xSign = 0, ySign = 0;
                                if (bl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
                                if (br.Distortion == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
                                if (tl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
                                if (tr.Distortion == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }

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
                                    _gridVertexOffsets[x, y] = new Vector3(
                                        xSign > 0 ? r : (xSign < 0 ? -r : 0),
                                        ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
                                }
                            }

                            bool hasC = (bl.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                        (br.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                        (tl.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                        (tr.Properties & CellConfigProperties.DropsShadow) != 0;
                            bool hasR = (bl.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                        (br.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                        (tl.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                        (tr.Properties & CellConfigProperties.ReceivesShadow) != 0;
                            _gridShadowValues[x, y] = (hasC && hasR) ? 0.7f : 0.0f;
                        }
                    }
                }

                // Full-width y-border rows (excluding x-border corners already done above)
                // When both dx and dy are non-zero, the x-border vertex columns cover
                // [vxStart, vxStart+vxLen). The y-border loop must skip those columns
                // and only process [0, vxStart) (dx>0) or [vxLen, gw) (dx<0).
                // Naively computing xStart = vxStart + vxLen overshoots when dx>0
                // (vxStart + vxLen = gw → skip), leaving the y-border non-x region stale.
                if (vyLen > 0 && vxLen < gw)
                {
                    int xStart, xEnd;
                    if (vxLen > 0)
                    {
                        if (dx > 0)
                        {
                            xStart = 0;
                            xEnd = vxStart;
                        }
                        else
                        {
                            xStart = vxLen;
                            xEnd = gw;
                        }
                    }
                    else
                    {
                        xStart = 0;
                        xEnd = gw;
                    }

                    if (xStart < xEnd)
                    {
                        for (int y = vyStart; y < vyStart + vyLen; y++)
                        {
                            for (int x = xStart; x < xEnd; x++)
                            {
                                int cx = x + 1;
                                int cy = y + 1;
                                var tl = _cellCache[x, cy];
                                var tr = _cellCache[cx, cy];
                                var bl = _cellCache[x, y];
                                var br = _cellCache[cx, y];

                                if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block ||
                                    bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block)
                                {
                                    _gridVertexOffsets[x, y] = Vector3.zero;
                                }
                                else
                                {
                                    int xSign = 0, ySign = 0;
                                    if (bl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
                                    if (br.Distortion == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
                                    if (tl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
                                    if (tr.Distortion == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }

                                    if (xSign == 0 && ySign == 0)
                                        _gridVertexOffsets[x, y] = Vector3.zero;
                                    else
                                    {
                                        uint seed = (uint)((_cacheMinX + cx) * 374761397 + (_cacheMinY + cy) * 668265263);
                                        seed = (seed ^ (seed >> 13)) * 1274126177;
                                        seed = seed ^ (seed >> 16);
                                        float r = ((seed % 4) + 1) * 0.0625f;
                                        uint seed2 = seed * 2654435761u;
                                        float ry = ((seed2 % 4) + 1) * 0.0625f;
                                        _gridVertexOffsets[x, y] = new Vector3(
                                            xSign > 0 ? r : (xSign < 0 ? -r : 0),
                                            ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
                                    }
                                }

                                bool hasC = (bl.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                            (br.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                            (tl.Properties & CellConfigProperties.DropsShadow) != 0 ||
                                            (tr.Properties & CellConfigProperties.DropsShadow) != 0;
                                bool hasR = (bl.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                            (br.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                            (tl.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                                            (tr.Properties & CellConfigProperties.ReceivesShadow) != 0;
                                _gridShadowValues[x, y] = (hasC && hasR) ? 0.7f : 0.0f;
                            }
                        }
                    }
                }
            }

            // --- Determine cell border range ---
            int cxStart = 0, cxLen = 0, cyStart = 0, cyLen = 0;
            if (dx > 0) { cxStart = _meshWidth - dx; cxLen = dx; }
            else if (dx < 0) { cxStart = 0; cxLen = -dx; }

            if (dy > 0) { cyStart = _meshHeight - dy; cyLen = dy; }
            else if (dy < 0) { cyStart = 0; cyLen = -dy; }

            bool hasCBorder = cxLen > 0 || cyLen > 0;
            if (hasCBorder)
            {
                // Full-height x-border cell columns
                if (cxLen > 0)
                {
                    for (int x = cxStart; x < cxStart + cxLen; x++)
                    {
                        int cx = x + 1;
                        for (int y = 0; y < _meshHeight; y++)
                        {
                            int cy = y + 1;
                            var data = _cellCache[cx, cy];

                            // Tiling descriptor
                            if (data.HasTileGroup)
                            {
                                byte m = 0;
                                if (_cellCache[cx - 1, cy].HasTileGroup && _cellCache[cx - 1, cy].TileGroupId == data.TileGroupId)
                                    m |= 1 << 0;
                                if (_cellCache[cx - 1, cy - 1].HasTileGroup && _cellCache[cx - 1, cy - 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 1;
                                if (_cellCache[cx, cy - 1].HasTileGroup && _cellCache[cx, cy - 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 2;
                                if (_cellCache[cx + 1, cy - 1].HasTileGroup && _cellCache[cx + 1, cy - 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 3;
                                if (_cellCache[cx + 1, cy].HasTileGroup && _cellCache[cx + 1, cy].TileGroupId == data.TileGroupId)
                                    m |= 1 << 4;
                                if (_cellCache[cx + 1, cy + 1].HasTileGroup && _cellCache[cx + 1, cy + 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 5;
                                if (_cellCache[cx, cy + 1].HasTileGroup && _cellCache[cx, cy + 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 6;
                                if (_cellCache[cx - 1, cy + 1].HasTileGroup && _cellCache[cx - 1, cy + 1].TileGroupId == data.TileGroupId)
                                    m |= 1 << 7;

                                _cellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
                            }
                            else
                            {
                                _cellTilingDescriptors[x, y] = 0;
                            }

                            // Relief mask
                            byte rm = 0;
                            bool isR = false;
                            if (_cellCache[cx, cy + 1].ReliefGroup >= data.ReliefGroup) { rm |= 1; }
                            else { isR = true; }
                            if (_cellCache[cx - 1, cy].ReliefGroup >= data.ReliefGroup) { rm |= 2; }
                            else { isR = true; }
                            if (_cellCache[cx, cy - 1].ReliefGroup >= data.ReliefGroup) { rm |= 4; }
                            else { isR = true; }
                            if (_cellCache[cx + 1, cy].ReliefGroup >= data.ReliefGroup) { rm |= 8; }
                            else { isR = true; }
                            _cellReliefMasks[x, y] = rm;
                            _cellIsRelief[x, y] = isR;
                        }
                    }
                }

                // Full-width y-border cell rows (excluding x-border corners already done above)
                // Same fix as the vertex y-border section — compute correct non-x-column range.
                if (cyLen > 0 && cxLen < _meshWidth)
                {
                    int xStart, xEnd;
                    if (cxLen > 0)
                    {
                        if (dx > 0)
                        {
                            xStart = 0;
                            xEnd = cxStart;
                        }
                        else
                        {
                            xStart = cxLen;
                            xEnd = _meshWidth;
                        }
                    }
                    else
                    {
                        xStart = 0;
                        xEnd = _meshWidth;
                    }

                    if (xStart < xEnd)
                    {
                        for (int y = cyStart; y < cyStart + cyLen; y++)
                        {
                            int cy = y + 1;
                            for (int x = xStart; x < xEnd; x++)
                            {
                                int cx = x + 1;
                                var data = _cellCache[cx, cy];

                                if (data.HasTileGroup)
                                {
                                    byte m = 0;
                                    if (_cellCache[cx - 1, cy].HasTileGroup && _cellCache[cx - 1, cy].TileGroupId == data.TileGroupId)
                                        m |= 1 << 0;
                                    if (_cellCache[cx - 1, cy - 1].HasTileGroup && _cellCache[cx - 1, cy - 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 1;
                                    if (_cellCache[cx, cy - 1].HasTileGroup && _cellCache[cx, cy - 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 2;
                                    if (_cellCache[cx + 1, cy - 1].HasTileGroup && _cellCache[cx + 1, cy - 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 3;
                                    if (_cellCache[cx + 1, cy].HasTileGroup && _cellCache[cx + 1, cy].TileGroupId == data.TileGroupId)
                                        m |= 1 << 4;
                                    if (_cellCache[cx + 1, cy + 1].HasTileGroup && _cellCache[cx + 1, cy + 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 5;
                                    if (_cellCache[cx, cy + 1].HasTileGroup && _cellCache[cx, cy + 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 6;
                                    if (_cellCache[cx - 1, cy + 1].HasTileGroup && _cellCache[cx - 1, cy + 1].TileGroupId == data.TileGroupId)
                                        m |= 1 << 7;

                                    _cellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
                                }
                                else
                                {
                                    _cellTilingDescriptors[x, y] = 0;
                                }

                                byte rm = 0;
                                bool isR = false;
                                if (_cellCache[cx, cy + 1].ReliefGroup >= data.ReliefGroup) { rm |= 1; }
                                else { isR = true; }
                                if (_cellCache[cx - 1, cy].ReliefGroup >= data.ReliefGroup) { rm |= 2; }
                                else { isR = true; }
                                if (_cellCache[cx, cy - 1].ReliefGroup >= data.ReliefGroup) { rm |= 4; }
                                else { isR = true; }
                                if (_cellCache[cx + 1, cy].ReliefGroup >= data.ReliefGroup) { rm |= 8; }
                                else { isR = true; }
                                _cellReliefMasks[x, y] = rm;
                                _cellIsRelief[x, y] = isR;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Incremental background map recomputation using Frontier-Based Parallel
        /// Wavefront (FBPW). Scrolls bgMapBuffer by (dx, dy), then only processes
        /// the exposed border region with parallel scan + wavefront propagation.
        ///
        /// The interior bgMapBuffer is already valid after the scroll, so the
        /// border-only frontier stops upon reaching known interior cells.
        /// </summary>
        private void IncrementalComputeBackgroundMap(int dx, int dy)
        {
            // Scroll bgMapBuffer by (dx, dy)
            Scroll2DArray(_bgMapBuffer, _meshWidth, _meshHeight, dx, dy);

            // Determine border range (stale entries not overwritten by scroll)
            int xStart = 0, xLen = 0, yStart = 0, yLen = 0;
            if (dx > 0) { xStart = _meshWidth - dx; xLen = dx; }
            else if (dx < 0) { xStart = 0; xLen = -dx; }

            if (dy > 0) { yStart = _meshHeight - dy; yLen = dy; }
            else if (dy < 0) { yStart = 0; yLen = -dy; }

            bool hasXBorder = xLen > 0;
            bool hasYBorder = yLen > 0;

            if (!hasXBorder && !hasYBorder) { return; }

            // Phase 1+2: Serial scan of border cells only (border is small — no Parallel.For overhead needed)
            var frontier = _fbpwFrontier;
            int w = _meshWidth, h = _meshHeight;
            frontier.Clear();

            // Full-height x-border columns
            if (hasXBorder)
            {
                for (int x = xStart; x < xStart + xLen; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        SeedBorderCell(x, y, frontier);
                    }
                }
            }

            // Full-width y-border rows (excluding x-border corners)
            if (hasYBorder)
            {
                int x2Start = hasXBorder ? xStart + xLen : 0;
                for (int y = yStart; y < yStart + yLen; y++)
                {
                    for (int x = x2Start; x < w; x++)
                    {
                        SeedBorderCell(x, y, frontier);
                    }
                }
            }

            // Phase 3: FBPW wavefront propagation (single-threaded — border is small, Parallel.For overhead dominates)
            FBPWPropagate(frontier, useParallel: false);

            // Phase 4: Safety sweep on border region only
            if (hasXBorder)
            {
                for (int x = xStart; x < xStart + xLen; x++)
                    for (int y = 0; y < h; y++)
                        if (_bgMapBuffer[x, y] == CellType.Unloaded)
                            _bgMapBuffer[x, y] = CellType.Empty;
            }

            if (hasYBorder)
            {
                int xSweepStart = hasXBorder ? xStart + xLen : 0;
                for (int y = yStart; y < yStart + yLen; y++)
                    for (int x = xSweepStart; x < w; x++)
                        if (_bgMapBuffer[x, y] == CellType.Unloaded)
                            _bgMapBuffer[x, y] = CellType.Empty;
            }
        }

        /// <summary>
        /// Fills a single border cell in bgMapBuffer during the incremental scan.
        /// Passable cells get their own type; non-passable cells vote from their
        /// passable 8-neighbors for the most frequent type.
        /// </summary>
        private void SeedBorderCell(int x, int y, List<(int, int)> frontier)
        {
            var cell = _cellCache[x + 1, y + 1];
            if ((cell.Properties & CellConfigProperties.Passable) != 0)
            {
                _bgMapBuffer[x, y] = cell.Type;
                lock (_fbpwLock) { frontier.Add((x, y)); }
            }
            else
            {
                Span<TypeCount> typeCounts = stackalloc TypeCount[8];
                int distinctCount = 0;
                int w = _meshWidth, h = _meshHeight;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) { continue; }

                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) { continue; }

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
                    lock (_fbpwLock) { frontier.Add((x, y)); }
                }
            }
        }

        /// <summary>
        /// Frontier-Based Parallel Wavefront (FBPW) propagation.
        ///
        /// Processes all frontier cells in parallel using a generation-counter barrier
        /// for lock-free cell claiming. Each wave: all frontier cells expand to their
        /// 8-neighbors; newly filled cells become the next wave's frontier. Repeats
        /// until no new cells are discovered (flood fill converged).
        ///
        /// Thread safety:
        /// - _fbpwGeneration[] is an int array used with Interlocked.CompareExchange
        ///   to atomically claim an Unloaded cell. Each wave has a unique generation
        ///   number, so cells claimed in older waves are never re-processed.
        /// - Per-thread local frontier lists avoid lock contention during accumulation.
        /// </summary>
        /// <summary>
        /// Processes frontier cells using FBPW (Frontier-Based Parallel Wavefront) propagation.
        /// Full-rebuild paths (large frontier) use Parallel.For; incremental paths (small border
        /// frontier, typically 2–64 cells) use a single-threaded loop to avoid thread-pool overhead.
        /// </summary>
        private void FBPWPropagate(List<(int, int)> frontier, bool useParallel = false)
        {
            if (frontier.Count == 0) { return; }

            int w = _meshWidth, h = _meshHeight;

            while (frontier.Count > 0)
            {
                _fbpwNextFrontier.Clear();
                int gen = _fbpwCurrentGen++;

                // Guard against absurd overflow (would take weeks at 60 FPS)
                if (_fbpwCurrentGen >= int.MaxValue - 1)
                {
                    Array.Clear(_fbpwGeneration, 0, _fbpwGeneration.Length);
                    _fbpwCurrentGen = 1;
                }

                if (useParallel)
                {
                    Parallel.For(
                        0,
                        frontier.Count,
                        () => new List<(int, int)>(16),
                        (i, state, local) =>
                        {
                            var (x, y) = frontier[i];
                            CellType bg = _bgMapBuffer[x, y];

                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) { continue; }

                                    int nx = x + dx;
                                    int ny = y + dy;
                                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) { continue; }

                                    // Skip cells already filled (not Unloaded)
                                    if (_bgMapBuffer[nx, ny] != CellType.Unloaded) { continue; }

                                    // Atomically claim this cell for the current wave via generation barrier
                                    int idx = nx + (ny * w);
                                    if (Interlocked.CompareExchange(ref _fbpwGeneration[idx], gen, gen - 1) != gen - 1)
                                    {
                                        continue;
                                    }

                                    _bgMapBuffer[nx, ny] = bg;
                                    local.Add((nx, ny));
                                }
                            }

                            return local;
                        },
                        local =>
                        {
                            if (local.Count > 0)
                            {
                                lock (_fbpwLock) { _fbpwNextFrontier.AddRange(local); }
                            }
                        });
                }
                else
                {
                    foreach (var (x, y) in frontier)
                    {
                        CellType bg = _bgMapBuffer[x, y];

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) { continue; }

                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx < 0 || nx >= w || ny < 0 || ny >= h) { continue; }

                                if (_bgMapBuffer[nx, ny] != CellType.Unloaded) { continue; }

                                int idx = nx + (ny * w);
                                if (Interlocked.CompareExchange(ref _fbpwGeneration[idx], gen, gen - 1) != gen - 1)
                                {
                                    continue;
                                }

                                _bgMapBuffer[nx, ny] = bg;
                                _fbpwNextFrontier.Add((nx, ny));
                            }
                        }
                    }
                }

                // Swap frontiers for next wave
                var temp = frontier;
                frontier.Clear();
                frontier.AddRange(_fbpwNextFrontier);
                _fbpwNextFrontier.Clear();
            }
        }

        /// <summary>
        /// Builds vertex/index buffers from cached cell/precalc data and uploads to the GPU.
        /// Shared between full rebuild and incremental paths.
        /// </summary>
        private void BuildMeshData(int minX, int minY, List<TextureAtlas> atlases, bool clearMesh = false)
        {
            var mm = MapManager.Instance;
            int vIdx = 0;
            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;
            for (int x = 0; x < _meshWidth; x++)
            {
                int gridX = minX + x;
                for (int y = 0; y < _meshHeight; y++)
                {
                    int unityY = minY + y;
                    FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, true, ref vIdx, atlases);
                    FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, false, ref vIdx, atlases);
                }
            }

            if (clearMesh)
            {
                _mesh.Clear();
            }

            // subMeshCount must be set before vertex buffer setup to avoid resetting vertex data.
            _mesh.subMeshCount = atlases.Count;

            // Single interleaved vertex buffer upload replaces 7 separate SetVertices/SetUVs/SetColors calls.
            _mesh.SetVertexBufferParams(_vertexBuffer.Length, VertexLayout);
            _mesh.SetVertexBufferData(_vertexBuffer, 0, 0, _vertexBuffer.Length, 0, UploadFlags);

            // SetVertexBufferData does NOT recalculate bounds (unlike SetVertices).
            // Without this explicit call, the mesh has zero bounds and is frustum-culled.
            _mesh.RecalculateBounds();
            var wtm = WorldTextureManager.Instance;
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
                    _materials[i].SetFloat("_SimpleGraphics", _targetSimpleGraphics);
                }

                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
            }

            // Note: UploadMeshData(false) was intentionally removed.
            // SetVertices/SetUVs/SetIndices already mark the mesh for GPU upload;
            // an explicit UploadMeshData call would force a redundant GPU sync point.
        }

        /// <summary>
        /// Incremental mesh data rebuild: scrolls existing vertex data in-place so only
        /// border cells (those entering the viewport) need FillQuadData. Interior cells
        /// are handled by ScrollVertexBuffer which shifts their data + adjusts positions.
        /// Skips RecalculateBounds and SetIndices since mesh topology/bounds don't change.
        /// </summary>
        private void BuildMeshDataIncremental(int minX, int minY, int dx, int dy, List<TextureAtlas> atlases)
        {
            var mm = MapManager.Instance;
            int mw = _meshWidth;
            int mh = _meshHeight;
            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;

            // Step 1: Scroll vertex buffer in-place.
            ScrollVertexBuffer(dx, dy);

            // Step 1.5: Fix UV3 for all cells.
            // ScrollVertexBuffer copies the entire TerrainVertex struct including UV3,
            // so scrolled interior cells have stale UV3.xy from their source.
            // Fix: reset UV3.xy to (gridX, unityY) = world position for every vertex.
            // Border cells will be overwritten again by FillQuadData in Step 2
            // (harmlessly redundant writes).
            int rowStride = mh * 8;
            for (int x = 0; x < mw; x++)
            {
                int colBase = x * rowStride;
                float gridX = minX + x;
                for (int y = 0; y < mh; y++)
                {
                    int cellBase = colBase + y * 8;
                    float unityY = minY + y;
                    int serverY = CoordinateUtils.UnityToServerY((int)unityY, worldHeight);
                    Vector4 uv3Base = new Vector4(gridX, serverY, 0, 0);
                    for (int v = 0; v < 8; v++)
                    {
                        TerrainVertex vert = _vertexBuffer[cellBase + v];
                        uv3Base.z = vert.UV3.z;
                        uv3Base.w = vert.UV3.w;
                        vert.UV3 = uv3Base;
                        _vertexBuffer[cellBase + v] = vert;
                    }
                }
            }

            // Step 2: Only fill border cells (those entering the viewport).
            // Border cells are the complement of the scroll region.
            if (dx > 0)
            {
                // Right edge: columns [mw-dx, mw)
                for (int x = mw - dx; x < mw; x++)
                {
                    int vIdx = (x * mh) * 8;
                    int gridX = minX + x;
                    for (int y = 0; y < mh; y++)
                    {
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, true, ref vIdx, atlases);
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, false, ref vIdx, atlases);
                    }
                }
            }
            else if (dx < 0)
            {
                // Left edge: columns [0, -dx)
                for (int x = 0; x < -dx; x++)
                {
                    int vIdx = (x * mh) * 8;
                    int gridX = minX + x;
                    for (int y = 0; y < mh; y++)
                    {
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, true, ref vIdx, atlases);
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, false, ref vIdx, atlases);
                    }
                }
            }

            if (dy > 0)
            {
                // Top edge: rows [mh-dy, mh)
                for (int y = mh - dy; y < mh; y++)
                {
                    for (int x = 0; x < mw; x++)
                    {
                        int vIdx = (x * mh + y) * 8;
                        int gridX = minX + x;
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, true, ref vIdx, atlases);
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, false, ref vIdx, atlases);
                    }
                }
            }
            else if (dy < 0)
            {
                // Bottom edge: rows [0, -dy)
                for (int y = 0; y < -dy; y++)
                {
                    for (int x = 0; x < mw; x++)
                    {
                        int vIdx = (x * mh + y) * 8;
                        int gridX = minX + x;
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, true, ref vIdx, atlases);
                        FillQuadData(x, y, gridX, unityY, worldWidth, worldHeight, false, ref vIdx, atlases);
                    }
                }
            }

            // Single interleaved upload. Vertex count and topology are unchanged,
            // so we use DontRecalculateBounds (bounds are still valid from the
            // previous full rebuild — only a few border cells changed).
            _mesh.SetVertexBufferData(_vertexBuffer, 0, 0, _vertexBuffer.Length, 0, UploadFlags | MeshUpdateFlags.DontRecalculateBounds);

            // SetIndices is NOT called here — the index buffer from the previous full
            // rebuild remains valid because FillQuadData always adds indices for every
            // cell (including above-world transparent cells with alpha=0). No cells
            // enter or leave the GPU's indexable set during incremental scroll.
        }

        /// <summary>
        /// Scrolls _vertexBuffer in-place by (dx, dy) cells, then adjusts positions
        /// for scrolled vertices. Direction-safe iteration prevents overwriting source
        /// data before it is read.
        ///
        /// Position fixup: after scroll, a vertex that was at old local position
        /// (x+dx, y+dy) now sits at local position (x, y), so its Position.xy
        /// must decrease by (dx, dy) in cell-local space.
        /// </summary>
        private void ScrollVertexBuffer(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;

            int mw = _meshWidth;
            int mh = _meshHeight;
            int stride = 8;
            int rowStride = mh * stride;
            Vector3 posOffset = new Vector3(-dx * _cellSize, -dy * _cellSize, 0);

            // Copy data then fix positions in the overlapping (scroll) region.
            // After the copy, data that was at (x+dx, y+dy) now lives at (x, y)
            // but its Position field still encodes the old local offset.
            // Note: TerrainVertex is a struct, so we must read → modify → write back
            // through a local variable; mutation through the array indexer is lost.

            if (dx > 0)
            {
                // Source x+dx > dest x → iterate x ascending to avoid overwrite.
                for (int x = 0; x < mw - dx; x++)
                {
                    int srcBase = (x + dx) * rowStride;
                    int dstBase = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            int src = srcBase + (y + dy) * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            int src = srcBase + (y + dy) * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = 0; y < mh; y++)
                        {
                            int src = srcBase + y * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                }
            }
            else if (dx < 0)
            {
                // Source x+dx < dest x → iterate x descending to avoid overwrite.
                for (int x = mw - 1; x >= -dx; x--)
                {
                    int srcBase = (x + dx) * rowStride;
                    int dstBase = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            int src = srcBase + (y + dy) * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            int src = srcBase + (y + dy) * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = 0; y < mh; y++)
                        {
                            int src = srcBase + y * stride;
                            int dst = dstBase + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                }
            }
            else if (dy != 0)
            {
                // dx == 0, only vertical scroll
                for (int x = 0; x < mw; x++)
                {
                    int baseX = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            int src = baseX + (y + dy) * stride;
                            int dst = baseX + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            int src = baseX + (y + dy) * stride;
                            int dst = baseX + y * stride;
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[src + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dst + v] = vert;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Scrolls _cellCache in-place by (dx, dy) cells and fills newly exposed edges
        /// from WorldLayer. Avoids re-reading the entire viewport on every camera movement.
        ///
        /// Direction-safe iteration: when dx > 0 data moves left (iterate left→right),
        /// when dx < 0 data moves right (iterate right→left), ensuring no source data
        /// is overwritten before being read.
        /// </summary>
        private void ScrollCellCache(int dx, int dy, List<TextureAtlas> atlases)
        {
            var mm = MapManager.Instance;
            var mapStorage = MapStorage.Instance;
            if (mm == null || mapStorage == null || !mapStorage.IsReady) return;

            int worldWidth = mm.WorldWidth;
            int worldHeight = mm.WorldHeight;

            _cacheMinX += dx;
            _cacheMinY += dy;

            // --- Scroll _cellCache in-place ---
            // Direction-safe copy: for dx > 0 we read from higher indices (right),
            // so iterate left→right to avoid overwriting unread sources.
            // For dx < 0 we read from lower indices (left), so iterate right→left.
            if (dx > 0)
            {
                for (int x = 0; x < _cacheWidth - dx; x++)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < _cacheHeight - dy; y++)
                            _cellCache[x, y] = _cellCache[srcX, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = _cacheHeight - 1; y >= -dy; y--)
                            _cellCache[x, y] = _cellCache[srcX, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < _cacheHeight; y++)
                            _cellCache[x, y] = _cellCache[srcX, y];
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = _cacheWidth - 1; x >= -dx; x--)
                {
                    int srcX = x + dx;
                    if (dy > 0)
                    {
                        for (int y = 0; y < _cacheHeight - dy; y++)
                            _cellCache[x, y] = _cellCache[srcX, y + dy];
                    }
                    else if (dy < 0)
                    {
                        for (int y = _cacheHeight - 1; y >= -dy; y--)
                            _cellCache[x, y] = _cellCache[srcX, y + dy];
                    }
                    else
                    {
                        for (int y = 0; y < _cacheHeight; y++)
                            _cellCache[x, y] = _cellCache[srcX, y];
                    }
                }
            }
            else if (dy != 0)
            {
                // dx == 0, only vertical scroll
                int xEnd = _cacheWidth;
                if (dy > 0)
                {
                    for (int x = 0; x < xEnd; x++)
                        for (int y = 0; y < _cacheHeight - dy; y++)
                            _cellCache[x, y] = _cellCache[x, y + dy];
                }
                else
                {
                    for (int x = 0; x < xEnd; x++)
                        for (int y = _cacheHeight - 1; y >= -dy; y--)
                            _cellCache[x, y] = _cellCache[x, y + dy];
                }
            }

            // --- Fill newly exposed edge cells from WorldLayer ---
            // Determine which world-coordinate range is now visible in the cache.
            int cacheMinX = _cacheMinX;
            int cacheMinY = _cacheMinY;
            int cacheMaxX = cacheMinX + _cacheWidth;
            int cacheMaxY = cacheMinY + _cacheHeight;

            // Build a set of (chunkIndex, localIndex)-pairs we need to fill.
            // We process rows/columns that entered the cache due to the scroll.
            var layer = mapStorage.CellLayer;
            if (layer == null) return;

            // Helper: fetch CellType at a world position and update the cache entry + its metadata.
            void FillCell(int cx, int cy)
            {
                int gridX = _cacheMinX + cx;
                int unityY = _cacheMinY + cy;

                CellType type;
                if (gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight)
                {
                    if (gridX < 0 || gridX >= worldWidth || unityY < 0)
                        type = (CellType)0;
                    else
                        type = CellType.Unloaded;
                }
                else
                {
                    int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);
                    if (!layer.GetChunkIndexAndLocal(gridX, serverY, out int chunkIndex, out int localIndex))
                    {
                        type = CellType.Unloaded;
                    }
                    else
                    {
                        var chunk = layer.GetChunk(chunkIndex, false, true);
                        type = chunk != null ? chunk[localIndex] : CellType.Unloaded;
                    }
                }

                var meta = GetMetadata(type, atlases);
                _cellCache[cx, cy] = new CachedCellData
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

            // Fill new rows/columns.
            if (dx > 0)
            {
                // Right edge entered view: fill columns _cacheWidth-dx .. _cacheWidth-1
                for (int x = _cacheWidth - dx; x < _cacheWidth; x++)
                    for (int y = 0; y < _cacheHeight; y++)
                        FillCell(x, y);
            }
            else if (dx < 0)
            {
                // Left edge entered view: fill columns 0 .. -dx-1
                for (int x = 0; x < -dx; x++)
                    for (int y = 0; y < _cacheHeight; y++)
                        FillCell(x, y);
            }

            if (dy > 0)
            {
                // Top edge entered view: fill rows _cacheHeight-dy .. _cacheHeight-1
                for (int y = _cacheHeight - dy; y < _cacheHeight; y++)
                    for (int x = 0; x < _cacheWidth; x++)
                        FillCell(x, y);
            }
            else if (dy < 0)
            {
                // Bottom edge entered view: fill rows 0 .. -dy-1
                for (int y = 0; y < -dy; y++)
                    for (int x = 0; x < _cacheWidth; x++)
                        FillCell(x, y);
            }

            // Request flow-map fallback texture.
            WorldTextureManager.Instance.RequestTexture((CellType)0);
        }

        private void FillQuadData(int x, int y, int gridX, int unityY, int worldWidth, int worldHeight, bool isBackground, ref int vIdx, List<TextureAtlas> atlases)
        {
            if (unityY >= worldHeight)
            {
                // Out-of-bounds cells (above world top): write transparent vertices so the shader
                // discards fragments via color.a < 0.05. Stale data from prior frames (left by the
                // scroll optimization) would otherwise render as mirroring/flood-fill artifacts.
                float posX = x * _cellSize;
                float posY = y * _cellSize;
                _vertexBuffer[vIdx + 0].Position = new Vector3(posX, posY, 0);
                _vertexBuffer[vIdx + 1].Position = new Vector3(posX + _cellSize, posY, 0);
                _vertexBuffer[vIdx + 2].Position = new Vector3(posX + _cellSize, posY + _cellSize, 0);
                _vertexBuffer[vIdx + 3].Position = new Vector3(posX, posY + _cellSize, 0);
                Color clear = Color.clear;
                _vertexBuffer[vIdx + 0].Color = clear;
                _vertexBuffer[vIdx + 1].Color = clear;
                _vertexBuffer[vIdx + 2].Color = clear;
                _vertexBuffer[vIdx + 3].Color = clear;
                Vector4 clearUV3 = new Vector4(x, y, 0, 0);
                _vertexBuffer[vIdx + 0].UV3 = clearUV3;
                _vertexBuffer[vIdx + 1].UV3 = clearUV3;
                _vertexBuffer[vIdx + 2].UV3 = clearUV3;
                _vertexBuffer[vIdx + 3].UV3 = clearUV3;

                // Always add indices so the GPU index buffer stays complete across
                // incremental scrolls. When this cell shifts into the visible world
                // (camera scrolling down past the world edge), the mesh still has
                // valid indices pointing to its vertex slots. Submesh 0 is safe
                // because the fragment shader discards alpha=0 fragments upfront.
                var toSubMesh = _subMeshIndices[0];
                toSubMesh.Add(vIdx + 0);
                toSubMesh.Add(vIdx + 3);
                toSubMesh.Add(vIdx + 2);
                toSubMesh.Add(vIdx + 2);
                toSubMesh.Add(vIdx + 1);
                toSubMesh.Add(vIdx + 0);

                vIdx += 4;
                return;
            }

            int cx = x + 1;
            int cy = y + 1;
            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

            // Cache _cellCache lookup (2D array — can't use ref local, so cache `.Type`)
            CachedCellData ccd = _cellCache[cx, cy];
            CellType cellFgType = ccd.Type;

            CellType cellType = isBackground ? _bgMapBuffer[x, y] : cellFgType;
            bool isSameCell = !isBackground || cellType == cellFgType;
            if (isBackground && (cellType == cellFgType || cellType == 0))
            {
                cellType = CellType.Unloaded;
                isSameCell = false;
            }

            ref CachedCellData data = ref (isSameCell ? ref _cellCache[cx, cy] : ref GetNeighborCacheEntry(cellType, cx, cy, atlases));
            int atlasIndex = data.AtlasIndex;

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize;
            float ly = y * _cellSize;

            // Cache gridVertexOffsets for this cell's 4 corners (avoids repeated 2D array indexer bounds checks)
            Vector3 off00 = _gridVertexOffsets[x, y];
            Vector3 off10 = _gridVertexOffsets[x + 1, y];
            Vector3 off01 = _gridVertexOffsets[x, y + 1];
            Vector3 off11 = _gridVertexOffsets[x + 1, y + 1];

            float cellSize = _cellSize;
            _vertexBuffer[vIdx + 0].Position = new Vector3(lx, ly, zOffset) + off00;
            _vertexBuffer[vIdx + 1].Position = new Vector3(lx + cellSize, ly, zOffset) + off10;
            _vertexBuffer[vIdx + 2].Position = new Vector3(lx + cellSize, ly + cellSize, zOffset) + off11;
            _vertexBuffer[vIdx + 3].Position = new Vector3(lx, ly + cellSize, zOffset) + off01;

            // Set base UV0 — may be modified by tiling descriptor below.
            Vector2 uv0 = new Vector2(0, 0);
            Vector2 uv1 = new Vector2(1, 0);
            Vector2 uv2 = new Vector2(1, 1);
            Vector2 uv3 = new Vector2(0, 1);

            int descriptor = isSameCell ? _cellTilingDescriptors[x, y] : 0;
            bool isOffWorld = gridX < 0 || gridX >= worldWidth || unityY < 0 || unityY >= worldHeight;
            float packedW = (data.HasTileGroup ? 1f : 0f);

            if (data.HasTileGroup && descriptor != 0)
            {
                if ((descriptor & 0x40) != 0)
                {
                    (uv0.x, uv1.x) = (uv1.x, uv0.x);
                    (uv3.x, uv2.x) = (uv2.x, uv3.x);
                }

                if ((descriptor & 0x20) != 0)
                {
                    (uv0.y, uv3.y) = (uv3.y, uv0.y);
                    (uv1.y, uv2.y) = (uv2.y, uv1.y);
                }

                if ((descriptor & 0x80) != 0)
                {
                    Vector2 t = uv0;
                    uv0 = uv1;
                    uv1 = uv2;
                    uv2 = uv3;
                    uv3 = t;
                }
            }

            _vertexBuffer[vIdx + 0].UV0 = uv0;
            _vertexBuffer[vIdx + 1].UV0 = uv1;
            _vertexBuffer[vIdx + 2].UV0 = uv2;
            _vertexBuffer[vIdx + 3].UV0 = uv3;

            Vector4 atlasRect = data.AtlasRect;
            bool useFallback = _useColorLod || atlasRect.z < 0.0001f;
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

            bool isRelief = isSameCell && _cellIsRelief[x, y];
            byte reliefMask = isSameCell ? _cellReliefMasks[x, y] : (byte)0;
            float textureType = isRelief ? 1.0f : 0.0f;

            float sv00 = _gridShadowValues[x, y];
            float sv10 = _gridShadowValues[x + 1, y];
            float sv11 = _gridShadowValues[x + 1, y + 1];
            float sv01 = _gridShadowValues[x, y + 1];

            _vertexBuffer[vIdx].Color = color;
            _vertexBuffer[vIdx].UV1 = atlasRect;
            _vertexBuffer[vIdx].UV2 = tileSizeVec;
            _vertexBuffer[vIdx].UV3 = worldPosVec;
            _vertexBuffer[vIdx].UV4 = animDataVec;
            _vertexBuffer[vIdx].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv00, _localUVsBuffer[0].x, _localUVsBuffer[0].y);

            _vertexBuffer[vIdx + 1].Color = color;
            _vertexBuffer[vIdx + 1].UV1 = atlasRect;
            _vertexBuffer[vIdx + 1].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 1].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 1].UV4 = animDataVec;
            _vertexBuffer[vIdx + 1].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv10, _localUVsBuffer[1].x, _localUVsBuffer[1].y);

            _vertexBuffer[vIdx + 2].Color = color;
            _vertexBuffer[vIdx + 2].UV1 = atlasRect;
            _vertexBuffer[vIdx + 2].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 2].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 2].UV4 = animDataVec;
            _vertexBuffer[vIdx + 2].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv11, _localUVsBuffer[2].x, _localUVsBuffer[2].y);

            _vertexBuffer[vIdx + 3].Color = color;
            _vertexBuffer[vIdx + 3].UV1 = atlasRect;
            _vertexBuffer[vIdx + 3].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 3].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 3].UV4 = animDataVec;
            _vertexBuffer[vIdx + 3].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv01, _localUVsBuffer[3].x, _localUVsBuffer[3].y);

            var indices = _subMeshIndices[atlasIndex];
            indices.Add(vIdx + 0);
            indices.Add(vIdx + 3);
            indices.Add(vIdx + 2);
            indices.Add(vIdx + 2);
            indices.Add(vIdx + 1);
            indices.Add(vIdx + 0);
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
            int w = _meshWidth, h = _meshHeight;
            Array.Clear(_bgMapBuffer, 0, _bgMapBuffer.Length);

            // Phase 1+2: Parallel scan — passable cells fill directly,
            // non-passable cells vote from their passable 8-neighbors for most frequent type.
            // Combined into a single parallel pass over columns for cache efficiency.
            var frontier = _fbpwFrontier;
            frontier.Clear();

            Parallel.For(0, w, x =>
            {
                var localFrontier = new List<(int, int)>(32);
                Span<TypeCount> typeCounts = stackalloc TypeCount[8];

                for (int y = 0; y < h; y++)
                {
                    int cx = x + 1, cy = y + 1;
                    var cell = _cellCache[cx, cy];

                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        _bgMapBuffer[x, y] = cell.Type;
                        localFrontier.Add((x, y));
                    }
                    else
                    {
                        int distinctCount = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) { continue; }

                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx < 0 || nx >= w || ny < 0 || ny >= h) { continue; }

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
                            localFrontier.Add((x, y));
                        }
                    }
                }

                if (localFrontier.Count > 0)
                {
                    lock (_fbpwLock) { frontier.AddRange(localFrontier); }
                }
            });

            // Phase 3: FBPW wavefront propagation (parallel — frontier is large enough for Parallel.For)
            FBPWPropagate(frontier, useParallel: true);

            // Phase 4: Safety sweep — remaining Unloaded cells become Empty
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
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

        public void SetSimpleGraphics(bool enabled)
        {
            _targetSimpleGraphics = enabled ? 1f : 0f;
            foreach (var mat in _materials)
                if (mat != null)
                    mat.SetFloat("_SimpleGraphics", _targetSimpleGraphics);
            PlayerPrefs.SetInt("SimpleGraphics", enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Interleaved vertex format matching Unity's preferred attribute order:
        /// Position → Color → TexCoord0 → TexCoord1 → TexCoord2 → TexCoord3 → TexCoord4 → TexCoord5.
        /// Unity reorders supplied attributes to this layout (logs a warning if order doesn't match),
        /// so the struct MUST follow this order for correct GPU buffer alignment.
        /// Total stride: 12 + 16 + 8 + (16 * 5) = 116 bytes.
        /// </summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct TerrainVertex
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public Vector3 Position;

            [System.Runtime.InteropServices.FieldOffset(12)]
            public Color Color;

            [System.Runtime.InteropServices.FieldOffset(28)]
            public Vector2 UV0;

            [System.Runtime.InteropServices.FieldOffset(36)]
            public Vector4 UV1;   // subAtlasRects

            [System.Runtime.InteropServices.FieldOffset(52)]
            public Vector4 UV2;   // tileSizeUVs

            [System.Runtime.InteropServices.FieldOffset(68)]
            public Vector4 UV3;   // worldPositions

            [System.Runtime.InteropServices.FieldOffset(84)]
            public Vector4 UV4;   // animationData

            [System.Runtime.InteropServices.FieldOffset(100)]
            public Vector4 UV5;   // packedReliefShadowLocalUV
        }
    }
}
