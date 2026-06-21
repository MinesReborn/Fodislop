using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using MinesServer.Data;

namespace Fodinae.Scripts.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private float _renderInterval = 0.033f;
        [SerializeField] private float _dragSpeed = 0.5f;

        private int _texWidth, _texHeight;
        private Canvas _canvas;
        private RawImage _rawImage;
        private Texture2D _mapTexture;
        private Color32[] _pixelBuffer;
        private Color32[] _cellColorTable = new Color32[256];
        private Color32 _defaultColor = new Color32(48, 48, 48, 255);

        private float _viewCenterX, _viewCenterY;
        private float _cellsPerPixel = 1f;
        private MapStorage _storage;
        private MapManager _manager;
        private PlayerMovementController _player;
        private InputAction _scrollAction;

        private bool _isDragging;
        private Vector2 _lastMousePos;
        private Vector2Int _lastPlayerPos;
        private float _lastRenderTime;
        private bool _initialRenderDone;
        private bool _followPlayer = true;

        private float _playerBlinkTimer;
        private bool _playerBlinkState = true;

        void Start()
        {
            _storage = MapStorage.Instance;
            _manager = MapManager.Instance;
            _player = FindObjectOfType<PlayerMovementController>();
            if (_storage == null || _manager == null)
            {
                Debug.LogError("[WorldMapRenderer] MapStorage or MapManager not available");
                enabled = false;
                return;
            }

            CreateCanvas();
            InitColorTable();
            InitTexture();

            int w = _manager.WorldWidth;
            int h = _manager.WorldHeight;
            _cellsPerPixel = Mathf.Max((float)w / _texWidth, (float)h / _texHeight, 0.05f);
            _viewCenterX = w / 2f;
            _viewCenterY = h / 2f;

            _scrollAction = new InputAction("MapScroll", binding: "<Mouse>/scroll");
            _scrollAction.performed += OnScroll;
            _scrollAction.Enable();

            if (!_canvas.gameObject.activeSelf)
                Hide();
        }

        void OnDestroy()
        {
            _scrollAction?.Dispose();
            if (_mapTexture != null) Destroy(_mapTexture);
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void Update()
        {
            if (!enabled) return;
            HandleDrag();
            HandleFollowPlayer();
            HandleQueuedRender();

            _playerBlinkTimer += Time.deltaTime;
            if (_playerBlinkTimer >= 0.5f)
            {
                _playerBlinkTimer = 0f;
                _playerBlinkState = !_playerBlinkState;
            }
        }

        public void Show()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            enabled = true;
            _lastRenderTime = -1f;
            _initialRenderDone = false;
            _followPlayer = true;
            _playerBlinkState = true;
            _playerBlinkTimer = 0f;
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            enabled = false;
        }

        public void SetViewCenter(float worldX, float worldY)
        {
            _viewCenterX = worldX;
            _viewCenterY = worldY;
        }

        private void CreateCanvas()
        {
            _canvas = new GameObject("MapCanvas").AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            _canvas.gameObject.AddComponent<CanvasScaler>();
            _canvas.gameObject.AddComponent<GraphicRaycaster>();

            var go = new GameObject("MapRawImage");
            go.transform.SetParent(_canvas.transform, false);
            _rawImage = go.AddComponent<RawImage>();
            _rawImage.color = Color.white;
            _rawImage.raycastTarget = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            DontDestroyOnLoad(_canvas.gameObject);
        }

        private void InitColorTable()
        {
            for (int i = 0; i < 256; i++)
            {
                CellType type = (CellType)i;
                Color color = _manager.GetCellMinimapColor(type);
                if (color.a < 0.01f) color = new Color(0.3f, 0.3f, 0.3f);
                _cellColorTable[i] = (Color32)color;
            }
        }

        private void InitTexture()
        {
            int baseRes = 512;
            _texHeight = baseRes;
            _texWidth = Mathf.RoundToInt(baseRes * ((float)Screen.width / Screen.height));
            _mapTexture = new Texture2D(_texWidth, _texHeight, TextureFormat.RGBA32, false);
            _mapTexture.filterMode = FilterMode.Point;
            _mapTexture.wrapMode = TextureWrapMode.Clamp;
            _pixelBuffer = new Color32[_texWidth * _texHeight];
            _rawImage.texture = _mapTexture;
        }

        private void HandleDrag()
        {
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                _isDragging = true;
                _followPlayer = false;
                _lastMousePos = Mouse.current.position.ReadValue();
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                _isDragging = false;
            }
            else if (_isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 currentPos = Mouse.current.position.ReadValue();
                Vector2 delta = currentPos - _lastMousePos;
                _lastMousePos = currentPos;

                if (delta.sqrMagnitude > 1f)
                {
                    _viewCenterX -= delta.x * _cellsPerPixel * _dragSpeed;
                    _viewCenterY -= delta.y * _cellsPerPixel * _dragSpeed;
                }
            }
        }

        private void HandleFollowPlayer()
        {
            if (_player == null) return;

            var pos = _player.ClientPosition;
            bool moved = pos.x != _lastPlayerPos.x || pos.y != _lastPlayerPos.y;
            if (!moved) return;
            _lastPlayerPos = pos;

            if (_followPlayer)
            {
                _viewCenterX = pos.x;
                _viewCenterY = pos.y;
            }
        }

        private void HandleQueuedRender()
        {
            if (_initialRenderDone && Time.time - _lastRenderTime < _renderInterval)
                return;

            _lastRenderTime = Time.time;
            _initialRenderDone = true;
            RenderViewport();
        }

        private void RenderViewport()
        {
            int worldW = _manager.WorldWidth;
            int worldH = _manager.WorldHeight;
            float cp = _cellsPerPixel;
            float cx = _viewCenterX;
            float cy = _viewCenterY;
            int texW = _texWidth;
            int texH = _texHeight;

            Color32 defaultCol = _defaultColor;
            for (int i = 0; i < _pixelBuffer.Length; i++)
                _pixelBuffer[i] = defaultCol;

            float leftWorld = cx - texW * 0.5f * cp;
            float rightWorld = cx + texW * 0.5f * cp;
            float bottomWorld = cy - texH * 0.5f * cp;
            float topWorld = cy + texH * 0.5f * cp;

            int minCellX = Mathf.Max(0, Mathf.FloorToInt(leftWorld));
            int maxCellX = Mathf.Min(worldW - 1, Mathf.CeilToInt(rightWorld));
            int minCellY = Mathf.Max(0, Mathf.FloorToInt(bottomWorld));
            int maxCellY = Mathf.Min(worldH - 1, Mathf.CeilToInt(topWorld));

            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                float worldY_top = cellY;
                float worldY_bottom = cellY + 1f;

                float pixelY_top = (worldY_top - cy) / cp + texH * 0.5f;
                float pixelY_bottom = (worldY_bottom - cy) / cp + texH * 0.5f;

                int pixY_start = Mathf.Clamp(Mathf.RoundToInt(pixelY_top), 0, texH - 1);
                int pixY_end = Mathf.Clamp(Mathf.RoundToInt(pixelY_bottom), 0, texH - 1);
                if (pixY_start >= texH || pixY_end < 0) continue;

                int serverY = worldH - 1 - cellY;

                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    float worldX_left = cellX;
                    float worldX_right = cellX + 1f;

                    float pixelX_left = (worldX_left - cx) / cp + texW * 0.5f;
                    float pixelX_right = (worldX_right - cx) / cp + texW * 0.5f;

                    int pixX_start = Mathf.Clamp(Mathf.RoundToInt(pixelX_left), 0, texW - 1);
                    int pixX_end = Mathf.Clamp(Mathf.RoundToInt(pixelX_right), 0, texW - 1);
                    if (pixX_start >= texW || pixX_end < 0) continue;

                    CellType type = _storage.GetCell(cellX, serverY);
                    Color32 color = _cellColorTable[(byte)type];

                    for (int py = pixY_start; py <= pixY_end; py++)
                    {
                        int rowStart = py * texW;
                        for (int px = pixX_start; px <= pixX_end; px++)
                        {
                            _pixelBuffer[rowStart + px] = color;
                        }
                    }
                }
            }

            if (_player != null && _playerBlinkState)
            {
                Vector2Int playerPos = _player.ClientPosition;

                if (playerPos.x >= minCellX && playerPos.x <= maxCellX && playerPos.y >= minCellY && playerPos.y <= maxCellY)
                {
                    float worldX_left = playerPos.x;
                    float worldX_right = playerPos.x + 1f;
                    float worldY_top = playerPos.y;
                    float worldY_bottom = playerPos.y + 1f;

                    float pixelX_left = (worldX_left - cx) / cp + texW * 0.5f;
                    float pixelX_right = (worldX_right - cx) / cp + texW * 0.5f;
                    float pixelY_top = (worldY_top - cy) / cp + texH * 0.5f;
                    float pixelY_bottom = (worldY_bottom - cy) / cp + texH * 0.5f;

                    int pixX_start = Mathf.Clamp(Mathf.RoundToInt(pixelX_left), 0, texW - 1);
                    int pixX_end = Mathf.Clamp(Mathf.RoundToInt(pixelX_right), 0, texW - 1);
                    int pixY_start = Mathf.Clamp(Mathf.RoundToInt(pixelY_top), 0, texH - 1);
                    int pixY_end = Mathf.Clamp(Mathf.RoundToInt(pixelY_bottom), 0, texH - 1);

                    Color32 playerColor = new Color32(255, 0, 0, 255);

                    for (int py = pixY_start; py <= pixY_end; py++)
                    {
                        int rowStart = py * texW;
                        for (int px = pixX_start; px <= pixX_end; px++)
                        {
                            _pixelBuffer[rowStart + px] = playerColor;
                        }
                    }
                }
            }

            _mapTexture.SetPixels32(_pixelBuffer);
            _mapTexture.Apply(false);
        }

        private void OnScroll(InputAction.CallbackContext ctx)
        {
            if (!enabled || _canvas == null || !_canvas.gameObject.activeSelf) return;

            float delta = ctx.ReadValue<Vector2>().y;
            if (Mathf.Abs(delta) < 0.01f) return;

            _cellsPerPixel *= (1f - delta * 0.1f);
            _cellsPerPixel = Mathf.Clamp(_cellsPerPixel, 0.02f, 10f);
        }
    }
}
