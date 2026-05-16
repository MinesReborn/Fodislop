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
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SingleMeshTerrainRenderer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Color _shimmerHighlightColor = Color.white;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -1000;
        [SerializeField] private int _viewportMargin = 4;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;

        private Texture2D _worldMapTex;
        private Color32[] _worldMapData;
        private Texture2D _cellConfigTex;
        private Texture2DArray _atlasesTexArray;
        private HashSet<CellType> _dirtyCellConfigs = new();
        private bool _atlasesDirty = false;

        private int _currentVpWidth = -1;
        private int _currentVpHeight = -1;
        private float _lastOrthoSize = -1f;
        private float _lastAspect = -1f;
        private Vector2Int _lastSnapPos = new Vector2Int(int.MinValue, int.MinValue);

        private Camera _mainCamera;

        private void OnValidate()
        {
            if (!Application.isPlaying && _material != null)
            {
                _material.SetColor("_ShimmerColor", _shimmerHighlightColor);
            }
        }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh();
            _mesh.name = "ViewportTerrainMesh";
            _mesh.MarkDynamic();
            _mesh.indexFormat = IndexFormat.UInt32;
            _meshFilter.mesh = _mesh;

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
            gameObject.layer = 0;
        }

        private void InitializeShader()
        {
            if (_terrainShader == null)
            {
                _terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain");
            }
            if (_terrainShader != null)
            {
                _material = new Material(_terrainShader);
                _meshRenderer.sharedMaterial = _material;
            }
            else
            {
                Debug.LogError("SingleMeshTerrainRenderer: Terrain shader NOT FOUND!");
            }
        }

        private void Start()
        {
            InitializeShader();

            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
            }

            if (WorldTextureManager.Instance != null)
            {
                WorldTextureManager.Instance.OnCellTextureLoaded += OnCellTextureLoaded;
            }

            _mainCamera = Camera.main;
            if (_mainCamera == null) _mainCamera = FindObjectsOfType<Camera>().FirstOrDefault();
        }

        private void OnWorldDataLoaded()
        {
            UpdateCellConfigTexture();
            UpdateWorldMapTexture();
            UpdateAtlasesTextureArray();

            if (_material != null)
            {
                _material.SetColor("_ShimmerColor", _shimmerHighlightColor);
                var atlases = WorldTextureManager.Instance.GetAllAtlases();
                if (atlases.Count > 0)
                {
                    var flowMapCoord = WorldTextureManager.Instance.GetFlowMapCoordinate(atlases[0]);
                    Rect r = flowMapCoord.UVRect;
                    _material.SetVector("_FlowMapRect", new Vector4(r.x, r.y, r.width, r.height));
                }
            }
        }

        private void OnCellTextureLoaded(CellType cellType)
        {
            _dirtyCellConfigs.Add(cellType);
            _atlasesDirty = true;
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            float ortho = _mainCamera.orthographicSize;
            float aspect = _mainCamera.aspect;

            if (!Mathf.Approximately(ortho, _lastOrthoSize) || !Mathf.Approximately(aspect, _lastAspect))
            {
                _lastOrthoSize = ortho;
                _lastAspect = aspect;

                int vpHeight = Mathf.CeilToInt(ortho * 2) + _viewportMargin * 2;
                int vpWidth = Mathf.CeilToInt(ortho * 2 * aspect) + _viewportMargin * 2;

                if (vpWidth != _currentVpWidth || vpHeight != _currentVpHeight)
                {
                    _currentVpWidth = vpWidth;
                    _currentVpHeight = vpHeight;
                    BuildViewportMesh(vpWidth, vpHeight);
                }
            }

            Vector3 camPos = _mainCamera.transform.position;
            Vector2Int snapPos = new Vector2Int(Mathf.FloorToInt(camPos.x), Mathf.FloorToInt(camPos.y));

            transform.position = new Vector3(snapPos.x, snapPos.y, 0);

            if (snapPos != _lastSnapPos)
            {
                _lastSnapPos = snapPos;
                if (_material != null)
                {
                    _material.SetVector("_WorldParams", new Vector4(snapPos.x, snapPos.y, MapManager.Instance.WorldWidth, MapManager.Instance.WorldHeight));
                }
                RequestVisibleTextures(snapPos);
            }

            if (_material != null)
            {
                _material.SetVector("_WorldOffset", new Vector4(camPos.x - snapPos.x, camPos.y - snapPos.y, 0, 0));
            }
        }

        private void LateUpdate()
        {
            if (_dirtyCellConfigs.Count > 0)
            {
                UpdateCellConfigTexture();
                _dirtyCellConfigs.Clear();
            }
            if (_atlasesDirty)
            {
                UpdateAtlasesTextureArray();
                _atlasesDirty = false;
            }
        }

        private void RequestVisibleTextures(Vector2Int snapPos)
        {
            if (!MapStorage.Instance.IsReady || _worldMapData == null) return;

            int halfW = _currentVpWidth / 2;
            int halfH = _currentVpHeight / 2;

            int worldW = MapManager.Instance.WorldWidth;
            int worldH = MapManager.Instance.WorldHeight;

            for (int y = snapPos.y - halfH; y <= snapPos.y + halfH; y++)
            {
                for (int x = snapPos.x - halfW; x <= snapPos.x + halfW; x++)
                {
                    int worldX = (x % worldW + worldW) % worldW;
                    int worldY = (y % worldH + worldH) % worldH;

                    int index = worldY * worldW + worldX;
                    if (index >= 0 && index < _worldMapData.Length)
                    {
                        Color32 data = _worldMapData[index];
                        WorldTextureManager.Instance.RequestTexture((CellType)data.r); // Foreground
                        WorldTextureManager.Instance.RequestTexture((CellType)data.a); // Background
                    }
                }
            }
        }

        private void BuildViewportMesh(int width, int height)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var localUVs = new List<Vector2>();
            var indices = new List<int>();

            float startX = -width / 2f;
            float startY = -height / 2f;

            float r = 0.70710678f;
            Vector2[] quadLocalUVs = new Vector2[]
            {
                new Vector2(-r, -r),
                new Vector2(r, -r),
                new Vector2(r, r),
                new Vector2(-r, r)
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float vx = startX + x;
                    float vy = startY + y;

                    // Foreground (Z=0)
                    int baseIdx = vertices.Count;
                    vertices.Add(new Vector3(vx, vy, 0));
                    vertices.Add(new Vector3(vx + 1, vy, 0));
                    vertices.Add(new Vector3(vx + 1, vy + 1, 0));
                    vertices.Add(new Vector3(vx, vy + 1, 0));

                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                    uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));

                    localUVs.AddRange(quadLocalUVs);

                    indices.Add(baseIdx + 0); indices.Add(baseIdx + 3); indices.Add(baseIdx + 2);
                    indices.Add(baseIdx + 2); indices.Add(baseIdx + 1); indices.Add(baseIdx + 0);

                    // Background (Z=0.1)
                    baseIdx = vertices.Count;
                    vertices.Add(new Vector3(vx, vy, 0.1f));
                    vertices.Add(new Vector3(vx + 1, vy, 0.1f));
                    vertices.Add(new Vector3(vx + 1, vy + 1, 0.1f));
                    vertices.Add(new Vector3(vx, vy + 1, 0.1f));

                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                    uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));

                    localUVs.AddRange(quadLocalUVs);

                    indices.Add(baseIdx + 0); indices.Add(baseIdx + 3); indices.Add(baseIdx + 2);
                    indices.Add(baseIdx + 2); indices.Add(baseIdx + 1); indices.Add(baseIdx + 0);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetUVs(0, uvs);
            _mesh.SetUVs(6, localUVs); // Using UV6 for localUVs as in previous version
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            _mesh.RecalculateBounds();
            _mesh.UploadMeshData(false);

            Debug.Log($"SingleMeshTerrainRenderer: Rebuilt viewport mesh {width}x{height} ({vertices.Count} verts)");
        }

        // ------------------------------------------------------------------------
        // All original helper methods (unchanged)
        // ------------------------------------------------------------------------
        private byte GetNeighborMask(int x, int serverY, int groupId)
        {
            byte mask = 0;
            int width = MapManager.Instance.WorldWidth;
            int height = MapManager.Instance.WorldHeight;

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
                    if (bgMap[x - minX, y - minY] != 0) continue;

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

            // Fallback: If still some Unloaded, pick a default passable type
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (bgMap[x, y] == CellType.Unloaded)
                    {
                        bgMap[x, y] = CellType.Empty;
                    }
                }
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

        private Vector3 GetVertexOffset(int x, int y)
        {
            if (MapManager.Instance == null || MapStorage.Instance == null || !MapStorage.Instance.IsReady)
                return Vector3.zero;

            int h = MapManager.Instance.WorldHeight;

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

            uint seed = (uint)(x * 374761397 + y * 668265263);
            seed = (seed ^ (seed >> 13)) * 1274126177;
            seed = seed ^ (seed >> 16);

            float rx = ((seed % 4) + 1) * 0.0625f;
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

        private void UpdateWorldMapTexture()
        {
            int w = MapManager.Instance.WorldWidth;
            int h = MapManager.Instance.WorldHeight;

            _worldMapTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _worldMapTex.filterMode = FilterMode.Point;
            _worldMapTex.wrapMode = TextureWrapMode.Repeat;

            Color32[] pixels = new Color32[w * h];
            ComputeBackgroundMap(0, 0, w - 1, h - 1, out var bgMap);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int serverY = h - 1 - y;
                    CellType cell = MapStorage.Instance.GetCell(x, serverY);
                    CellType bg = bgMap[x, y];

                    byte g = 0;
                    if (MapManager.Instance.TryGetTileGroup(cell, out int groupId))
                    {
                        byte mask = GetNeighborMask(x, serverY, groupId);
                        g = (byte)TileBitmaskConverter.GetDescriptor(mask);
                    }

                    byte reliefGroup = GetReliefGroup(cell);
                    bool isRelief;
                    byte reliefMask = GetReliefMask(x, serverY, reliefGroup, out isRelief);
                    float shadow = GetShadowValueForVertex(x, y);

                    byte b = (byte)((reliefMask & 0x0F) | ((byte)(shadow * 15f) << 4));
                    pixels[y * w + x] = new Color32((byte)cell, g, b, (byte)bg);
                }
            }

            _worldMapData = pixels;
            _worldMapTex.SetPixels32(pixels);
            _worldMapTex.Apply();
            _material.SetTexture("_WorldMapTex", _worldMapTex);
        }

        private void UpdateCellConfigTexture()
        {
            _cellConfigTex = new Texture2D(256, 2, TextureFormat.RGBAFloat, false);
            _cellConfigTex.filterMode = FilterMode.Point;
            _cellConfigTex.wrapMode = TextureWrapMode.Clamp;

            for (int i = 0; i < 256; i++)
            {
                CellType type = (CellType)i;
                Vector4 rect = WorldTextureManager.Instance.GetCellFrameRect(type);
                _cellConfigTex.SetPixel(i, 0, new Color(rect.x, rect.y, rect.z, rect.w));

                var config = MapManager.Instance.GetCellConfig(type);
                WorldTextureManager.Instance.TryGetTextureInfo(type, out var info);

                float animType = (float)config.Animation;
                float speed = (float)config.AnimationSpeed;
                float animFrames = (info != null) ? (float)info.AnimationFrames : 1f;
                float atlasIdx = WorldTextureManager.Instance.GetAtlasIndex(type);

                _cellConfigTex.SetPixel(i, 1, new Color(animType, speed, animFrames, atlasIdx));
            }

            _cellConfigTex.Apply();
            _material.SetTexture("_CellConfigTex", _cellConfigTex);
        }

        private void UpdateAtlasesTextureArray()
        {
            var atlases = WorldTextureManager.Instance.GetAllAtlases();
            if (atlases.Count == 0) return;

            int size = atlases[0].Size;
            if (_atlasesTexArray == null || _atlasesTexArray.depth != atlases.Count || _atlasesTexArray.width != size)
            {
                if (_atlasesTexArray != null) Destroy(_atlasesTexArray);
                _atlasesTexArray = new Texture2DArray(size, size, atlases.Count, TextureFormat.RGBA32, false);
                _atlasesTexArray.filterMode = FilterMode.Point;
                _atlasesTexArray.wrapMode = TextureWrapMode.Clamp;
            }

            for (int i = 0; i < atlases.Count; i++)
            {
                if (atlases[i].Texture != null)
                {
                    Graphics.CopyTexture(atlases[i].Texture, 0, 0, _atlasesTexArray, i, 0);
                }
            }

            if (_material != null)
            {
                _material.SetTexture("_Atlases", _atlasesTexArray);
            }
        }

        private void CleanupMaterials()
        {
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            CleanupMaterials();
            if (_worldMapTex != null) Destroy(_worldMapTex);
            if (_cellConfigTex != null) Destroy(_cellConfigTex);
            if (_atlasesTexArray != null) Destroy(_atlasesTexArray);
        }
    }
}
