using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using UnityEngine.Rendering;

namespace Fodinae.Assets.Scripts.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WorldBackgroundRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        public int _chunkSize = 32;
        public int _renderDistance = 15;
        public float _cellSize = 1.0f;
        public bool _debugMode = true;

        [Header("Background Settings")]
        [SerializeField] private int _sortingOrder = -1000;
        [SerializeField] private string _sortingLayerName = "Default";

        private MeshRenderer _meshRenderer;
        private Material _backgroundMaterial;

        private WorldLayer<CellType> _worldLayer;

        private readonly ConcurrentDictionary<Vector2Int, ChunkObject> _chunkObjects = new();
        private readonly HashSet<Vector2Int> _generatingChunks = new();
        private readonly HashSet<Vector2Int> _visibleChunks = new();

        private readonly HashSet<Vector2Int> _newVisibleBuffer = new();
        private readonly List<Vector2> _cachedUVs = new();
        private readonly List<Vector2Int> _chunksToRemoveBuffer = new();

        private Camera _mainCamera;
        private Vector2Int _lastCameraChunk = new Vector2Int(int.MinValue, int.MinValue);

        private bool _isInitialized = false;
        private bool _worldInitialized = false;
        private bool _texturesLoaded = false;
        private bool _atlasTextureApplied = false;  // ← Добавлено поле

        private enum InitializationState { Uninitialized, WaitingForWorldInit, WaitingForWorldData, ReadyForRendering, Rendering, Failed }
        private InitializationState _currentState = InitializationState.Uninitialized;

        private bool _fallbackInitializationAttempted = false;
        private float _lastInitializationCheck = 0f;
        private const float _initializationCheckInterval = 2.0f;

        void Awake() => Initialize();

        private void Initialize()
        {
            if (_isInitialized) return;

            _meshRenderer = GetComponent<MeshRenderer>();

            _mainCamera = Camera.main;

            ConfigureBackgroundRendering();

            WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;

            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
            }

            _isInitialized = true;
            _currentState = InitializationState.WaitingForWorldInit;
        }

        private void ConfigureBackgroundRendering()
        {
            Debug.Log("Loading shader...");

            Shader shader = Resources.Load<Shader>("Shaders/WorldObjectWithBackground");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            _backgroundMaterial = new Material(shader);

            _backgroundMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _backgroundMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _backgroundMaterial.SetInt("_ZWrite", 0);
            _backgroundMaterial.renderQueue = (int)RenderQueue.Transparent;

            Debug.Log("Material configured for transparency");

            ApplyAtlas();
        }

        private void Update()
        {
            if (!_isInitialized || _mainCamera == null) return;

            if (!_worldInitialized) InitializeWorldLayer();

            if (_currentState == InitializationState.WaitingForWorldInit ||
                _currentState == InitializationState.WaitingForWorldData)
            {
                CheckFallbackInitialization();
            }

            if (_worldLayer != null && _currentState == InitializationState.ReadyForRendering)
            {
                UpdateVisibleChunks();
                UpdateAnimations();
            }
        }

        private void UpdateAnimations()
        {
            foreach (var chunkObj in _chunkObjects.Values)
            {
                if (chunkObj.GameObject == null || chunkObj.AnimatedCells == null || chunkObj.AnimatedCells.Count == 0)
                    continue;

                var meshFilter = chunkObj.GameObject.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;

                var mesh = meshFilter.sharedMesh;

                _cachedUVs.Clear();
                mesh.GetUVs(0, _cachedUVs);

                if (_cachedUVs.Count == 0) continue;

                bool changed = false;

                foreach (var cell in chunkObj.AnimatedCells)
                {
                    var coord = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cell.CellType, cell.ServerPosition.x, cell.ServerPosition.y);
                    if (coord != AtlasCoordinate.Empty && cell.VertexStartIndex + 3 < _cachedUVs.Count)
                    {
                        _cachedUVs[cell.VertexStartIndex] = new Vector2(coord.U1, coord.V1);
                        _cachedUVs[cell.VertexStartIndex + 1] = new Vector2(coord.U2, coord.V1);
                        _cachedUVs[cell.VertexStartIndex + 2] = new Vector2(coord.U2, coord.V2);
                        _cachedUVs[cell.VertexStartIndex + 3] = new Vector2(coord.U1, coord.V2);
                        changed = true;
                    }
                }

                if (changed)
                {
                    mesh.SetUVs(0, _cachedUVs);
                }
            }
        }

        public void ForceInitialization()
        {
            _worldInitialized = false;
            _worldLayer = null;

            foreach (var chunk in _chunkObjects.Values)
            {
                if (chunk.GameObject != null) Destroy(chunk.GameObject);
            }
            _chunkObjects.Clear();
            _generatingChunks.Clear();
            _visibleChunks.Clear();

            InitializeWorldLayer();

            if (_worldLayer != null)
            {
                _currentState = InitializationState.ReadyForRendering;
                _worldInitialized = true;
                _lastCameraChunk = new Vector2Int(int.MinValue, int.MinValue);
            }
        }

        public void ForceReinitialize() { Initialize(); ForceInitialization(); }

        private void InitializeWorldLayer()
        {
            if (MapStorage.Instance?.cellLayer != null)
            {
                _worldLayer = MapStorage.Instance.cellLayer;
                _worldInitialized = true;
                _currentState = InitializationState.ReadyForRendering;
                _lastCameraChunk = new Vector2Int(int.MinValue, int.MinValue);
            }
        }

        private void UpdateVisibleChunks()
        {
            if (_worldLayer == null || _mainCamera == null) return;

            var cameraPos = _mainCamera.transform.position;
            int cx = Mathf.FloorToInt(cameraPos.x / (_chunkSize * _cellSize));
            int cy = Mathf.FloorToInt(cameraPos.y / (_chunkSize * _cellSize));
            var currentChunk = new Vector2Int(cx, cy);

            if (currentChunk == _lastCameraChunk) return;
            _lastCameraChunk = currentChunk;

            _newVisibleBuffer.Clear();

            float camHeight = _mainCamera.orthographicSize * 2f;
            float camWidth = camHeight * _mainCamera.aspect;

            float minCamX = cameraPos.x - camWidth / 2f;
            float maxCamX = cameraPos.x + camWidth / 2f;
            float minCamY = cameraPos.y - camHeight / 2f;
            float maxCamY = cameraPos.y + camHeight / 2f;

            int minX = Mathf.FloorToInt(minCamX / (_chunkSize * _cellSize)) - 1;
            int maxX = Mathf.FloorToInt(maxCamX / (_chunkSize * _cellSize)) + 1;
            int minY = Mathf.FloorToInt(minCamY / (_chunkSize * _cellSize)) - 1;
            int maxY = Mathf.FloorToInt(maxCamY / (_chunkSize * _cellSize)) + 1;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    _newVisibleBuffer.Add(new Vector2Int(x, y));
                }
            }

            _chunksToRemoveBuffer.Clear();
            foreach (var chunkPos in _visibleChunks)
            {
                if (!_newVisibleBuffer.Contains(chunkPos))
                {
                    _chunksToRemoveBuffer.Add(chunkPos);
                }
            }

            foreach (var chunkPos in _chunksToRemoveBuffer)
            {
                if (_chunkObjects.TryRemove(chunkPos, out var chunkObj))
                {
                    if (chunkObj.GameObject != null) Destroy(chunkObj.GameObject);
                }
                _visibleChunks.Remove(chunkPos);
            }

            foreach (var chunkPos in _newVisibleBuffer)
            {
                if (!_visibleChunks.Contains(chunkPos))
                {
                    _visibleChunks.Add(chunkPos);
                    if (!_chunkObjects.ContainsKey(chunkPos) && !_generatingChunks.Contains(chunkPos))
                    {
                        _generatingChunks.Add(chunkPos);
                        GenerateChunkObjectAsync(chunkPos).Forget();
                    }
                }
            }
        }

        private async UniTask GenerateChunkObjectAsync(Vector2Int chunkPos)
        {
            try
            {
                var chunkMesh = new ChunkMesh(chunkPos);
                await GenerateGeometry(chunkMesh);

                if (chunkMesh.Vertices.Count == 0)
                {
                    _chunkObjects[chunkPos] = new ChunkObject { Position = chunkPos };
                    return;
                }

                await GenerateTexturesOptimized(chunkMesh);

                var go = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}");
                go.transform.SetParent(this.transform, false);
                go.transform.localPosition = new Vector3(chunkPos.x * _chunkSize * _cellSize, chunkPos.y * _chunkSize * _cellSize, 0);

                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();

                renderer.sharedMaterial = _backgroundMaterial;
                renderer.sortingLayerName = _sortingLayerName;
                renderer.sortingOrder = _sortingOrder;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var mesh = new Mesh();
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(chunkMesh.Vertices);
                mesh.SetTriangles(chunkMesh.Triangles, 0);
                mesh.SetUVs(0, chunkMesh.UVs);
                mesh.RecalculateBounds();

                filter.mesh = mesh;

                _chunkObjects[chunkPos] = new ChunkObject
                {
                    Position = chunkPos,
                    GameObject = go,
                    AnimatedCells = chunkMesh.AnimatedCells
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"WorldBackgroundRenderer: Error generating chunk {chunkPos}: {ex.Message}");
            }
            finally
            {
                _generatingChunks.Remove(chunkPos);
            }
        }

        private UniTask GenerateGeometry(ChunkMesh mesh)
        {
            int vertexIndex = 0;

            for (int y = 0; y < _chunkSize; y++)
            {
                for (int x = 0; x < _chunkSize; x++)
                {
                    int wx = mesh.ChunkPosition.x * _chunkSize + x;
                    int wy = mesh.ChunkPosition.y * _chunkSize + y;

                    CellType cell = CellType.Unloaded;
                    try { cell = MapStorage.Instance.GetCell(wx, MapManager.Instance.WorldHeight - 1 - wy); } catch { }

                    if (cell == CellType.Unloaded || cell == CellType.Pregener)
                    {
                        continue;
                    }

                    float gx = x * _cellSize;
                    float gy = y * _cellSize;

                    mesh.Vertices.Add(new Vector3(gx, gy, 0));
                    mesh.Vertices.Add(new Vector3(gx + _cellSize, gy, 0));
                    mesh.Vertices.Add(new Vector3(gx + _cellSize, gy + _cellSize, 0));
                    mesh.Vertices.Add(new Vector3(gx, gy + _cellSize, 0));

                    mesh.UVs.Add(Vector2.zero);
                    mesh.UVs.Add(Vector2.zero);
                    mesh.UVs.Add(Vector2.zero);
                    mesh.UVs.Add(Vector2.zero);

                    mesh.Triangles.Add(vertexIndex);
                    mesh.Triangles.Add(vertexIndex + 2);
                    mesh.Triangles.Add(vertexIndex + 1);
                    mesh.Triangles.Add(vertexIndex);
                    mesh.Triangles.Add(vertexIndex + 3);
                    mesh.Triangles.Add(vertexIndex + 2);

                    mesh.Cells.Add(new CellRenderData
                    {
                        CellType = cell,
                        VertexStartIndex = vertexIndex,
                        WorldPosition = new Vector2Int(wx, wy),
                        ServerPosition = new Vector2Int(wx, MapManager.Instance.WorldHeight - 1 - wy)
                    });
                    vertexIndex += 4;
                }
            }

            return UniTask.CompletedTask;
        }

        private async UniTask GenerateTexturesOptimized(ChunkMesh mesh)
        {
            if (mesh.Cells.Count == 0) return;

            var coords = new AtlasCoordinate[mesh.Cells.Count];

            for (int i = 0; i < mesh.Cells.Count; i++)
            {
                var c = mesh.Cells[i];
                coords[i] = await WorldTextureManager.Instance.GetCellTextureCoordinate(c.CellType, c.ServerPosition.x, c.ServerPosition.y);
            }

            for (int i = 0; i < mesh.Cells.Count; i++)
            {
                var c = mesh.Cells[i];
                var uv = coords[i];

                if (WorldTextureManager.Instance.HasAnimations(c.CellType))
                {
                    mesh.AnimatedCells.Add(c);
                }

                if (uv == AtlasCoordinate.Empty) continue;

                mesh.UVs[c.VertexStartIndex] = new Vector2(uv.U1, uv.V1);
                mesh.UVs[c.VertexStartIndex + 1] = new Vector2(uv.U2, uv.V1);
                mesh.UVs[c.VertexStartIndex + 2] = new Vector2(uv.U2, uv.V2);
                mesh.UVs[c.VertexStartIndex + 3] = new Vector2(uv.U1, uv.V2);
            }
        }

        private void OnTextureLoaded(string name, Texture2D tex)
        {
            _texturesLoaded = true;
            ApplyAtlas();
        }

        private async void ApplyAtlas()
        {
            if (WorldTextureManager.Instance == null) return;
            var atlases = WorldTextureManager.Instance.GetAllAtlases();

            if (atlases.Count > 0)
            {
                var tex = await atlases[0].GetAtlasTexture();
                if (tex != null && _backgroundMaterial != null)
                {
                    _backgroundMaterial.mainTexture = tex;
                    _atlasTextureApplied = true;  // ← Устанавливаем в true
                    Debug.Log($"Atlas applied. Texture format: {tex.format}");
                }
            }
        }

        public bool IsProperlyConfigured() => _isInitialized;
        public int GetVisibleChunkCount() => _chunkObjects.Count;
        public bool AreTexturesLoaded() => _texturesLoaded;
        public bool IsAtlasApplied() => _atlasTextureApplied;  // ← Добавлен метод
        public string GetRendererState() => _currentState.ToString();

        private void OnWorldInitialized() => _currentState = InitializationState.WaitingForWorldData;
        private void OnWorldDataLoaded() { ForceInitialization(); }

        private void CheckFallbackInitialization()
        {
            if (_fallbackInitializationAttempted) return;

            if (Time.time - _lastInitializationCheck < _initializationCheckInterval) return;
            _lastInitializationCheck = Time.time;

            if (MapStorage.Instance != null && MapStorage.Instance.IsReady && _worldLayer == null)
            {
                InitializeWorldLayer();
                if (_worldLayer != null) return;
            }

            if (_currentState == InitializationState.WaitingForWorldInit)
            {
                var standaloneInit = FindObjectOfType<StandaloneWorldInitializer>();
                if (standaloneInit != null && standaloneInit.IsReady())
                {
                    ForceInitialization();
                    if (_worldLayer != null) return;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var chunk in _chunkObjects.Values)
            {
                if (chunk.GameObject != null) Destroy(chunk.GameObject);
            }
            _chunkObjects.Clear();
        }

        internal void OnDrawGizmosSelected()
        {
            if (!_debugMode || _worldLayer == null) return;

            Gizmos.color = Color.yellow;
            foreach (var chunkPos in _visibleChunks)
            {
                var center = new Vector3(
                    chunkPos.x * _chunkSize * _cellSize,
                    chunkPos.y * _chunkSize * _cellSize,
                    0
                );
                var size = new Vector3(_chunkSize * _cellSize, _chunkSize * _cellSize, 0.1f);
                Gizmos.DrawWireCube(center, size);
            }
        }

        public class CellRenderData
        {
            public Vector2Int LocalPosition, WorldPosition, ServerPosition;
            public CellType CellType;
            public int VertexStartIndex;
        }

        private class ChunkMesh
        {
            public Vector2Int ChunkPosition;
            public List<Vector3> Vertices = new();
            public List<int> Triangles = new();
            public List<Vector2> UVs = new();
            public List<CellRenderData> Cells = new();
            public List<CellRenderData> AnimatedCells = new();
            public ChunkMesh(Vector2Int p) { ChunkPosition = p; }
        }

        private class ChunkObject
        {
            public Vector2Int Position;
            public GameObject GameObject;
            public List<CellRenderData> AnimatedCells;
        }
    }
}