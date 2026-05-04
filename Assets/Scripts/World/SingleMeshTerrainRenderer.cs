using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;

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
        private List<int> _indices = new();
        private List<Vector2> _uvs = new(); // Quad-relative UVs [0,1]
        private List<Vector4> _subAtlasRects = new(); // [Umin, Vmin, WidthUV, HeightUV]
        private List<Vector2> _tileSizeUVs = new(); // [TileWidthUV, TileHeightUV]
        private List<Vector2> _worldPositions = new(); // [ServerX, ServerY]

        private Vector2Int _lastMinVisible = new Vector2Int(-1, -1);
        private Vector2Int _lastMaxVisible = new Vector2Int(-1, -1);
        private float _lastRebuildTime = 0;

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

            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
                if (_terrainShader == null)
                {
                    Debug.LogWarning("Shader 'Universal Render Pipeline/Custom/Terrain' not found by name, attempting to load from Resources...");
                    _terrainShader = Resources.Load<Shader>("Shaders/Terrain");
                }
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

            // Clamp to world bounds
            minX = Mathf.Max(0, minX);
            maxX = Mathf.Min(MapManager.Instance.WorldWidth - 1, maxX);
            minY = Mathf.Max(0, minY);
            maxY = Mathf.Min(MapManager.Instance.WorldHeight - 1, maxY);

            // Optimization: only rebuild if visible range changed or some time has passed (for animations)
            bool rangeChanged = minX != _lastMinVisible.x || minY != _lastMinVisible.y || maxX != _lastMaxVisible.x || maxY != _lastMaxVisible.y;
            bool timeToUpdateAnimations = Time.time - _lastRebuildTime > 0.05f; // ~20fps for animations

            if (rangeChanged || timeToUpdateAnimations)
            {
                _lastMinVisible = new Vector2Int(minX, minY);
                _lastMaxVisible = new Vector2Int(maxX, maxY);
                _lastRebuildTime = Time.time;
                BuildMesh(minX, minY, maxX, maxY);
            }
        }

        private void BuildMesh(int minX, int minY, int maxX, int maxY)
        {
            _vertices.Clear();
            _uvs.Clear();
            _subAtlasRects.Clear();
            _tileSizeUVs.Clear();
            _worldPositions.Clear();

            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            if (_subMeshIndices.Length != atlases.Count)
            {
                _subMeshIndices = new List<int>[atlases.Count];
                for (int i = 0; i < atlases.Count; i++) _subMeshIndices[i] = new List<int>();

                // Cleanup old materials
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

                _materials = new Material[atlases.Count];
                for (int i = 0; i < atlases.Count; i++)
                {
                    _materials[i] = new Material(_terrainShader);
                }
            }

            foreach (var list in _subMeshIndices) list.Clear();

            int vertexCount = 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Convert Unity Y to Server Y
                    int serverY = MapManager.Instance.WorldHeight - 1 - y;
                    CellType cellType = MapStorage.Instance.GetCell(x, serverY);

                    if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
                        continue;

                    // Get current frame coordinate
                    AtlasCoordinate coord = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, x, serverY);
                    if (coord == AtlasCoordinate.Empty) continue;

                    // Find which atlas it belongs to
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

                    // Update material texture if needed
                    // (Assuming atlas texture might have changed/loaded)
                    // We can optimize this by only doing it when atlases are marked dirty
                    var atlasTex = atlases[atlasIndex].Texture;
                    if (_materials[atlasIndex].GetTexture("_BaseMap") != atlasTex)
                    {
                        _materials[atlasIndex].SetTexture("_BaseMap", atlasTex);
                    }

                    // Vertices
                    _vertices.Add(new Vector3(x * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, y * _cellSize, 0));
                    _vertices.Add(new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, 0));
                    _vertices.Add(new Vector3(x * _cellSize, (y + 1) * _cellSize, 0));

                    // Quad UVs
                    _uvs.Add(new Vector2(0, 0));
                    _uvs.Add(new Vector2(1, 0));
                    _uvs.Add(new Vector2(1, 1));
                    _uvs.Add(new Vector2(0, 1));

                    // Sub-atlas rect: [Umin, Vmin, WidthUV, HeightUV]
                    // This rect is for the ENTIRE sub-atlas (including variants and frames)
                    // Wait, the shader expects the current frame's rect to do variant selection within it.
                    // If we have animations, the coord returned by GetCellTextureCoordinateSync is already for the current frame.
                    // We need to pass the current frame's rect.

                    // In TextureAtlas.cs, GetWrappedCoordinate returns a 32x32 tile's coordinate.
                    // But we want the shader to do the variant wrapping.
                    // So we should give it the base rectangle of the sub-atlas (or the current animation frame).

                    // Actually, if we want the shader to do variant selection, we need to know the frame's bounds.
                    // Let's re-examine AtlasCoordinate.
                    // U1, V1, U2, V2 give the rect.

                    // The coordinate returned by Sync is already wrapped for positional variant too!
                    // Wait, the user said: "The shader should be responsible for the positional variant selection since it has the necessary information to do so"
                    // And "i guess the animation frame selection would be the CPU responsibility still"

                    // So I should provide the RECT of the current animation frame.
                    // I need a way to get the animation frame rect WITHOUT positional wrapping.

                    Vector4 frameRect = GetAnimationFrameRect(cellType, atlasIndex);
                    float tileSize = 32f; // Default
                    // Note: We can't easily access WorldTextureManager's private _cellTextureSize,
                    // but we can assume it's 32 or try to get it from Atlas if it was public.
                    // For now keeping it 32 as per standard in this codebase.
                    Vector2 tileUV = new Vector2(tileSize / atlases[atlasIndex].Size, tileSize / atlases[atlasIndex].Size);

                    for (int i = 0; i < 4; i++)
                    {
                        _subAtlasRects.Add(frameRect);
                        _tileSizeUVs.Add(tileUV);
                        _worldPositions.Add(new Vector2(x, serverY));
                    }

                    // Indices for sub-mesh (Clockwise)
                    _subMeshIndices[atlasIndex].Add(vertexCount + 0); // BL
                    _subMeshIndices[atlasIndex].Add(vertexCount + 3); // TL
                    _subMeshIndices[atlasIndex].Add(vertexCount + 2); // TR

                    _subMeshIndices[atlasIndex].Add(vertexCount + 2); // TR
                    _subMeshIndices[atlasIndex].Add(vertexCount + 1); // BR
                    _subMeshIndices[atlasIndex].Add(vertexCount + 0); // BL

                    vertexCount += 4;
                }
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
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }

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

        private Vector4 GetAnimationFrameRect(CellType cellType, int atlasIndex)
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            var atlas = atlases[atlasIndex];

            // Get base coordinate for the cell (the whole sub-atlas)
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

            // Calculate UVs for the current frame
            float uMin = (float)baseCoord.AtlasX / atlas.Size;
            float vMin = (float)(baseCoord.AtlasY + frameIndex * frameHeight) / atlas.Size;
            float uSize = (float)baseCoord.Width / atlas.Size;
            float vSize = (float)(frameHeight > 0 ? frameHeight : baseCoord.Height) / atlas.Size;

            return new Vector4(uMin, vMin, uSize, vSize);
        }
    }
}
