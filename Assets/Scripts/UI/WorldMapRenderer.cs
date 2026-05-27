using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Player;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private float _renderInterval = 1f;
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
        }

        public void Show()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            enabled = true;
            _lastRenderTime = -1f;
            _initialRenderDone = false;
            _followPlayer = true;
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
            int halfTexW = _texWidth / 2;
            int halfTexH = _texHeight / 2;

            float left = _viewCenterX - halfTexW * _cellsPerPixel;

            for (int py = 0; py < _texHeight; py++)
            {
                float worldY = _viewCenterY - (halfTexH - py) * _cellsPerPixel;
                int cellY = Mathf.RoundToInt(worldY);
                int rowStart = py * _texWidth;

                if (cellY < 0 || cellY >= worldH)
                {
                    System.Array.Fill(_pixelBuffer, _defaultColor, rowStart, _texWidth);
                    continue;
                }

                int serverY = worldH - 1 - cellY;

                for (int px = 0; px < _texWidth; px++)
                {
                    int cellX = Mathf.RoundToInt(left + px * _cellsPerPixel);
                    if (cellX >= 0 && cellX < worldW)
                    {
                        CellType type = _storage.GetCell(cellX, serverY);
                        _pixelBuffer[rowStart + px] = _cellColorTable[(byte)type];
                    }
                    else
                    {
                        _pixelBuffer[rowStart + px] = _defaultColor;
                    }
                }
            }

            if (_player != null)
            {
                int mx = Mathf.RoundToInt((_player.ClientPosition.x - _viewCenterX) / _cellsPerPixel + halfTexW);
                int my = Mathf.RoundToInt((_player.ClientPosition.y - _viewCenterY) / _cellsPerPixel + halfTexH);
                var red = new Color32(255, 0, 0, 255);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx != 0 && dy != 0) continue;
                        int px = mx + dx;
                        int py = my + dy;
                        if (px >= 0 && px < _texWidth && py >= 0 && py < _texHeight)
                            _pixelBuffer[py * _texWidth + px] = red;
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
