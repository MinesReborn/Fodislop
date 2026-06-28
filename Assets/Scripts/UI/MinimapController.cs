using System.Collections.Generic;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinae.Scripts.UI
{
    /// <summary>
    /// Chunk-batched minimap renderer with time-throttled updates and async GPU upload.
    /// No coroutines, no per-cell WorldLayer<T> indexer calls — reads whole chunks at once.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 200;

        // UI
        private Text _coordinatesText;
        private RawImage _minimapImage;
        private Texture2D _minimapTexture;

        // World state
        private PlayerMovementController _player;
        private MapStorage _mapStorage;
        private MapManager _mapManager;
        private WorldLayer<CellType> _cellLayer;
        private int _worldWidth;
        private int _worldHeight;
        private int _chunkSize;
        private int _heightChunks;

        // Pixel buffer and cell color cache
        private Color32[] _pixelColors;
        private readonly Dictionary<CellType, Color32> _cellColors = new(256);

        // Per-update chunk cache (reused, cleared each frame — allocation-free)
        private readonly Dictionary<int, CellType[]> _chunkCache = new();

        // Throttle state
        private Vector2Int _lastUpdatePos;
        private float _lastUpdateTime;
        private bool _ready;

        private const int TextureSize = GameConstants.UI.MINIMAP_WIDTH; // 128
        private const float UpdateDelay = 0.1f; // 10 FPS — sufficient for minimap

        private static readonly Color32 UnloadedColor = new(32, 32, 32, 255);
        private static readonly Color32 OutOfBoundsColor = new(0, 0, 0, 255);
        private static readonly Color32 MarkerColor = Color.white;
        private static readonly Color32 CenterColor = Color.red;

        private void Start()
        {
            _mapManager = MapManager.Instance;
            if (_mapManager == null)
            {
                Debug.LogError("[MinimapController] MapManager.Instance is null");
                return;
            }

            _mapStorage = MapStorage.Instance;

            CacheCellColors();

            _minimapTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixelColors = new Color32[TextureSize * TextureSize];

            CreateUI();

            _player = FindFirstObjectByType<PlayerMovementController>();
            if (_player != null)
            {
                _player.OnPlayerMoved += OnPlayerMoved;
            }
        }

        /// <summary>
        /// One-time initialization check (replaces coroutine).
        /// Runs every frame until the world is ready, then becomes a no-op.
        /// </summary>
        private void Update()
        {
            if (!_ready)
            {
                TryInitialize();
            }
        }

        private void TryInitialize()
        {
            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_mapStorage == null || !_mapStorage.IsReady)
            {
                return;
            }

            InitializeWorldState();

            // Initial render
            if (_player != null)
            {
                UpdateCoordinatesText(_player.ClientPosition.x, _player.ClientPosition.y);
                RefreshTexture(_player.ClientPosition.x, _player.ClientPosition.y);
            }
        }

        private void CacheCellColors()
        {
            for (int i = 0; i <= 255; i++)
            {
                CellType cellType = (CellType)i;
                Color color = _mapManager.GetCellMinimapColor(cellType);
                if (color.a < 0.01f)
                {
                    color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }

                _cellColors[cellType] = (Color32)color;
            }
        }

        private void InitializeWorldState()
        {
            _cellLayer = _mapStorage.CellLayer;
            _worldWidth = _mapManager.WorldWidth;
            _worldHeight = _mapManager.WorldHeight;
            _chunkSize = _cellLayer.ChunkSize;
            _heightChunks = _cellLayer.HeightChunks;
            _ready = true;
        }

        private void CreateUI()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Minimap image
            GameObject minimapObj = new("Minimap");
            minimapObj.transform.SetParent(canvas.transform, false);
            _minimapImage = minimapObj.AddComponent<RawImage>();
            _minimapImage.texture = _minimapTexture;
            _minimapImage.color = Color.white;

            RectTransform rt = minimapObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(10, 10);
            rt.sizeDelta = new Vector2(_uiSize, _uiSize);

            // Coordinates text
            GameObject textObj = new("PlayerCoordinates");
            textObj.transform.SetParent(canvas.transform, false);
            _coordinatesText = textObj.AddComponent<Text>();
            _coordinatesText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_coordinatesText.font == null)
            {
                _coordinatesText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }

            _coordinatesText.fontSize = 20;
            _coordinatesText.color = Color.white;
            _coordinatesText.alignment = TextAnchor.MiddleCenter;
            _coordinatesText.text = string.Empty;
            _coordinatesText.fontStyle = FontStyle.Bold;
            _coordinatesText.raycastTarget = false;

            Shadow shadow = textObj.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(2, -2);

            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.zero;
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.anchoredPosition = new Vector2(10 + (_uiSize * 0.5f), 10 + _uiSize + 5);
            textRt.sizeDelta = new Vector2(200, 30);
            textObj.transform.SetAsLastSibling();
        }

        private void OnPlayerMoved(Vector2Int oldPos, Vector2Int newPos)
        {
            if (!_ready)
            {
                TryInitialize();
                if (!_ready)
                {
                    return;
                }
            }

            UpdateCoordinatesText(newPos.x, newPos.y);

            float now = Time.time;
            if (now - _lastUpdateTime >= UpdateDelay)
            {
                _lastUpdateTime = now;
                _lastUpdatePos = newPos;
                RefreshTexture(newPos.x, newPos.y);
            }
        }

        private void RefreshTexture(int playerX, int playerY)
        {
            int halfSize = TextureSize / 2;
            int minX = playerX - halfSize;
            int minY = playerY - halfSize;
            int texSize = TextureSize;
            Color32[] colors = _pixelColors;
            Dictionary<CellType, Color32> cellColors = _cellColors;
            Dictionary<int, CellType[]> cache = _chunkCache;
            cache.Clear();

            int index = 0;

            for (int texY = 0; texY < texSize; texY++)
            {
                int worldY = minY + texY;

                if (worldY < 0 || worldY >= _worldHeight)
                {
                    // Entire row is out of bounds
                    int end = index + texSize;
                    while (index < end)
                    {
                        colors[index++] = OutOfBoundsColor;
                    }

                    continue;
                }

                int serverY = Fodinae.Scripts.Utils.CoordinateUtils.UnityToServerY(worldY, _worldHeight);

                // Column-major chunk indexing for WorldLayer<T>
                int chunkY = serverY / _chunkSize;
                int localY = serverY % _chunkSize;

                for (int texX = 0; texX < texSize; texX++)
                {
                    int worldX = minX + texX;

                    if (worldX < 0 || worldX >= _worldWidth)
                    {
                        colors[index++] = OutOfBoundsColor;
                        continue;
                    }

                    int chunkX = worldX / _chunkSize;
                    int chunkIdx = chunkY + (chunkX * _heightChunks);

                    if (!cache.TryGetValue(chunkIdx, out CellType[] chunk))
                    {
                        // Don't create missing chunks, don't touch LRU (no cache pollution)
                        chunk = _cellLayer.GetChunk(chunkIdx, false, false);
                        cache[chunkIdx] = chunk;
                    }

                    if (chunk != null)
                    {
                        int localIdx = localY + ((worldX % _chunkSize) * _chunkSize);
                        colors[index++] = cellColors[chunk[localIdx]];
                    }
                    else
                    {
                        colors[index++] = UnloadedColor;
                    }
                }
            }

            // Draw player marker (plus sign)
            int cx = halfSize;
            colors[(cx * texSize) + cx - 1] = MarkerColor;
            colors[(cx * texSize) + cx] = CenterColor;
            colors[(cx * texSize) + cx + 1] = MarkerColor;
            colors[((cx - 1) * texSize) + cx] = MarkerColor;
            colors[((cx + 1) * texSize) + cx] = MarkerColor;

            _minimapTexture.SetPixels32(colors);
            _minimapTexture.Apply(true); // Async GPU upload — non-blocking
        }

        private void UpdateCoordinatesText(int x, int y)
        {
            if (_coordinatesText != null)
            {
                _coordinatesText.text = $"{x}:{y}";
            }
        }

        public void ForceRefresh()
        {
            if (_player != null && _ready)
            {
                RefreshTexture(_player.ClientPosition.x, _player.ClientPosition.y);
            }
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }

            if (_minimapTexture != null)
            {
                Destroy(_minimapTexture);
            }
        }
    }
}
