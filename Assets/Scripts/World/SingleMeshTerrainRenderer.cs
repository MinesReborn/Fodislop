using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using MinesServer.Data;
using Fodinae.Assets.Scripts.Game.Managers;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.World
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float _cellSize = 1.0f;
        [SerializeField] private int _bufferCells = 2;
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Color _shimmerHighlightColor = Color.white;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -1000;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;

        private List<Vector3> _vertices = new();
        private List<Vector2> _uvs = new();
        private List<Color> _colors = new();
        private List<Vector4> _subAtlasRects = new();
        private List<Vector4> _tileSizeUVs = new();
        private List<Vector4> _worldPositions = new();
        private List<Vector4> _animationData = new();
        private List<Vector2> _shadowReliefData = new(); // UV5: x = textureType, y = shadow/relief value
        private List<Vector2> _localUVs = new(); // UV6: untransformed local UVs [-0.707, 0.707]

        private Vector2Int _lastMinVisible = new Vector2Int(-1, -1);
        private Vector2Int _lastMaxVisible = new Vector2Int(-1, -1);
        private float _lastRebuildTime = 0;
        private bool _needsRebuild = false;

        private Material[] _materials = Array.Empty<Material>();
        private List<int>[] _subMeshIndices = Array.Empty<List<int>>();

        private void OnValidate()
        {
            _needsRebuild = true;
            // Immediate update if possible
            if (!Application.isPlaying && _materials != null)
            {
                foreach (var mat in _materials)
                {
                    if (mat != null) mat.SetColor("_ShimmerColor", _shimmerHighlightColor);
                }
            }
        }

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
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
            gameObject.layer = 0; // Default layer

            if (WorldTextureManager.Instance != null)
            {
                WorldTextureManager.Instance.OnTextureLoaded += OnTextureLoaded;
            }
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            _needsRebuild = true;
        }

        private byte GetNeighborMask(int x, int serverY, int groupId)
        {
            byte mask = 0;
            int width = MapManager.Instance.WorldWidth;
            int height = MapManager.Instance.WorldHeight;

            // Bits: TL(7) T(6) TR(5) R(4) BR(3) B(2) BL(1) L(0)
            // Server offsets (x, serverY):
            // L: (-1, 0)   [Bit 0]
            // BL: (-1, 1)  [Bit 1]
            // B: (0, 1)    [Bit 2]
            // BR: (1, 1)   [Bit 3]
            // R: (1, 0)    [Bit 4]
            // TR: (1, -1)  [Bit 5]
            // T: (0, -1)   [Bit 6]
            // TL: (-1, -1) [Bit 7]

            int[] dx = { -1, -1, 0, 1, 1, 1, 0, -1 };
            int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = (x + dx[i] + width) % width;
                int ny = (serverY + dy[i] + height) % height;

                CellType neighborType = MapStorage.Instance.GetCell(nx, ny);

                if (MapManager.Instance.TryGetTileGroup(neighborType, out int neighborGroupId) && neighborGroupId == groupId)
                {
                    mask |= (byte)(1 << i);
                }
            }

            return mask;
        }

        private bool IsPassable(CellType cellType)
        {
            var config = MapManager.Instance.GetCellConfig(cellType);
            return (config.Properties & CellConfigProperties.Passable) != 0;
        }

        private struct TypeCount
        {
            public CellType type;
            public int count;
        }

        private void ComputeBackgroundMap(int minX, int minY, int maxX, int maxY, out CellType[,] bgMap)
        {
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            bgMap = new CellType[width, height];

            Queue<(int x, int y)> queue = new();

            // Pass 1: Set passable cells as their own background
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int serverY = MapManager.Instance.WorldHeight - 1 - y;
                    CellType cellType = MapStorage.Instance.GetCell(x, serverY);
                    if (IsPassable(cellType))
                    {
                        bgMap[x - minX, y - minY] = cellType;
                        queue.Enqueue((x, y));
                    }
                }
            }

            // Pass 2: For non-passable cells, try most frequent passable neighbor
            List<(int x, int y)> pass2Cells = new();
            Span<TypeCount> typeCounts = stackalloc TypeCount[8];

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (bgMap[x - minX, y - minY] != 0) continue; // 0 is Unloaded/Default

                    int distinctCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;

                            int nServerY = MapManager.Instance.WorldHeight - 1 - ny;
                            CellType nType = MapStorage.Instance.GetCell(nx, nServerY);
                            if (IsPassable(nType))
                            {
                                bool found = false;
                                for (int i = 0; i < distinctCount; i++)
                                {
                                    if (typeCounts[i].type == nType)
                                    {
                                        typeCounts[i].count++;
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found && distinctCount < 8)
                                {
                                    typeCounts[distinctCount++] = new TypeCount { type = nType, count = 1 };
                                }
                            }
                        }
                    }

                    if (distinctCount > 0)
                    {
                        CellType mostFrequent = typeCounts[0].type;
                        int maxC = typeCounts[0].count;
                        for (int i = 1; i < distinctCount; i++)
                        {
                            if (typeCounts[i].count > maxC)
                            {
                                maxC = typeCounts[i].count;
                                mostFrequent = typeCounts[i].type;
                            }
                        }
                        bgMap[x - minX, y - minY] = mostFrequent;
                        pass2Cells.Add((x, y));
                    }
                }
            }
            foreach (var cell in pass2Cells) queue.Enqueue(cell);

            // Pass 3: Flood fill remaining cells
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                CellType currentBg = bgMap[x - minX, y - minY];

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;

                        if (bgMap[nx - minX, ny - minY] == CellType.Unloaded)
                        {
                            bgMap[nx - minX, ny - minY] = currentBg;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }

            // Fallback: If still some Unloaded (e.g. whole screen is non-passable and far from any passable)
            // Pick a default passable type
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (bgMap[x, y] == CellType.Unloaded)
                    {
                        bgMap[x, y] = CellType.Empty; // Default fallback
                    }
                }
            }
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
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
                return;

            UpdateVisibleMesh();
        }

        private void UpdateVisibleMesh()
        {
            Camera cam = Camera.main;
            if (cam == null && !Application.isPlaying)
            {
                // In Editor mode, try to find any camera if Camera.main is null
                cam = GameObject.FindObjectsOfType<Camera>().FirstOrDefault();
            }
            if (cam == null) return;

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

        private bool DropsShadow(CellType cellType)
        {
            var config = MapManager.Instance.GetCellConfig(cellType);
            return (config.Properties & CellConfigProperties.DropsShadow) != 0;
        }

        private bool ReceivesShadow(CellType cellType)
        {
            var config = MapManager.Instance.GetCellConfig(cellType);
            return (config.Properties & CellConfigProperties.ReceivesShadow) != 0;
        }

        private byte GetReliefGroup(CellType cellType)
        {
            return MapManager.Instance.GetCellConfig(cellType).ReliefGroup;
        }

        private float GetShadowValueForVertex(int x, int unityY)
        {
            int h = MapManager.Instance.WorldHeight;
            int w = MapManager.Instance.WorldWidth;

            // 4 cells around vertex (x, unityY)
            // (x-1, y), (x, y), (x-1, y-1), (x, y-1)
            // Map to serverY: serverY = h - 1 - unityY

            CellType tl = GetCellSafe(x - 1, h - 1 - unityY);
            CellType tr = GetCellSafe(x, h - 1 - unityY);
            CellType bl = GetCellSafe(x - 1, h - unityY);
            CellType br = GetCellSafe(x, h - unityY);

            bool hasCaster = DropsShadow(tl) || DropsShadow(tr) || DropsShadow(bl) || DropsShadow(br);
            bool hasReceiver = ReceivesShadow(tl) || ReceivesShadow(tr) || ReceivesShadow(bl) || ReceivesShadow(br);

            return (hasCaster && hasReceiver) ? 0.7f : 0.0f;
        }

        private CellType GetCellSafe(int x, int serverY)
        {
            if (x < 0 || x >= MapManager.Instance.WorldWidth || serverY < 0 || serverY >= MapManager.Instance.WorldHeight)
                return CellType.Unloaded;
            return MapStorage.Instance.GetCell(x, serverY);
        }

        private byte GetReliefMask(int x, int serverY, byte currentRelief, out bool isRelief)
        {
            isRelief = false;
            int width = MapManager.Instance.WorldWidth;
            int height = MapManager.Instance.WorldHeight;

            // Relief neighbors: Top, Left, Bottom, Right
            // Server offsets (x, serverY):
            // T: (0, -1)   [+1]
            // L: (-1, 0)   [+2]
            // B: (0, 1)    [+4]
            // R: (1, 0)    [+8]

            byte mask = 0;

            // Top
            byte tRelief = GetReliefGroup(GetCellSafe(x, (serverY - 1 + height) % height));
            if (tRelief >= currentRelief) { mask += 1; } else { isRelief = true; }

            // Left
            byte lRelief = GetReliefGroup(GetCellSafe((x - 1 + width) % width, serverY));
            if (lRelief >= currentRelief) { mask += 2; } else { isRelief = true; }

            // Bottom
            byte bRelief = GetReliefGroup(GetCellSafe(x, (serverY + 1) % height));
            if (bRelief >= currentRelief) { mask += 4; } else { isRelief = true; }

            // Right
            byte rRelief = GetReliefGroup(GetCellSafe((x + 1) % width, serverY));
            if (rRelief >= currentRelief) { mask += 8; } else { isRelief = true; }

            return mask;
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
            _colors.Clear();
            _subAtlasRects.Clear();
            _tileSizeUVs.Clear();
            _worldPositions.Clear();
            _animationData.Clear();
            _shadowReliefData.Clear();
            _localUVs.Clear();

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

            ComputeBackgroundMap(minX, minY, maxX, maxY, out var bgMap);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int serverY = MapManager.Instance.WorldHeight - 1 - y;
                    CellType cellType = MapStorage.Instance.GetCell(x, serverY);

                    CellType bgType = bgMap[x - minX, y - minY];
                    bool hasBackground = bgType != cellType && bgType != 0;

                    // 1. Render Background if needed
                    if (hasBackground)
                    {
                        vertexCount = AddQuad(x, y, serverY, bgType, 0.1f, vertexCount, pendingLoads, atlases);
                    }

                    // 2. Render Foreground
                    vertexCount = AddQuad(x, y, serverY, cellType, 0.0f, vertexCount, pendingLoads, atlases);
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
            _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _subAtlasRects);
            _mesh.SetUVs(2, _tileSizeUVs);
            _mesh.SetUVs(3, _worldPositions);
            _mesh.SetUVs(4, _animationData);
            _mesh.SetUVs(5, _shadowReliefData);
            _mesh.SetUVs(6, _localUVs);

            _mesh.subMeshCount = atlases.Count;
            for (int i = 0; i < atlases.Count; i++)
            {
                var flowMapCoord = WorldTextureManager.Instance.GetFlowMapCoordinate(atlases[i]);
                Rect r = flowMapCoord.UVRect;
                _materials[i].SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                _materials[i].SetColor("_ShimmerColor", _shimmerHighlightColor);
                _mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i);
            }

            _mesh.RecalculateBounds();
            _mesh.UploadMeshData(false);
            _meshRenderer.sharedMaterials = _materials;

            if (vertexCount > 0)
            {
                Debug.Log($"SingleMeshTerrainRenderer: Built mesh with {vertexCount} vertices and {atlases.Count} sub-meshes. Bounds: {_mesh.bounds}");
            }
        }

        private Vector3 GetVertexOffset(int x, int y)
        {
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
                return Vector3.zero;

            int h = MapManager.Instance.WorldHeight;

            // Surrounding cells in Unity coords:
            // TL: (x-1, y), TR: (x, y)
            // BL: (x-1, y-1), BR: (x, y-1)
            // Map to serverY: serverY = h - 1 - unityY

            CellDistortionType tl = GetDistortion(x - 1, h - 1 - y);
            CellDistortionType tr = GetDistortion(x, h - 1 - y);
            CellDistortionType bl = GetDistortion(x - 1, h - y);
            CellDistortionType br = GetDistortion(x, h - y);

            if (tl == CellDistortionType.Block || tr == CellDistortionType.Block ||
                bl == CellDistortionType.Block || br == CellDistortionType.Block)
            {
                return Vector3.zero;
            }

            int xSign = 0;
            int ySign = 0;

            if (bl == CellDistortionType.Cause) { xSign -= 1; ySign += 1; }
            if (br == CellDistortionType.Cause) { xSign += 1; ySign += 1; }
            if (tl == CellDistortionType.Cause) { xSign -= 1; ySign -= 1; }
            if (tr == CellDistortionType.Cause) { xSign += 1; ySign -= 1; }

            if (xSign == 0 && ySign == 0) return Vector3.zero;

            // Pseudo-random rx, ry using consistent hash
            uint seed = (uint)(x * 374761397 + y * 668265263);
            seed = (seed ^ (seed >> 13)) * 1274126177;
            seed = seed ^ (seed >> 16);

            // random multiple of 2px up to including 8px (32px = 1 unit)
            float rx = ((seed % 4) + 1) * 0.0625f; // 0.0625 = 2/32

            uint seed2 = seed * 2654435761u;
            float ry = ((seed2 % 4) + 1) * 0.0625f;

            float fx = xSign > 0 ? rx : (xSign < 0 ? -rx : 0);
            float fy = ySign > 0 ? ry : (ySign < 0 ? -ry : 0);

            return new Vector3(fx, fy, 0);
        }

        private CellDistortionType GetDistortion(int x, int serverY)
        {
            if (x < 0 || x >= MapManager.Instance.WorldWidth || serverY < 0 || serverY >= MapManager.Instance.WorldHeight)
                return CellDistortionType.Neutral;

            CellType type = MapStorage.Instance.GetCell(x, serverY);
            if (type == CellType.Unloaded || type == CellType.Pregener) return CellDistortionType.Neutral;

            return MapManager.Instance.GetCellConfig(type).Distortion;
        }

        private int AddQuad(int x, int y, int serverY, CellType cellType, float zOffset, int vertexCount, HashSet<CellType> pendingLoads, List<TextureAtlas> atlases)
        {
            // Use server coordinates for consistent texture coordinate lookup
            AtlasCoordinate coord = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, x, serverY);
            if (coord == AtlasCoordinate.Empty)
            {
                if (!pendingLoads.Contains(cellType))
                {
                    pendingLoads.Add(cellType);
                    WorldTextureManager.Instance.GetCellTextureCoordinate(cellType, x, serverY).Forget();
                }
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

            bool useFallback = atlasIndex == -1 || coord == AtlasCoordinate.Empty;
            if (useFallback)
            {
                atlasIndex = 0; // Force to first atlas/submesh
            }

            var atlasTex = atlases[atlasIndex].Texture;
            if (atlasTex == null) return vertexCount;

            if (_materials[atlasIndex].GetTexture("_BaseMap") != atlasTex)
            {
                _materials[atlasIndex].SetTexture("_BaseMap", atlasTex);
            }

            _vertices.Add(new Vector3(x * _cellSize, y * _cellSize, zOffset) + GetVertexOffset(x, y));
            _vertices.Add(new Vector3((x + 1) * _cellSize, y * _cellSize, zOffset) + GetVertexOffset(x + 1, y));
            _vertices.Add(new Vector3((x + 1) * _cellSize, (y + 1) * _cellSize, zOffset) + GetVertexOffset(x + 1, y + 1));
            _vertices.Add(new Vector3(x * _cellSize, (y + 1) * _cellSize, zOffset) + GetVertexOffset(x, y + 1));

            // Vertex shadow values
            float s0 = GetShadowValueForVertex(x, y);
            float s1 = GetShadowValueForVertex(x + 1, y);
            float s2 = GetShadowValueForVertex(x + 1, y + 1);
            float s3 = GetShadowValueForVertex(x, y + 1);

            // Relief calculation
            byte reliefGroup = GetReliefGroup(cellType);
            bool isRelief;
            byte reliefMask = GetReliefMask(x, serverY, reliefGroup, out isRelief);

            float textureType = isRelief ? 1.0f : 0.0f;

            Vector2[] quadUVs = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            int descriptor = 0;
            float isTiling = 0f;
            if (MapManager.Instance.TryGetTileGroup(cellType, out int groupId))
            {
                // Ensure we have the latest neighbor data from MapStorage
                byte mask = GetNeighborMask(x, serverY, groupId);
                descriptor = TileBitmaskConverter.GetDescriptor(mask);
                isTiling = 1f;

                bool rotate90 = (descriptor & 0x80) != 0;
                bool flipX = (descriptor & 0x40) != 0;
                bool flipY = (descriptor & 0x20) != 0;

                // Apply transformations by remapping quad UVs
                if (flipX)
                {
                    (quadUVs[0].x, quadUVs[1].x) = (quadUVs[1].x, quadUVs[0].x);
                    (quadUVs[3].x, quadUVs[2].x) = (quadUVs[2].x, quadUVs[3].x);
                }
                if (flipY)
                {
                    (quadUVs[0].y, quadUVs[3].y) = (quadUVs[3].y, quadUVs[0].y);
                    (quadUVs[1].y, quadUVs[2].y) = (quadUVs[2].y, quadUVs[1].y);
                }
                if (rotate90)
                {
                    // Clockwise 90 degrees rotation
                    Vector2 temp = quadUVs[0];
                    quadUVs[0] = quadUVs[1];
                    quadUVs[1] = quadUVs[2];
                    quadUVs[2] = quadUVs[3];
                    quadUVs[3] = temp;
                }
            }

            _uvs.AddRange(quadUVs);

            Color mapColor = MapManager.Instance.GetCellMinimapColor(cellType);
            for (int i = 0; i < 4; i++)
            {
                _colors.Add(useFallback ? mapColor : _shimmerHighlightColor);
            }

            Vector4 frameRect = useFallback ? Vector4.zero : GetAnimationFrameRect(cellType, atlasIndex);
            float tileSize = RenderingConstants.CELL_SIZE;
            float atlasSize = atlases[atlasIndex].Size;
            float uvTileSize = tileSize / atlasSize;

            var config = MapManager.Instance.GetCellConfig(cellType);
            float animType = useFallback ? 0f : (float)config.Animation;
            float speed = useFallback ? 0f : (float)config.AnimationSpeed;
            float offset = 0f;

            if (!useFallback && config.Animation == CellAnimationType.Blinking)
            {
                uint seed = (uint)(x * 374761397 + serverY * 668265263);
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                offset = (seed % 6283) / 1000f;
            }

            Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, 0, 0);
            Vector4 worldPosVec = new Vector4(x, serverY, descriptor & 0x1F, isTiling);
            Vector4 animDataVec = new Vector4(animType, speed, offset, useFallback ? 1f : 0f);

            // localUVs in range [-0.707, 0.707]
            float r = 0.70710678f;
            Vector2[] lUVs = new Vector2[]
            {
                new Vector2(-r, -r),
                new Vector2(r, -r),
                new Vector2(r, r),
                new Vector2(-r, r)
            };

            float[] shadows = new float[] { s0, s1, s2, s3 };

            for (int i = 0; i < 4; i++)
            {
                _subAtlasRects.Add(frameRect);
                _tileSizeUVs.Add(tileSizeVec);
                _worldPositions.Add(worldPosVec);
                _animationData.Add(animDataVec);
                _shadowReliefData.Add(new Vector2(textureType, isRelief ? reliefMask : shadows[i]));
                _localUVs.Add(lUVs[i]);
            }

            _subMeshIndices[atlasIndex].Add(vertexCount + 0);
            _subMeshIndices[atlasIndex].Add(vertexCount + 3);
            _subMeshIndices[atlasIndex].Add(vertexCount + 2);

            _subMeshIndices[atlasIndex].Add(vertexCount + 2);
            _subMeshIndices[atlasIndex].Add(vertexCount + 1);
            _subMeshIndices[atlasIndex].Add(vertexCount + 0);

            return vertexCount + 4;
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
