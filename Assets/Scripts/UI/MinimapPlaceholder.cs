using UnityEngine;
using UnityEngine.UI;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Player;
using MinesServer.Data;
using System.Collections;
using System.Collections.Generic;

namespace Fodinae.Assets.Scripts.UI
{
    public class MinimapPlaceholder : MonoBehaviour
    {
        private Text coordinatesText;
        private Texture2D _minimapTexture;
        private RawImage _minimapImage;
        private bool _minimapReady = false;
        private const int MINIMAP_WIDTH = 128;
        private const int MINIMAP_HEIGHT = 128;
        private const int TEXTURE_SIZE = 128;
        private PlayerMovementController _player;
        private Dictionary<CellType, Color32> _cachedColors = new Dictionary<CellType, Color32>();
        private Color32[] _pixelColors;
        private MapStorage _mapStorage;
        private MapManager _mapManager;
        private int _worldWidth;
        private int _worldHeight;
        private int _cachedMinX, _cachedMinY;
        private bool _boundsCached = false;

        // ███████████████ ОПТИМИЗАЦИЯ ДЛЯ УСТРАНЕНИЯ РЫВКОВ ███████████████
        private Vector2Int _lastUpdatePos = new Vector2Int(int.MinValue, int.MinValue);
        private float _lastUpdateTime;
        private const float UPDATE_DELAY = 0.033f; // 30 FPS для миникарты (незаметно для глаз)
        private bool _updateScheduled = false;

        void Start()
        {
            StartCoroutine(InitializeMinimap());
        }

