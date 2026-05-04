using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Cysharp.Threading.Tasks;

namespace Fodinae.Assets.Scripts.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
        [SerializeField] private int _bufferCells = 2;
        [SerializeField] private Shader _terrainShader;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;

        private List<Vector3> _vertices = new();
        private List<Vector2> _uvs = new();
        private List<Vector4> _subAtlasRects = new();
        private List<Vector4> _tileSizeUVs = new();
        private List<Vector4> _worldPositions = new();

        private Vector2Int _lastMinVisible = new Vector2Int(-1, -1);
        private Vector2Int _lastMaxVisible = new Vector2Int(-1, -1);
        private float _lastRebuildTime = 0;
        private bool _needsRebuild = false;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh();
            _mesh.name = "TerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            InitializeShader();

            _meshRenderer.enabled = true;
            _meshRenderer.sortingOrder = -1000;

            if (WorldTextureManager.Instance != null)
            {
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
            }
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            _needsRebuild = true;
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
            }

            if (_terrainShader == null)
            {
                Debug.LogError("SingleMeshTerrainRenderer: Terrain shader NOT FOUND!");
            }
        }

        private void LateUpdate()
        {
            if (Camera.main == null || MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
                return;

            UpdateVisibleMesh();
        }

        private void UpdateVisibleMesh()
        {
            Camera cam = Camera.main;
            float height = cam.orthographicSize * 2;
            float width = height * cam.aspect;
            Vector3 camPos = cam.transform.position;

            int minX = Mathf.FloorToInt((camPos.x - width / 2) / _cellSize) - _bufferCells;
            int maxX = Mathf.FloorToInt((camPos.x + width / 2) / _cellSize) + _bufferCells;
            int minY = Mathf.FloorToInt((camPos.y - height / 2) / _cellSize) - _bufferCells;
            int maxY = Mathf.FloorToInt((camPos.y + height / 2) / _cellSize) + _bufferCells;

            minX = Mathf.Max(0, minX);
            maxX = Mathf.Min(MapManager.Instance.WorldWidth - 1, maxX);
            minY = Mathf.Max(0, minY);
            maxY = Mathf.Min(MapManager.Instance.WorldHeight - 1, maxY);

            bool rangeChanged = minX != _lastMinVisible.x || minY != _lastMinVisible.y || maxX != _lastMaxVisible.x || maxY != _lastMaxVisible.y;
            bool timeToUpdateAnimations = Time.time - _lastRebuildTime > 0.05f;

            if (rangeChanged || timeToUpdateAnimations || _needsRebuild)
            {
                _lastMinVisible = new Vector2Int(minX, minY);
                _lastMaxVisible = new Vector2Int(maxX, maxY);
                _lastRebuildTime = Time.time;
                _needsRebuild = false;
                BuildMesh(minX, minY, maxX, maxY);
            }
        }

        private void BuildMesh(int minX, int minY, int maxX, int maxY)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            foreach (var atlas in atlases)
            {
                if (atlas.IsDirty)
                {
                    atlas.UpdateAtlasTexture().Forget();
                }
            }

            _vertices.Clear();
            _uvs.Clear();
            _subAtlasRects.Clear();
            _tileSizeUVs.Clear();
            _worldPositions.Clear();

            if (_subMeshIndices.Length != atlases.Count)
            {
                CleanupMaterials();
                _subMeshIndices = new List<int>[atlases.Count];
                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++)
                {
                    _subMeshIndices[i] = new List<int>();
                    _materials[i] = new Material(_terrainShader);
                }
            }

            foreach (var list in _subMeshIndices) list.Clear();

            int vertexCount = 0;
            HashSet<CellType> pendingLoads = new HashSet<CellType>();

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int serverY = MapManager.Instance.WorldHeight - 1 - y;
                    CellType cellType = MapStorage.Instance.GetCell(x, serverY);

                    if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
                        continue;

                    AtlasCoordinate coord = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, x, serverY);
                    if (coord == AtlasCoordinate.Empty)
                    {
                        if (!pendingLoads.Contains(cellType))
                        {
                            pendingLoads.Add(cellType);
                            WorldTextureManager.Instance.GetCellTextureCoordinate(cellType, x, serverY).Forget();
                        }
                        continue;
                    }

                    int atlasIndex = -1;
                    for (int i = 0; i < atlases.Count; i++)
                    {
                        if (atlases[i].ContainsCell(cellType))
                        {
                            atlasIndex = i;
                            break;
                        }
                    }

                    if (atlasIndex == -1) continue;

                    var atlasTex = atlases[atlasIndex].Texture;
                    if (atlasTex == null) continue;

                    if (_materials[atlasIndex].GetTexture("_BaseMap") != atlasTex)
                    {
                        _materials[atlasIndex].SetTexture("_BaseMap", atlasTex);
                    }

                    _vertices.Add(new Vector3(x * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, 0));
                    _vertices.Add(new Vector3(x * _cellSize, (y + 1) * _cellSize, 0));

                    _uvs.Add(new Vector2(0, 0));
                    _uvs.Add(new Vector2(1, 0));
                    _uvs.Add(new Vector2(1, 1));
                    _uvs.Add(new Vector2(0, 1));

                    Vector4 frameRect = GetAnimationFrameRect(cellType, atlasIndex);
                    float tileSize = 32f;
                    float atlasSize = atlases[atlasIndex].Size;
                    float uvTileSize = tileSize / atlasSize;

                    Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, 0, 0);
                    Vector4 worldPosVec = new Vector4(x, serverY, 0, 0);

                    for (int i = 0; i < 4; i++)
                    {
                        _subAtlasRects.Add(frameRect);
                        _tileSizeUVs.Add(tileSizeVec);
                        _worldPositions.Add(worldPosVec);
                    }

                    _subMeshIndices[atlasIndex].Add(vertexCount + 0);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 3);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 2);

                    _subMeshIndices[atlasIndex].Add(vertexCount + 2);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 1);
                    _subMeshIndices[atlasIndex].Add(vertexCount + 0);

                    vertexCount += 4;
                }
            }

            if (vertexCount == 0)
            {
                _mesh.Clear();
                return;
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _subAtlasRects);
            _mesh.SetUVs(2, _tileSizeUVs);
            _mesh.SetUVs(3, _worldPositions);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i);
            }

            _mesh.RecalculateBounds();
            _meshRenderer.sharedMaterials = _materials;

            if (vertexCount > 0)
            {
                Debug.Log($"SingleMeshTerrainRenderer: Built mesh with {vertexCount} vertices and {atlases.Count} sub-meshes.");
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
                        if (Application.isPlaying) Destroy(mat);
                        else DestroyImmediate(mat);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (WorldTextureManager.Instance != null)
            {
                WorldTextureManager.Instance.OnTextureLoaded -= OnTextureLoaded;
            }
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            CleanupMaterials();
        }

        private Vector4 GetAnimationFrameRect(CellType cellType, int atlasIndex)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            var atlas = atlases[atlasIndex];

            AtlasCoordinate baseCoord = atlas.GetCoordinate(cellType);

            int frameIndex = 0;
            int frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);

            if (frameHeight > 0)
            {
                byte speed = MapManager.Instance.GetAnimationSpeed(cellType);
                if (speed == 0) speed = 5;

                int animationFrames = baseCoord.Height / frameHeight;
                if (animationFrames > 0)
                {
                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % animationFrames;
                }
            }

            float atlasSize = atlas.Size;
            float uMin = (float)baseCoord.AtlasX / atlasSize;
            float vMin = (float)(baseCoord.AtlasY + frameIndex * (frameHeight > 0 ? frameHeight : 0)) / atlasSize;
            float uSize = (float)baseCoord.Width / atlasSize;
            float vSize = (float)(frameHeight > 0 ? frameHeight : baseCoord.Height) / atlasSize;

            return new Vector4(uMin, vMin, uSize, vSize);
        }
    }
}
