using System.Collections.Generic;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Fodinae.Scripts.UI
{
    /// <summary>
    /// Chunk-batched minimap renderer with time-throttled updates and async GPU upload.
    /// No coroutines, no per-cell WorldLayer.<T> indexer calls — reads whole chunks at once.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 200;

        // UI
        private Text _coordinatesText;
        private RawImage _minimapImage;
        private Texture2D _minimapTexture;
        private GameObject _minimapObj;
        private GameObject _textObj;

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

        // Toggle state
        private bool _isVisible = true;
        private string _togglePrefKey = "MinimapVisible";

        private const int TEXTURE_SIZE = GameConstants.UI.MINIMAP_WIDTH; // 128
        private const float UPDATE_DELAY = 0.1f; // 10 FPS — sufficient for minimap

        private static readonly Color32 UnloadedColor = new(32, 32, 32, 255);
        private static readonly Color32 OutOfBoundsColor = new(0, 0, 0, 255);
        private static readonly Color32 MarkerColor = Color.white;
        private static readonly Color32 CenterColor = Color.red;

        protected void Start()
        {
            _mapManager = MapManager.Instance;
            if (_mapManager == null)
            {
                Debug.LogError("[MinimapController] MapManager.Instance is null");
                return;
            }

            _mapStorage = ServiceLocator.Resolve<IWorldDataStorage>();

            CacheCellColors();

            _minimapTexture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixelColors = new Color32[TEXTURE_SIZE * TEXTURE_SIZE];

            CreateUI();

            _player = PlayerMovementController.LocalPlayer;
            if (_player != null)
            {
                _player.OnPlayerMoved += OnPlayerMoved;
            }
            else
            {
                PlayerMovementController.OnLocalPlayerSpawned += OnPlayerSpawned;
            }
        }

        private void OnPlayerSpawned(PlayerMovementController player)
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnPlayerSpawned;
            _player = player;
            if (_player != null)
            {
                _player.OnPlayerMoved += OnPlayerMoved;
                if (_ready)
                {
                    UpdateCoordinatesText(_player.Position.x, _player.Position.y);
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }
            }
        }

        /// <summary>
        /// One-time initialization check (replaces coroutine).
        /// Runs every frame until the world is ready, then becomes a no-op.
        /// </summary>
        protected void Update()
        {
            if (!_ready)
            {
                TryInitialize();
            }

            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                ToggleVisibility();
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
                UpdateCoordinatesText(_player.Position.x, _player.Position.y);
                RefreshTexture(_player.Position.x, _player.Position.y);
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
            SetVisible(_isVisible);
        }

        private void CreateUI()
        {
            // UI Toolkit renders independently from uGUI. A dedicated overlay canvas
            // prevents a full-screen UIDocument from covering the minimap.
            GameObject canvasObj = new("MinimapCanvas");
            canvasObj.transform.SetParent(transform, false);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;

            // Draw above the world, but below the UI Toolkit HUD and its modal panels.
            canvas.sortingOrder = -1;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Minimap image
            _minimapObj = new GameObject("Minimap");
            _minimapObj.transform.SetParent(canvas.transform, false);
            _minimapImage = _minimapObj.AddComponent<RawImage>();
            _minimapImage.texture = _minimapTexture;
            _minimapImage.color = Color.white;

            RectTransform rt = _minimapObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(10, 10);
            rt.sizeDelta = new Vector2(_uiSize, _uiSize);

            // Coordinates text
            _textObj = new GameObject("PlayerCoordinates");
            _textObj.transform.SetParent(canvas.transform, false);
            _coordinatesText = _textObj.AddComponent<Text>();
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

            Shadow shadow = _textObj.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(2, -2);

            RectTransform textRt = _textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.zero;
            textRt.pivot = new Vector2(0.5f, 1f);
            textRt.anchoredPosition = new Vector2(10 + (_uiSize * 0.5f), 10 + _uiSize + 5);
            textRt.sizeDelta = new Vector2(200, 30);
            _textObj.transform.SetAsLastSibling();

            // The minimap is a permanent in-game HUD element. It becomes visible
            // only once a world has been initialized.
            _isVisible = true;
            SetVisible(false);
        }

        protected void OnEnable()
        {
            if (_ready)
            {
                SetVisible(_isVisible);
            }
        }

        protected void OnDisable()
        {
            SetVisible(false);
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

            if (_player != null)
            {
                UpdateCoordinatesText(_player.Position.x, _player.Position.y);
            }

            float now = Time.time;
            if (now - _lastUpdateTime >= UPDATE_DELAY)
            {
                _lastUpdateTime = now;
                _lastUpdatePos = newPos;
                RefreshTexture(newPos.x, newPos.y);
            }
        }

        private void RefreshTexture(int playerX, int playerY)
        {
            const int HALF_SIZE = TEXTURE_SIZE / 2;
            int minX = playerX - HALF_SIZE;
            const int TEX_SIZE = TEXTURE_SIZE;
            Color32[] colors = _pixelColors;
            Dictionary<CellType, Color32> cellColors = _cellColors;
            Dictionary<int, CellType[]> cache = _chunkCache;
            cache.Clear();

            int index = 0;

            for (int texY = 0; texY < TEX_SIZE; texY++)
            {
                // texY = 0 is bottom of screen (deeper underground, larger Server Y)
                // texY = TEX_SIZE - 1 is top of screen (towards surface, smaller Server Y)
                int serverY = playerY + HALF_SIZE - texY;

                if (serverY < 0 || serverY >= _worldHeight)
                {
                    // Entire row is out of bounds
                    int end = index + TEX_SIZE;
                    while (index < end)
                    {
                        colors[index++] = OutOfBoundsColor;
                    }

                    continue;
                }

                // Column-major chunk indexing for WorldLayer<T>
                int chunkY = serverY / _chunkSize;
                int localY = serverY % _chunkSize;

                for (int texX = 0; texX < TEX_SIZE; texX++)
                {
                    int serverX = minX + texX;

                    if (serverX < 0 || serverX >= _worldWidth)
                    {
                        colors[index++] = OutOfBoundsColor;
                        continue;
                    }

                    int chunkX = serverX / _chunkSize;
                    int chunkIdx = chunkY + (chunkX * _heightChunks);

                    if (!cache.TryGetValue(chunkIdx, out CellType[] chunk))
                    {
                        // Don't create missing chunks, don't touch LRU (no cache pollution)
                        chunk = _cellLayer.GetChunk(chunkIdx, false, false);
                        cache[chunkIdx] = chunk;
                    }

                    if (chunk != null)
                    {
                        int localIdx = localY + ((serverX % _chunkSize) * _chunkSize);
                        colors[index++] = cellColors[chunk[localIdx]];
                    }
                    else
                    {
                        colors[index++] = UnloadedColor;
                    }
                }
            }

            // Draw player marker (plus sign)
            const int cx = HALF_SIZE;
            colors[(cx * TEX_SIZE) + cx - 1] = MarkerColor;
            colors[(cx * TEX_SIZE) + cx] = CenterColor;
            colors[(cx * TEX_SIZE) + cx + 1] = MarkerColor;
            colors[((cx - 1) * TEX_SIZE) + cx] = MarkerColor;
            colors[((cx + 1) * TEX_SIZE) + cx] = MarkerColor;

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
                RefreshTexture(_player.Position.x, _player.Position.y);
            }
        }

        protected void OnDestroy()
        {
            PlayerMovementController.OnLocalPlayerSpawned -= OnPlayerSpawned;

            if (_player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }

            if (_minimapTexture != null)
            {
                Destroy(_minimapTexture);
            }
        }

        private void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            SetVisible(_isVisible);
            PlayerPrefs.SetInt(_togglePrefKey, _isVisible ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void SetVisible(bool visible)
        {
            if (_minimapObj != null)
            {
                _minimapObj.SetActive(visible);
            }

            if (_textObj != null)
            {
                _textObj.SetActive(visible);
            }
        }
    }
}
