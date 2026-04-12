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
    /// Enhanced terrain renderer with multi-atlas support using sub-meshes
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class EnhancedTerrainRenderer : MonoBehaviour
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
        
        private readonly ConcurrentDictionary<Vector2Int, ChunkMesh> _chunkMeshes = new();
        private readonly HashSet<Vector2Int> _visibleChunks = new();
        
        private Camera _mainCamera;
        private Vector2Int _lastCameraChunk = Vector2Int.zero;
        private bool _isInitialized = false;
        private MultiAtlasMaterialManager _materialManager;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _materialManager = FindObjectOfType<MultiAtlasMaterialManager>();
            
            if (_meshFilter == null || _meshRenderer == null)
            {
                Debug.LogError("EnhancedTerrainRenderer requires MeshFilter and MeshRenderer components");
                enabled = false;
                return;
            }

            _mesh = new Mesh();
            _meshFilter.mesh = _mesh;
            
            _mainCamera = Camera.main;
            
            // Initialize with default material
            if (_materialManager == null)
            {
                var defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _meshRenderer.material = defaultMaterial;
            }
            else
            {
                _meshRenderer.materials = _materialManager.GetAllMaterials();
            }

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized || _worldLayer == null || _mainCamera == null) return;

            UpdateVisibleChunks();
            UpdateMesh();
            
            // Update materials if needed
            if (_materialManager != null)
            {
                _materialManager.UpdateAtlasTextures();
                _meshRenderer.materials = _materialManager.GetAllMaterials();
            }
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

                // Get atlas texture for this coordinate
                var atlasTexture = await GetAtlasTextureForCoordinate(coord);
                
                // Get material index from material manager
                int atlasIndex = 0;
                if (_materialManager != null && atlasTexture != null)
                {
                    atlasIndex = _materialManager.GetAtlasMaterialIndex(atlasTexture);
                }
                
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

        private async UniTask<Texture2D> GetAtlasTextureForCoordinate(AtlasCoordinate coord)
        {
            // For now, return null - this would need to be implemented
            // based on how you determine which atlas contains this coordinate
            return null;
        }

        private void UpdateCellUVs(ChunkMesh chunkMesh, int vertexStartIndex, AtlasCoordinate coord)
        {
            if (vertexStartIndex + 3 >= chunkMesh.UVs.Count) return;

            // Update UV coordinates for the quad
            chunkMesh.UVs[vertexStartIndex] = new Vector2(coord.U1, coord.V1);     // Bottom-left
            chunkMesh.UVs[vertexStartIndex + 1] = new Vector2(coord.U2, coord.V1); // Bottom-right
            chunkMesh.UVs[vertexStartIndex + 2] = new Vector2(coord.U2, coord.V2); // Top-right
            chunkMesh.UVs[vertexStartIndex + 3] = new Vector2(coord.U1, coord.V2); // Top-left
        }

        private void UpdateMesh()
        {
            if (_chunkMeshes.Count == 0)
            {
                _mesh.Clear();
                return;
            }

            if (_enableBatching)
            {
                BatchMeshesWithSubMeshes();
            }
            else
            {
                // Use first chunk as primary mesh for simplicity
                var firstChunk = _chunkMeshes.Values.First();
                ApplyChunkMeshToRenderer(firstChunk);
            }
        }

        private void BatchMeshesWithSubMeshes()
        {
            var combinedVertices = new List<Vector3>();
            var combinedUVs = new List<Vector2>();
            var combinedAtlasIndices = new List<int>();

            // Dictionary to store triangles per atlas index
            var atlasTriangles = new Dictionary<int, List<int>>();
            
            int vertexOffset = 0;
            int chunkCount = 0;

            foreach (var chunkMesh in _chunkMeshes.Values)
            {
                if (chunkCount >= _maxBatchSize) break;

                // Offset vertices to world position
                var chunkOffset = new Vector3(
                    chunkMesh.ChunkPosition.x * _chunkSize * _cellSize,
                    chunkMesh.ChunkPosition.y * _chunkSize * _cellSize,
                    0
                );

                // Add vertices with offset
                foreach (var vertex in chunkMesh.Vertices)
                {
                    combinedVertices.Add(vertex + chunkOffset);
                }

                // Group triangles by atlas index
                for (int i = 0; i < chunkMesh.Triangles.Count; i += 3)
                {
                    int triIndex1 = chunkMesh.Triangles[i];
                    int triIndex2 = chunkMesh.Triangles[i + 1];
                    int triIndex3 = chunkMesh.Triangles[i + 2];

                    // Get atlas index for this triangle (use first vertex)
                    int atlasIndex = chunkMesh.AtlasIndices[triIndex1];

                    if (!atlasTriangles.ContainsKey(atlasIndex))
                    {
                        atlasTriangles[atlasIndex] = new List<int>();
                    }

                    // Add triangle with vertex offset
                    atlasTriangles[atlasIndex].Add(triIndex1 + vertexOffset);
                    atlasTriangles[atlasIndex].Add(triIndex2 + vertexOffset);
                    atlasTriangles[atlasIndex].Add(triIndex3 + vertexOffset);
                }

                // Add UVs
                combinedUVs.AddRange(chunkMesh.UVs);

                // Add atlas indices
                combinedAtlasIndices.AddRange(chunkMesh.AtlasIndices);

                vertexOffset += chunkMesh.Vertices.Count;
                chunkCount++;
            }

            // Apply combined mesh with sub-meshes
            _mesh.Clear();
            _mesh.vertices = combinedVertices.ToArray();
            _mesh.uv = combinedUVs.ToArray();

            // Set sub-meshes for different atlas materials
            int maxAtlasIndex = atlasTriangles.Keys.Count > 0 ? atlasTriangles.Keys.Max() : 0;
            _mesh.subMeshCount = maxAtlasIndex + 1;

            for (int i = 0; i <= maxAtlasIndex; i++)
            {
                if (atlasTriangles.ContainsKey(i))
                {
                    _mesh.SetTriangles(atlasTriangles[i].ToArray(), i);
                }
                else
                {
                    _mesh.SetTriangles(new int[0], i);
                }
            }

            _mesh.RecalculateNormals();
        }

        private void ApplyChunkMeshToRenderer(ChunkMesh chunkMesh)
        {
            _mesh.Clear();
            _mesh.vertices = chunkMesh.Vertices.ToArray();
            _mesh.triangles = chunkMesh.Triangles.ToArray();
            _mesh.uv = chunkMesh.UVs.ToArray();
            _mesh.RecalculateNormals();
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
        private class CellInfo
        {
            public Vector2Int LocalPosition;
            public Vector2Int WorldPosition;
            public CellType CellType;
            public int VertexStartIndex;
        }

        [System.Serializable]
        private class ChunkMesh
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