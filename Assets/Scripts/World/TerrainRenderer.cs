using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Renders terrain using a flat 2D mesh with one quad per cell.
    /// Integrates with the dynamic texture atlas system for efficient rendering.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Reference to the world layer containing cell data")]
        [SerializeField] private WorldLayer<CellType> _worldLayer;
        
        [Tooltip("Chunk size for mesh generation (should match WorldLayer chunk size)")]
        [SerializeField] private int _chunkSize = 32;
        
        [Tooltip("Render distance in chunks from camera")]
        [SerializeField] private int _renderDistance = 10;
        
        [Tooltip("Cell size in world units")]
        [SerializeField] private float _cellSize = 1.0f;
        
        [Tooltip("Enable debug visualization")]
        [SerializeField] private bool _debugMode = false;

        [Header("Performance")]
        [Tooltip("Enable mesh batching for better performance")]
        [SerializeField] private bool _enableBatching = true;
        
        [Tooltip("Maximum chunks to batch together")]
        [SerializeField] private int _maxBatchSize = 16;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;

        public readonly ConcurrentDictionary<Vector2Int, ChunkMesh> _chunkMeshes = new();
        public readonly List<Material> _atlasMaterials = new();
        public readonly HashSet<Vector2Int> _visibleChunks = new();
        
        private Camera _mainCamera;
        private Vector2Int _lastCameraChunk = Vector2Int.zero;
        private bool _isInitialized = false;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            
            if (_meshFilter == null || _meshRenderer == null)
            {
                Debug.LogError("TerrainRenderer requires MeshFilter and MeshRenderer components");
                enabled = false;
                return;
            }

            _mesh = new Mesh();
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Enable 32-bit indices
            _meshFilter.mesh = _mesh;
            
            _mainCamera = Camera.main;
            
            // Initialize with default material
            var defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _meshRenderer.material = defaultMaterial;
            _atlasMaterials.Add(defaultMaterial);

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized || _worldLayer == null || _mainCamera == null) return;

            UpdateVisibleChunks();
            UpdateMesh();
        }

        private async void UpdateVisibleChunks()
        {
            if (_worldLayer == null) return;

            var cameraPos = _mainCamera.transform.position;
            var cameraChunkX = Mathf.FloorToInt(cameraPos.x / (_chunkSize * _cellSize));
            var cameraChunkY = Mathf.FloorToInt(cameraPos.y / (_chunkSize * _cellSize));
            var cameraChunk = new Vector2Int(cameraChunkX, cameraChunkY);

            if (cameraChunk == _lastCameraChunk) return;
            _lastCameraChunk = cameraChunk;

            var newVisibleChunks = new HashSet<Vector2Int>();

            // Calculate visible chunk range
            int minX = cameraChunk.x - _renderDistance;
            int maxX = cameraChunk.x + _renderDistance;
            int minY = cameraChunk.y - _renderDistance;
            int maxY = cameraChunk.y + _renderDistance;

            // Generate visible chunks
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var chunkPos = new Vector2Int(x, y);
                    newVisibleChunks.Add(chunkPos);
                }
            }

            // Remove chunks that are no longer visible
            var chunksToRemove = _visibleChunks.Except(newVisibleChunks).ToList();
            foreach (var chunkPos in chunksToRemove)
            {
                _chunkMeshes.TryRemove(chunkPos, out _);
                _visibleChunks.Remove(chunkPos);
            }

            // Add new visible chunks
            var chunksToAdd = newVisibleChunks.Except(_visibleChunks).ToList();
            foreach (var chunkPos in chunksToAdd)
            {
                _visibleChunks.Add(chunkPos);
                await GenerateChunkMeshAsync(chunkPos);
            }
        }

        private async UniTask GenerateChunkMeshAsync(Vector2Int chunkPos)
        {
            if (_chunkMeshes.ContainsKey(chunkPos)) return;

            var chunkMesh = new ChunkMesh(chunkPos, _chunkSize, _cellSize);
            
            // Generate vertices and triangles for the chunk
            await GenerateChunkGeometry(chunkMesh);
            
            // Get texture coordinates for all cells in the chunk
            await GenerateChunkTextures(chunkMesh);

            _chunkMeshes[chunkPos] = chunkMesh;
        }

        private async UniTask GenerateChunkGeometry(ChunkMesh chunkMesh)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            var atlasIndices = new List<int>();

            int vertexIndex = 0;

            for (int y = 0; y < _chunkSize; y++)
            {
                for (int x = 0; x < _chunkSize; x++)
                {
                    var worldX = chunkMesh.ChunkPosition.x * _chunkSize + x;
                    var worldY = chunkMesh.ChunkPosition.y * _chunkSize + y;

                    // Skip unloaded or pregener cells
                    // Invert Y access: Unity worldY to Server Y
                    int serverY = MapManager.Instance.WorldHeight - 1 - worldY;
                    var cellType = _worldLayer[worldX, serverY];
                    if (cellType == CellType.Unloaded || cellType == CellType.Pregener) continue;

                    // Generate quad vertices
                    var bottomLeft = new Vector3(x * _cellSize, y * _cellSize, 0);
                    var bottomRight = new Vector3((x + 1) * _cellSize, y * _cellSize, 0);
                    var topRight = new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, 0);
                    var topLeft = new Vector3(x * _cellSize, (y + 1) * _cellSize, 0);

                    // Add vertices
                    vertices.Add(bottomLeft);
                    vertices.Add(bottomRight);
                    vertices.Add(topRight);
                    vertices.Add(topLeft);

                    // Add UVs (will be updated later with actual texture coordinates)
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);
                    uvs.Add(Vector2.zero);

                    // Add triangles
                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex + 2);

                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 2);
                    triangles.Add(vertexIndex + 3);

                    // Store cell info for texture assignment
                    chunkMesh.Cells.Add(new CellInfo
                    {
                        LocalPosition = new Vector2Int(x, y),
                        WorldPosition = new Vector2Int(worldX, worldY),
                        CellType = cellType,
                        VertexStartIndex = vertexIndex
                    });

                    vertexIndex += 4;
                }
            }

            chunkMesh.Vertices = vertices;
            chunkMesh.Triangles = triangles;
            chunkMesh.UVs = uvs;
            chunkMesh.AtlasIndices = atlasIndices;
        }

        private async UniTask GenerateChunkTextures(ChunkMesh chunkMesh)
        {
            if (chunkMesh.Cells.Count == 0) return;

            // Get texture coordinates for all cells in the chunk
            var textureTasks = chunkMesh.Cells.Select(cell => 
                WorldTextureManager.Instance.GetCellTextureCoordinate(cell.CellType, cell.WorldPosition.x, cell.WorldPosition.y)
            ).ToArray();

            var textureCoordinates = await UniTask.WhenAll(textureTasks);

            // Assign texture coordinates and determine atlas indices
            for (int i = 0; i < chunkMesh.Cells.Count; i++)
            {
                var cell = chunkMesh.Cells[i];
                var coord = textureCoordinates[i];

                if (coord == AtlasCoordinate.Empty) continue;

                // Find or create material for this atlas
                int atlasIndex = GetAtlasIndex(coord);
                
                // Update UV coordinates for this cell's quad
                UpdateCellUVs(chunkMesh, cell.VertexStartIndex, coord);

                // Store atlas index for this cell's vertices
                for (int v = 0; v < 4; v++)
                {
                    while (chunkMesh.AtlasIndices.Count <= cell.VertexStartIndex + v)
                    {
                        chunkMesh.AtlasIndices.Add(0);
                    }
                    chunkMesh.AtlasIndices[cell.VertexStartIndex + v] = atlasIndex;
                }
            }
        }

        private int GetAtlasIndex(AtlasCoordinate coord)
        {
            // For now, use the first atlas
            // In the future, this could map to different atlases
            return 0;
        }

        private void UpdateCellUVs(ChunkMesh chunkMesh, int vertexStartIndex, AtlasCoordinate coord)
        {
            if (vertexStartIndex + 3 >= chunkMesh.UVs.Count) return;

            // Calculate proper UV coordinates for the individual tile within the atlas
            // The AtlasCoordinate gives us the position and size of the tile within the atlas
            float u1 = (float)coord.AtlasX / coord.AtlasWidth;
            float v1 = (float)coord.AtlasY / coord.AtlasHeight;
            float u2 = (float)(coord.AtlasX + coord.Width) / coord.AtlasWidth;
            float v2 = (float)(coord.AtlasY + coord.Height) / coord.AtlasHeight;

            // Update UV coordinates for the quad
            chunkMesh.UVs[vertexStartIndex] = new Vector2(u1, v1);     // Bottom-left
            chunkMesh.UVs[vertexStartIndex + 1] = new Vector2(u2, v1); // Bottom-right
            chunkMesh.UVs[vertexStartIndex + 2] = new Vector2(u2, v2); // Top-right
            chunkMesh.UVs[vertexStartIndex + 3] = new Vector2(u1, v2); // Top-left
        }

        private void UpdateMesh()
        {
            if (_chunkMeshes.Count == 0)
            {
                _mesh.Clear();
                return;
            }

            BatchMeshes();
            
            _ = UpdateRendererMaterial();
        }

        private void BatchMeshes()
        {
            if (_mainCamera == null) return;

            var combinedVertices = new List<Vector3>();
            var combinedTriangles = new List<int>();
            var combinedUVs = new List<Vector2>();
            var combinedAtlasIndices = new List<int>();

            int vertexOffset = 0;
            
            // Camera-relative origin for centering
            Vector3 cameraWorldPos = _mainCamera.transform.position;
            // Snap cameraWorldPos to avoid precision issues if needed, but here we just center
            
            foreach (var chunkMesh in _chunkMeshes.Values)
            {
                // World position of the chunk's bottom-left corner
                var chunkWorldOrigin = new Vector3(
                    chunkMesh.ChunkPosition.x * _chunkSize * _cellSize,
                    chunkMesh.ChunkPosition.y * _chunkSize * _cellSize,
                    0
                );

                // Add vertices relative to the camera (camera is at 0 in this mesh)
                foreach (var vertex in chunkMesh.Vertices)
                {
                    combinedVertices.Add(vertex + chunkWorldOrigin - cameraWorldPos);
                }

                // Add triangles with offset
                foreach (var triangle in chunkMesh.Triangles)
                {
                    combinedTriangles.Add(triangle + vertexOffset);
                }

                // Add UVs
                combinedUVs.AddRange(chunkMesh.UVs);

                // Add atlas indices
                combinedAtlasIndices.AddRange(chunkMesh.AtlasIndices);

                vertexOffset += chunkMesh.Vertices.Count;
            }

            // Apply combined mesh
            _mesh.Clear();
            _mesh.vertices = combinedVertices.ToArray();
            _mesh.triangles = combinedTriangles.ToArray();
            _mesh.uv = combinedUVs.ToArray();
            _mesh.RecalculateNormals();
            
            // Move GameObject to camera position to complete world-to-local transform
            transform.position = cameraWorldPos;
        }

        private async UniTask UpdateRendererMaterial()
        {
            if (_meshRenderer == null || WorldTextureManager.Instance == null) return;

            // Get all atlases from the WorldTextureManager
            var atlases = WorldTextureManager.Instance.GetAllAtlases();

            if (atlases.Any())
            {
                var mainAtlas = atlases.First();
                var atlasTexture = await mainAtlas.GetAtlasTexture();

                if (atlasTexture != null)
                {
                    if (_meshRenderer.material == null || _meshRenderer.material.shader.name != "Universal Render Pipeline/Unlit")
                    {
                        // Use Unlit shader for terrain cells to avoid lighting issues
                        _meshRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    }

                    _meshRenderer.material.SetTexture("_BaseMap", atlasTexture);
                    _meshRenderer.material.mainTextureScale = Vector2.one;
                    _meshRenderer.material.mainTextureOffset = Vector2.zero;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!_debugMode || _worldLayer == null) return;

            Gizmos.color = Color.yellow;
            
            foreach (var chunkPos in _visibleChunks)
            {
                var center = new Vector3(
                    chunkPos.x * _chunkSize * _cellSize + (_chunkSize * _cellSize / 2),
                    chunkPos.y * _chunkSize * _cellSize + (_chunkSize * _cellSize / 2),
                    0
                );
                
                var size = new Vector3(_chunkSize * _cellSize, _chunkSize * _cellSize, 0.1f);
                Gizmos.DrawWireCube(center, size);
            }
        }

        [System.Serializable]
        public class CellInfo
        {
            public Vector2Int LocalPosition;
            public Vector2Int WorldPosition;
            public CellType CellType;
            public int VertexStartIndex;
        }

        [System.Serializable]
        public class ChunkMesh
        {
            public Vector2Int ChunkPosition;
            public List<Vector3> Vertices;
            public List<int> Triangles;
            public List<Vector2> UVs;
            public List<int> AtlasIndices;
            public List<CellInfo> Cells;

            public ChunkMesh(Vector2Int chunkPosition, int chunkSize, float cellSize)
            {
                ChunkPosition = chunkPosition;
                Vertices = new List<Vector3>();
                Triangles = new List<int>();
                UVs = new List<Vector2>();
                AtlasIndices = new List<int>();
                Cells = new List<CellInfo>();
            }
        }
    }
}