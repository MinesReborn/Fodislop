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
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
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
        private Vector4[] _tileSizeUVs;
        private Vector4[] _worldPositions;
        private Vector4[] _animationData;
        private Vector2[] _shadowReliefData;
        private Vector2[] _localUVs;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private float _lastOrthoSize;
        private float _lastAspect;
        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
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
            public byte AnimationSpeed;
            public Vector4 AtlasRect;
            public int AtlasIndex;
            public float UVTileSize;
        }

        private CachedCellData[,] _cellCache;
        private int _cacheMinX, _cacheMinY;
        private int _cacheWidth, _cacheHeight;

        private struct CellMetadata {
            public CellConfigProperties Properties;
            public byte ReliefGroup;
            public CellDistortionType Distortion;
            public bool HasTileGroup;
            public int TileGroupId;
            public Color MinimapColor;
            public CellAnimationType Animation;
            public byte AnimationSpeed;
            public Vector4 AtlasRect;
            public int AtlasIndex;
            public float UVTileSize;
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
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = Camera.main;

            _mesh = new Mesh();
            _mesh.name = "TerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            InitializeShader();

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
            gameObject.layer = 0;

            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
        }

        private void OnDestroy()
        {
            if (WorldTextureManager.Instance != null)
                WorldTextureManager.Instance.OnTextureLoaded -= OnTextureLoaded;

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            CleanupMaterials();
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            _metadataCache.Clear();
            _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
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

        private void Update()
        {
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            bool viewportChanged = Mathf.Abs(_mainCamera.orthographicSize - _lastOrthoSize) > 0.01f ||
                                 Mathf.Abs(_mainCamera.aspect - _lastAspect) > 0.01f;

            if (viewportChanged || !_isInitialized)
            {
                _meshWidth = Mathf.CeilToInt((_mainCamera.orthographicSize * 2 * _mainCamera.aspect) / _cellSize) + _viewportPadding * 2;
                _meshHeight = Mathf.CeilToInt((_mainCamera.orthographicSize * 2) / _cellSize) + _viewportPadding * 2;

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

            if (currentGridPos != _lastGridPos)
            {
                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
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
                _animationData = new Vector4[vertCount]; _shadowReliefData = new Vector2[vertCount]; _localUVs = new Vector2[vertCount];
            }

            if (_bgMapBuffer == null || _bgMapBuffer.GetLength(0) != _meshWidth || _bgMapBuffer.GetLength(1) != _meshHeight)
                _bgMapBuffer = new CellType[_meshWidth, _meshHeight];

            _cacheWidth = _meshWidth + 2; _cacheHeight = _meshHeight + 2;
            if (_cellCache == null || _cellCache.GetLength(0) != _cacheWidth || _cellCache.GetLength(1) != _cacheHeight)
                _cellCache = new CachedCellData[_cacheWidth, _cacheHeight];
        }

        private CellMetadata GetMetadata(CellType type, List<TextureAtlas> atlases)
        {
            if (_metadataCache.TryGetValue(type, out var meta)) return meta;
            var config = MapManager.Instance.GetCellConfig(type);
            int atlasIndex = 0;
            for (int i = 0; i < atlases.Count; i++) if (atlases[i].ContainsCell(type)) { atlasIndex = i; break; }
            Vector4 atlasRect = WorldTextureManager.Instance.GetCellFrameRect(type);
            meta = new CellMetadata {
                Properties = config.Properties, ReliefGroup = config.ReliefGroup, Distortion = config.Distortion,
                HasTileGroup = MapManager.Instance.TryGetTileGroup(type, out int gid), TileGroupId = gid,
                MinimapColor = MapManager.Instance.GetCellMinimapColor(type), Animation = config.Animation,
                AnimationSpeed = config.AnimationSpeed, AtlasRect = atlasRect, AtlasIndex = atlasIndex,
                UVTileSize = atlases.Count > atlasIndex ? RenderingConstants.CELL_SIZE / atlases[atlasIndex].Size : 0,
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

            for (int y = 0; y < _cacheHeight; y++) {
                for (int x = 0; x < _cacheWidth; x++) {
                    int gridX = _cacheMinX + x, unityY = _cacheMinY + y;
                    int worldX = (gridX % worldWidth + worldWidth) % worldWidth;
                    int serverY = (worldHeight - 1 - unityY) % worldHeight; if (serverY < 0) serverY += worldHeight;
                    CellType type = MapStorage.Instance.GetCell(worldX, serverY);
                    var meta = GetMetadata(type, atlases);
                    _cellCache[x, y] = new CachedCellData {
                        Type = type, Properties = meta.Properties, ReliefGroup = meta.ReliefGroup, Distortion = meta.Distortion,
                        HasTileGroup = meta.HasTileGroup, TileGroupId = meta.TileGroupId, MinimapColor = meta.MinimapColor,
                        Animation = meta.Animation, AnimationSpeed = meta.AnimationSpeed, AtlasRect = meta.AtlasRect,
                        AtlasIndex = meta.AtlasIndex, UVTileSize = meta.UVTileSize
                    };
                    if (type != CellType.Unloaded && !meta.IsTextureReady) WorldTextureManager.Instance.RequestTexture(type);
                }
            }
        }

        private void UpdateVertexAttributes(int minX, int minY)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            bool materialsChanged = false;
            if (_subMeshIndices.Length != atlases.Count)
            {
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++) {
                    _subMeshIndices[i] = new(); _materials[i] = new Material(_terrainShader);
                }
                materialsChanged = true;
            }

            foreach (var list in _subMeshIndices) list.Clear();

            PopulateCellCache(minX, minY);
            ComputeBackgroundMap();

            int vIdx = 0;
            for (int y = 0; y < _meshHeight; y++) {
                for (int x = 0; x < _meshWidth; x++) {
                    FillQuadData(x + 1, y + 1, minX + x, minY + y, true, ref vIdx, atlases);
                    FillQuadData(x + 1, y + 1, minX + x, minY + y, false, ref vIdx, atlases);
                }
            }

            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uvs); _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _subAtlasRects); _mesh.SetUVs(2, _tileSizeUVs); _mesh.SetUVs(3, _worldPositions);
            _mesh.SetUVs(4, _animationData); _mesh.SetUVs(5, _shadowReliefData); _mesh.SetUVs(6, _localUVs);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                var atlasTex = atlases[i].Texture;
                if (_materials[i].GetTexture("_BaseMap") != atlasTex)
                {
                    var flowMapCoord = WorldTextureManager.Instance.GetFlowMapCoordinate(atlases[i]);
                    Rect r = flowMapCoord.UVRect;
                    _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                    _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                    _materials[i].SetTexture("_BaseMap", atlasTex);
                }
                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i);
            }

            _mesh.UploadMeshData(false);
            if (materialsChanged) _meshRenderer.sharedMaterials = _materials;
        }

        private void FillQuadData(int cx, int cy, int gridX, int unityY, bool isBackground, ref int vIdx, List<TextureAtlas> atlases)
        {
            int serverY = (MapManager.Instance.WorldHeight - 1 - unityY) % MapManager.Instance.WorldHeight;
            if (serverY < 0) serverY += MapManager.Instance.WorldHeight;

            CellType cellType = isBackground ? _bgMapBuffer[cx - 1, cy - 1] : _cellCache[cx, cy].Type;
            if (isBackground && (cellType == _cellCache[cx, cy].Type || cellType == 0)) cellType = CellType.Unloaded;

            CachedCellData data = (cellType == _cellCache[cx, cy].Type) ? _cellCache[cx, cy] : GetNeighborCacheEntry(cellType, cx, cy, atlases);
            int atlasIndex = data.AtlasIndex;

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = (cx - 1) * _cellSize, ly = (cy - 1) * _cellSize;

            _vertices[vIdx+0] = new Vector3(lx, ly, zOffset) + GetVertexOffsetCached(cx, cy);
            _vertices[vIdx+1] = new Vector3(lx + _cellSize, ly, zOffset) + GetVertexOffsetCached(cx + 1, cy);
            _vertices[vIdx+2] = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + GetVertexOffsetCached(cx + 1, cy + 1);
            _vertices[vIdx+3] = new Vector3(lx, ly + _cellSize, zOffset) + GetVertexOffsetCached(cx, cy + 1);

            _uvs[vIdx+0] = new Vector2(0, 0); _uvs[vIdx+1] = new Vector2(1, 0); _uvs[vIdx+2] = new Vector2(1, 1); _uvs[vIdx+3] = new Vector2(0, 1);

            int descriptor = 0; float isTiling = 0f;
            if (data.HasTileGroup) {
                descriptor = TileBitmaskConverter.GetDescriptor(GetNeighborMaskCached(cx, cy, data.TileGroupId));
                isTiling = 1f;
                if ((descriptor & 0x40) != 0) { (_uvs[vIdx+0].x, _uvs[vIdx+1].x) = (_uvs[vIdx+1].x, _uvs[vIdx+0].x); (_uvs[vIdx+3].x, _uvs[vIdx+2].x) = (_uvs[vIdx+2].x, _uvs[vIdx+3].x); }
                if ((descriptor & 0x20) != 0) { (_uvs[vIdx+0].y, _uvs[vIdx+3].y) = (_uvs[vIdx+3].y, _uvs[vIdx+0].y); (_uvs[vIdx+1].y, _uvs[vIdx+2].y) = (_uvs[vIdx+2].y, _uvs[vIdx+1].y); }
                if ((descriptor & 0x80) != 0) { Vector2 t = _uvs[vIdx+0]; _uvs[vIdx+0] = _uvs[vIdx+1]; _uvs[vIdx+1] = _uvs[vIdx+2]; _uvs[vIdx+2] = _uvs[vIdx+3]; _uvs[vIdx+3] = t; }
            }

            bool useFallback = data.AtlasRect.z < 0.0001f;
            Color color = useFallback ? data.MinimapColor : _shimmerHighlightColor;
            float animOffset = 0f;
            if (!useFallback && data.Animation == CellAnimationType.Blinking) {
                uint seed = (uint)(gridX * 374761397 + serverY * 668265263);
                seed = (seed ^ (seed >> 13)) * 1274126177; seed = seed ^ (seed >> 16);
                animOffset = (seed % 6283) / 1000f;
            }

            Vector4 animDataVec = new Vector4((float)data.Animation, (float)data.AnimationSpeed, animOffset, useFallback ? 1f : 0f);
            Vector4 tileSizeVec = new Vector4(data.UVTileSize, data.UVTileSize, 0, 0);
            Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, isTiling);

            float s0 = GetShadowValueCached(cx, cy), s1 = GetShadowValueCached(cx + 1, cy);
            float s2 = GetShadowValueCached(cx + 1, cy + 1), s3 = GetShadowValueCached(cx, cy + 1);

            bool isRelief; byte reliefMask = GetReliefMaskCached(cx, cy, data.ReliefGroup, out isRelief);
            float textureType = isRelief ? 1.0f : 0.0f;

            for (int i = 0; i < 4; i++) {
                _colors[vIdx+i] = color; _subAtlasRects[vIdx+i] = data.AtlasRect;
                _tileSizeUVs[vIdx+i] = tileSizeVec; _worldPositions[vIdx+i] = worldPosVec;
                _animationData[vIdx+i] = animDataVec; _localUVs[vIdx+i] = _localUVsBuffer[i];
                _shadowReliefData[vIdx+i] = new Vector2(textureType, isRelief ? reliefMask : (i==0?s0:i==1?s1:i==2?s2:s3));
            }

            _subMeshIndices[atlasIndex].Add(vIdx + 0); _subMeshIndices[atlasIndex].Add(vIdx + 3); _subMeshIndices[atlasIndex].Add(vIdx + 2);
            _subMeshIndices[atlasIndex].Add(vIdx + 2); _subMeshIndices[atlasIndex].Add(vIdx + 1); _subMeshIndices[atlasIndex].Add(vIdx + 0);
            vIdx += 4;
        }

        private CachedCellData GetNeighborCacheEntry(CellType type, int cx, int cy, List<TextureAtlas> atlases)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (_cellCache[cx + dx, cy + dy].Type == type) return _cellCache[cx + dx, cy + dy];

            var meta = GetMetadata(type, atlases);
            return new CachedCellData {
                Type = type, Properties = meta.Properties, ReliefGroup = meta.ReliefGroup, Distortion = meta.Distortion,
                MinimapColor = meta.MinimapColor, AtlasRect = meta.AtlasRect, AtlasIndex = meta.AtlasIndex, UVTileSize = meta.UVTileSize
            };
        }

        private byte GetNeighborMaskCached(int cx, int cy, int groupId)
        {
            byte mask = 0;
            if (_cellCache[cx - 1, cy].HasTileGroup && _cellCache[cx - 1, cy].TileGroupId == groupId) mask |= 1;
            if (_cellCache[cx - 1, cy + 1].HasTileGroup && _cellCache[cx - 1, cy + 1].TileGroupId == groupId) mask |= 2;
            if (_cellCache[cx, cy + 1].HasTileGroup && _cellCache[cx, cy + 1].TileGroupId == groupId) mask |= 4;
            if (_cellCache[cx + 1, cy + 1].HasTileGroup && _cellCache[cx + 1, cy + 1].TileGroupId == groupId) mask |= 8;
            if (_cellCache[cx + 1, cy].HasTileGroup && _cellCache[cx + 1, cy].TileGroupId == groupId) mask |= 16;
            if (_cellCache[cx + 1, cy - 1].HasTileGroup && _cellCache[cx + 1, cy - 1].TileGroupId == groupId) mask |= 32;
            if (_cellCache[cx, cy - 1].HasTileGroup && _cellCache[cx, cy - 1].TileGroupId == groupId) mask |= 64;
            if (_cellCache[cx - 1, cy - 1].HasTileGroup && _cellCache[cx - 1, cy - 1].TileGroupId == groupId) mask |= 128;
            return mask;
        }

        private float GetShadowValueCached(int cx, int cy)
        {
            bool hasCaster = (_cellCache[cx-1,cy-1].Properties & CellConfigProperties.DropsShadow) != 0 || (_cellCache[cx,cy-1].Properties & CellConfigProperties.DropsShadow) != 0 || (_cellCache[cx-1,cy].Properties & CellConfigProperties.DropsShadow) != 0 || (_cellCache[cx,cy].Properties & CellConfigProperties.DropsShadow) != 0;
            bool hasReceiver = (_cellCache[cx-1,cy-1].Properties & CellConfigProperties.ReceivesShadow) != 0 || (_cellCache[cx,cy-1].Properties & CellConfigProperties.ReceivesShadow) != 0 || (_cellCache[cx-1,cy].Properties & CellConfigProperties.ReceivesShadow) != 0 || (_cellCache[cx,cy].Properties & CellConfigProperties.ReceivesShadow) != 0;
            return (hasCaster && hasReceiver) ? 0.7f : 0.0f;
        }

        private byte GetReliefMaskCached(int cx, int cy, byte currentRelief, out bool isRelief)
        {
            isRelief = false; byte mask = 0;
            if (_cellCache[cx, cy + 1].ReliefGroup >= currentRelief) mask += 1; else isRelief = true;
            if (_cellCache[cx - 1, cy].ReliefGroup >= currentRelief) mask += 2; else isRelief = true;
            if (_cellCache[cx, cy - 1].ReliefGroup >= currentRelief) mask += 4; else isRelief = true;
            if (_cellCache[cx + 1, cy].ReliefGroup >= currentRelief) mask += 8; else isRelief = true;
            return mask;
        }

        private Vector3 GetVertexOffsetCached(int cx, int cy)
        {
            var tl = _cellCache[cx - 1, cy]; var tr = _cellCache[cx, cy];
            var bl = _cellCache[cx - 1, cy - 1]; var br = _cellCache[cx, cy - 1];
            if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block || bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block) return Vector3.zero;
            int xSign = 0, ySign = 0;
            if (bl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
            if (br.Distortion == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
            if (tl.Distortion == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
            if (tr.Distortion == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }
            if (xSign == 0 && ySign == 0) return Vector3.zero;
            uint seed = (uint)((_cacheMinX + cx) * 374761397 + (_cacheMinY + cy) * 668265263);
            seed = (seed ^ (seed >> 13)) * 1274126177; seed = seed ^ (seed >> 16);
            float rx = ((seed % 4) + 1) * 0.0625f;
            uint seed2 = seed * 2654435761u; float ry = ((seed2 % 4) + 1) * 0.0625f;
            return new Vector3(xSign > 0 ? rx : (xSign < 0 ? -rx : 0), ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
        }

        private void ComputeBackgroundMap()
        {
            Array.Clear(_bgMapBuffer, 0, _bgMapBuffer.Length); _floodFillQueue.Clear();
            for (int y = 0; y < _meshHeight; y++) {
                for (int x = 0; x < _meshWidth; x++) {
                    var cell = _cellCache[x + 1, y + 1];
                    if ((cell.Properties & CellConfigProperties.Passable) != 0) {
                        _bgMapBuffer[x, y] = cell.Type; _floodFillQueue.Enqueue((x, y));
                    }
                }
            }
            _pass2Cells.Clear(); Span<TypeCount> typeCounts = stackalloc TypeCount[8];
            for (int y = 0; y < _meshHeight; y++) {
                for (int x = 0; x < _meshWidth; x++) {
                    if (_bgMapBuffer[x, y] != 0) continue;
                    int distinctCount = 0;
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= _meshWidth || ny < 0 || ny >= _meshHeight) continue;
                            var n = _cellCache[nx + 1, ny + 1];
                            if ((n.Properties & CellConfigProperties.Passable) != 0) {
                                bool found = false;
                                for (int i = 0; i < distinctCount; i++) if (typeCounts[i].type == n.Type) { typeCounts[i].count++; found = true; break; }
                                if (!found && distinctCount < 8) typeCounts[distinctCount++] = new TypeCount { type = n.Type, count = 1 };
                            }
                        }
                    }
                    if (distinctCount > 0) {
                        CellType mostFrequent = typeCounts[0].type; int maxC = typeCounts[0].count;
                        for (int i = 1; i < distinctCount; i++) if (typeCounts[i].count > maxC) { maxC = typeCounts[i].count; mostFrequent = typeCounts[i].type; }
                        _bgMapBuffer[x, y] = mostFrequent; _pass2Cells.Add((x, y));
                    }
                }
            }
            foreach (var cell in _pass2Cells) _floodFillQueue.Enqueue(cell);
            while (_floodFillQueue.Count > 0) {
                var (x, y) = _floodFillQueue.Dequeue();
                CellType currentBg = _bgMapBuffer[x, y];
                for (int dy = -1; dy <= 1; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
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
    }
}