        private IEnumerator InitializeMinimap()
        {
            yield return new WaitUntil(() => MapManager.Instance != null && MapManager.Instance._isWorldInitialized);
            yield return new WaitUntil(() => MapStorage.Instance != null && MapStorage.Instance.IsReady);

            _player = FindObjectOfType<PlayerMovementController>();
            if (_player == null)
                yield break;
            _player.OnPlayerMoved += OnPlayerMoved;

            _mapManager = MapManager.Instance;
            _mapStorage = MapStorage.Instance;
            _worldWidth = _mapManager.WorldWidth;
            _worldHeight = _mapManager.WorldHeight;

            CacheAllColors();
            _pixelColors = new Color32[TEXTURE_SIZE * TEXTURE_SIZE];
            _minimapTexture = new Texture2D(TEXTURE_SIZE, TEXTURE_SIZE, TextureFormat.RGBA32, false);
            _minimapTexture.filterMode = FilterMode.Point;
            _minimapTexture.wrapMode = TextureWrapMode.Clamp;

            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("Canvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            CreateMinimapUI(canvas);
            CreateCoordinatesText(canvas);

            // Первоначальная отрисовка
            UpdateMinimapTextureFast(_player.ClientPosition.x, _player.ClientPosition.y);
            UpdateCoordinatesText(_player.ClientPosition.x, _player.ClientPosition.y);
            _lastUpdatePos = _player.ClientPosition;
            _lastUpdateTime = Time.time;

            _minimapReady = true;
        }

        /// <summary>
        /// Вызывается при движении игрока
        /// </summary>
        private void OnPlayerMoved(Vector2Int oldPos, Vector2Int newPos)
        {
            if (!_minimapReady) return;

            // Обновляем текст координат мгновенно (дешево)
            UpdateCoordinatesText(newPos.x, newPos.y);

            // █████ КРИТИЧЕСКИ ВАЖНО: НЕ ОБНОВЛЯЕМ ТЕКСТУРУ НА КАЖДОМ КАДРЕ ДВИЖЕНИЯ █████
            // Вместо этого или обновляем с задержкой, или используем отложенное обновление

            // ВАРИАНТ 1: Обновление с ограничением частоты (убирает рывки)
            if (Time.time - _lastUpdateTime >= UPDATE_DELAY)
            {
                UpdateMinimapTextureFast(newPos.x, newPos.y);
                _lastUpdateTime = Time.time;
                _lastUpdatePos = newPos;
            }
            else if (!_updateScheduled)
            {
                // Запланировать обновление через небольшой промежуток
                _updateScheduled = true;
                StartCoroutine(DelayedUpdate(newPos));
            }
        }

        private IEnumerator DelayedUpdate(Vector2Int targetPos)
        {
            yield return new WaitForSeconds(UPDATE_DELAY);
            if (_minimapReady && _player != null)
            {
                UpdateMinimapTextureFast(targetPos.x, targetPos.y);
                _lastUpdateTime = Time.time;
                _lastUpdatePos = targetPos;
            }
            _updateScheduled = false;
        }

        private void UpdateCoordinatesText(int x, int y)
        {
            if (coordinatesText != null)
            {
                coordinatesText.text = $"{x}:{y}";
            }
        }

        private void CacheAllColors()
        {
            for (int i = 0; i <= 255; i++)
            {
                CellType cellType = (CellType)i;
                Color color = _mapManager.GetCellMinimapColor(cellType);
                if (color.a < 0.01f)
                {
                    color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                _cachedColors[cellType] = (Color32)color;
            }
        }

        private void CreateMinimapUI(Canvas canvas)
        {
            GameObject minimapObj = new GameObject("Minimap");
            minimapObj.transform.SetParent(canvas.transform, false);
            _minimapImage = minimapObj.AddComponent<RawImage>();
            _minimapImage.color = Color.white;
            _minimapImage.texture = _minimapTexture;
            RectTransform rectTransform = minimapObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0);
            rectTransform.anchorMax = new Vector2(0, 0);
            rectTransform.pivot = new Vector2(0, 0);
            rectTransform.anchoredPosition = new Vector2(10, 10);
            rectTransform.sizeDelta = new Vector2(200, 200);
        }

        private void CreateCoordinatesText(Canvas canvas)
        {
            try
            {
                GameObject textObj = new GameObject("PlayerCoordinates");
                textObj.transform.SetParent(canvas.transform, false);
                coordinatesText = textObj.AddComponent<Text>();
                coordinatesText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (coordinatesText.font == null)
                {
                    coordinatesText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
                }
                coordinatesText.fontSize = 20;
                coordinatesText.color = Color.white;
                coordinatesText.alignment = TextAnchor.MiddleCenter;
                coordinatesText.text = "0:0";
                coordinatesText.fontStyle = FontStyle.Bold;
                coordinatesText.raycastTarget = false;
                Shadow shadow = textObj.AddComponent<Shadow>();
                shadow.effectColor = Color.black;
                shadow.effectDistance = new Vector2(2, -2);
                RectTransform textRect = textObj.GetComponent<RectTransform>();
                RectTransform minimapRect = _minimapImage.GetComponent<RectTransform>();
                float minimapX = minimapRect.anchoredPosition.x;
                float minimapY = minimapRect.anchoredPosition.y;
                float minimapWidth = minimapRect.sizeDelta.x;
                float minimapHeight = minimapRect.sizeDelta.y;
                textRect.anchorMin = new Vector2(0, 0);
                textRect.anchorMax = new Vector2(0, 0);
                textRect.pivot = new Vector2(0.5f, 1);
                float textX = minimapX + minimapWidth / 2;
                float textY = minimapY + minimapHeight + 5;
                textRect.anchoredPosition = new Vector2(textX, textY);
                textRect.sizeDelta = new Vector2(200, 30);
                textObj.transform.SetAsLastSibling();
                coordinatesText.enabled = true;
                textObj.SetActive(true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MinimapPlaceholder] Ошибка создания текста: {e.Message}");
            }
        }

        /// <summary>
        /// ОПТИМИЗИРОВАННОЕ обновление с кэшированием границ
        /// </summary>
        private void UpdateMinimapTextureFast(int playerX, int playerY)
        {
            // Обновляем границы только при значительном смещении
            if (!_boundsCached || Mathf.Abs(_cachedMinX - (playerX - MINIMAP_WIDTH / 2)) > 8 ||
                Mathf.Abs(_cachedMinY - (playerY - MINIMAP_HEIGHT / 2)) > 8)
            {
                _cachedMinX = playerX - MINIMAP_WIDTH / 2;
                _cachedMinY = playerY - MINIMAP_HEIGHT / 2;
                _boundsCached = true;
            }

            int minX = _cachedMinX;
            int minY = _cachedMinY;
            int index = 0;

            // Используем локальные переменные для скорости
            int textureSize = TEXTURE_SIZE;
            int worldWidth = _worldWidth;
            int worldHeight = _worldHeight;
            var pixelColors = _pixelColors;
            var mapStorage = _mapStorage;
            var cachedColors = _cachedColors;

            for (int texY = 0; texY < textureSize; texY++)
            {
                int worldY = minY + texY;
                bool yValid = worldY >= 0 && worldY < worldHeight;

                if (!yValid)
                {
                    // Заполняем строку чёрным цветом (быстро)
                    for (int texX = 0; texX < textureSize; texX++)
                    {
                        pixelColors[index++] = Color.black;
                    }
                    continue;
                }

                for (int texX = 0; texX < textureSize; texX++)
                {
                    int worldX = minX + texX;

                    if (worldX >= 0 && worldX < worldWidth)
                    {
                        try
                        {
                            CellType cellType = mapStorage.GetCell(worldX, worldY);
                            pixelColors[index++] = cachedColors[cellType];
                        }
                        catch
                        {
                            pixelColors[index++] = new Color32(32, 32, 32, 255);
                        }
                    }
                    else
                    {
                        pixelColors[index++] = Color.black;
                    }
                }
            }

            // Пакетное обновление текстуры
            _minimapTexture.SetPixels32(_pixelColors);

            // Рисуем маркер (быстро, только несколько пикселей)
            int centerX = textureSize / 2;
            int centerY = textureSize / 2;

            for (int i = -1; i <= 1; i++)
            {
                _minimapTexture.SetPixel(centerX + i, centerY, Color.white);
                _minimapTexture.SetPixel(centerX, centerY + i, Color.white);
            }
            _minimapTexture.SetPixel(centerX, centerY, Color.red);

            // Применяем изменения (false = без обновления мипмапов)
            _minimapTexture.Apply(false);
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }
            if (_minimapTexture != null)
                Destroy(_minimapTexture);
        }

        public void ForceRefresh()
        {
            if (_player != null)
            {
                UpdateMinimapTextureFast(_player.ClientPosition.x, _player.ClientPosition.y);
            }
        }
    }
}