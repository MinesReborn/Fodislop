using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    public class WorldTextureManager : MonoBehaviour
    {
        private static WorldTextureManager _instance;
        public static WorldTextureManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<WorldTextureManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[WorldTextureManager]");
                        _instance = go.AddComponent<WorldTextureManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Atlas Configuration")]
        [SerializeField] private int _initialAtlasSize = 4096;
        [SerializeField] private int _maxAtlasSize = 4096;
        [SerializeField] private int _texturePadding = 2;

        [Header("Performance")]
        [SerializeField] private int _cellTextureSize = 32;
        [SerializeField] private bool _enableCompression = true;

        public TextureAtlas _currentAtlas;
        private CellTextureCache _textureCache;
        private readonly ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = new();
        private readonly List<TextureAtlas> _atlases = new();

        // Кэш текстуры фона (Empty/32)
        private Texture2D _cachedEmptyTexture;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            _textureCache = new CellTextureCache();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);

            // Subscribe to texture loaded event
            ClientAssetLoader.Instance.OnTextureLoaded += OnTextureLoadedHandler;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                if (ClientAssetLoader.Instance != null)
                {
                    ClientAssetLoader.Instance.OnTextureLoaded -= OnTextureLoadedHandler;
                }
            }
        }

        private void OnTextureLoadedHandler(string filename, Texture2D texture)
        {
            OnTextureLoaded?.Invoke(filename, texture);
        }

        public event Action<string, Texture2D> OnTextureLoaded;

        public bool HasAnimations(CellType cellType)
        {
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return textureInfo.AnimationFrames > 1;
            }
            return false;
        }

        public AtlasCoordinate GetCellTextureCoordinateSync(CellType cellType, int globalX, int globalY)
        {
            if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
            {
                return AtlasCoordinate.Empty;
            }

            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var variation = CalculateVariation(textureInfo, globalX, globalY);

                int frameIndex = 0;
                int frameHeight = 0;

                if (textureInfo.AnimationFrames > 1)
                {
                    byte speed = MapManager.Instance.GetAnimationSpeed(cellType);
                    if (speed == 0) speed = 5;

                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);
                }

                foreach (var atlas in _atlases)
                {
                    if (atlas.ContainsCell(cellType))
                    {
                        return atlas.GetWrappedCoordinate(cellType, globalX, globalY, variation, frameHeight, frameIndex);
                    }
                }
            }

            return AtlasCoordinate.Empty;
        }

        public async UniTask<AtlasCoordinate> GetCellTextureCoordinate(CellType cellType, int globalX, int globalY)
        {
            if (cellType == CellType.Unloaded || cellType == CellType.Pregener)
            {
                return AtlasCoordinate.Empty;
            }

            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return GetCellTextureCoordinateSync(cellType, globalX, globalY);
            }

            if (_pendingRequests.TryGetValue(cellType, out var existingRequest))
            {
                await existingRequest.Task;
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }
            }

            var request = new TextureRequest(cellType);
            _pendingRequests.TryAdd(cellType, request);

            try
            {
                await LoadTexture(cellType);
                request.SetResult(true);

                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load texture for cell type {cellType}: {ex.Message}");
                request.SetResult(false);

                CreateFallbackTexture(cellType, globalX, globalY);

                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }

                return AtlasCoordinate.Empty;
            }
            finally
            {
                _pendingRequests.TryRemove(cellType, out _);
            }

            return AtlasCoordinate.Empty;
        }

        private async UniTask LoadTexture(CellType cellType)
        {
            var filename = $"cells/{(int)cellType}.png";

            if (cellType == CellType.Empty)
            {
                filename = "cells/32.png";
            }

            var cachedTexture = _textureCache.GetCachedTexture(cellType);
            if (cachedTexture != null)
            {
                await AddTextureToAtlas(cellType, cachedTexture);
                return;
            }

            Texture2D texture = null;
            try
            {
                texture = await ClientAssetLoader.Instance.GetTextureAsync(filename);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldTextureManager] Warning loading {filename}: {ex.Message}");
            }

            if (texture != null)
            {
                if (cellType == CellType.Empty)
                {
                    Debug.Log($"[WorldTextureManager] Successfully loaded texture for Empty cell (32.png)");
                    _cachedEmptyTexture = texture;
                }

                // Не модифицируем текстуру — оставляем как есть с прозрачностью
                await AddTextureToAtlas(cellType, texture);
            }
            else
            {
                throw new Exception($"Failed to load texture for {cellType}");
            }
        }

        private async UniTask AddTextureToAtlas(CellType cellType, Texture2D texture)
        {
            await UniTask.SwitchToMainThread();

            int frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);

            var textureInfo = new CellTextureInfo
            {
                CellType = cellType,
                BaseTexture = texture,
                HasVariations = texture.width >= _cellTextureSize,
                VariationCount = 1,
                AnimationFrames = frameHeight > 0 ? texture.height / frameHeight : 1,
                FramesPerRow = 1,
                FrameSize = _cellTextureSize
            };

            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(newSize, _cellTextureSize, _texturePadding);
                    _atlases.Add(newAtlas);
                    _currentAtlas = newAtlas;

                    if (!_currentAtlas.TryAddTexture(cellType, texture, out coordinate))
                    {
                        Debug.LogError($"Failed to add texture to new atlas of size {newSize}");
                        return;
                    }
                }
                else
                {
                    Debug.LogError($"Atlas size limit reached ({_maxAtlasSize}). Cannot add more textures.");
                    return;
                }
            }

            _textureCache.AddTexture(cellType, textureInfo);

            await _currentAtlas.UpdateAtlasTexture();
            OnTextureLoaded?.Invoke($"cells/{(int)cellType}.png", texture);
        }

        private CellVariation CalculateVariation(CellTextureInfo textureInfo, int globalX, int globalY)
        {
            if (!textureInfo.HasVariations)
                return CellVariation.None;

            int variationX = (globalX % 2 + 2) % 2;
            int variationY = (globalY % 2 + 2) % 2;

            return new CellVariation
            {
                Horizontal = variationX == 1,
                Vertical = variationY == 1
            };
        }

        public List<TextureAtlas> GetAllAtlases()
        {
            return _atlases;
        }

        public TextureAtlas GetAtlasForCell(CellType cellType)
        {
            foreach (var atlas in _atlases)
            {
                if (atlas.ContainsCell(cellType))
                    return atlas;
            }
            return null;
        }

        public void Clear()
        {
            _textureCache.Clear();
            foreach (var atlas in _atlases)
            {
                atlas.Clear();
            }
            _atlases.Clear();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);
            _cachedEmptyTexture = null;
        }

        private void CreateFallbackTexture(CellType cellType, int globalX, int globalY)
        {
            try
            {
                // Определяем, какой фон будет под клеткой
                Color dominantColor = GetDominantBackgroundColor(globalX, globalY);

                var fallbackTexture = new Texture2D(_cellTextureSize, _cellTextureSize);
                var pixels = new Color[_cellTextureSize * _cellTextureSize];

                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = dominantColor;
                }

                fallbackTexture.SetPixels(pixels);
                fallbackTexture.Apply();

                var textureInfo = new CellTextureInfo
                {
                    CellType = cellType,
                    BaseTexture = fallbackTexture,
                    HasVariations = false,
                    VariationCount = 1,
                    AnimationFrames = 1,
                    FramesPerRow = 1,
                    FrameSize = _cellTextureSize
                };

                if (_currentAtlas.TryAddTexture(cellType, fallbackTexture, out var coordinate))
                {
                    _textureCache.AddTexture(cellType, textureInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create fallback texture for cell type {cellType}: {ex.Message}");
            }
        }

        private Color GetDominantBackgroundColor(int globalX, int globalY)
        {
            // Собираем цвета со всех окружающих клеток в радиусе 2 клеток
            var backgroundColors = new List<Color>();
            int radius = 2; // Радиус поиска вокруг клетки

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    // Получаем тип клетки по координатам
                    CellType neighborType = GetCellTypeAt(globalX + dx, globalY + dy);

                    // Если клетка пустая (32) или дорога - берём её цвет
                    if (neighborType == CellType.Empty || neighborType == CellType.Road)
                    {
                        Color cellColor = GetCellBackgroundColor(neighborType);
                        // Добавляем цвет несколько раз для веса (чем ближе к центру, тем больше вес)
                        int weight = Mathf.Max(0, radius - Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) + 1);
                        for (int w = 0; w < weight; w++)
                        {
                            backgroundColors.Add(cellColor);
                        }
                    }
                }
            }

            // Если нашли окружающие фоновые клетки - возвращаем самый частый цвет
            if (backgroundColors.Count > 0)
            {
                return GetMostFrequentColor(backgroundColors);
            }

            // Если ничего не нашли - возвращаем стандартный цвет для пустой клетки
            return GetCellBackgroundColor(CellType.Empty);
        }

        private CellType GetCellTypeAt(int x, int y)
        {
            // Пытаемся получить реальный тип клетки из MapStorage
            if (MapStorage.Instance != null && MapStorage.Instance.cellLayer != null)
            {
                try
                {
                    return MapStorage.Instance.cellLayer[x, y];
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"GetCellTypeAt error: {ex.Message}");
                }
            }

            return CellType.Empty;
        }

        private Color GetCellBackgroundColor(CellType cellType)
        {
            switch (cellType)
            {
                case CellType.Empty:
                    return new Color(0.2f, 0.2f, 0.2f, 1f);
                case CellType.Road:
                    return new Color(0.8f, 0.7f, 0.5f, 1f);
                case CellType.WhiteSand:
                    return new Color(0.95f, 0.9f, 0.7f, 1f);
                case CellType.GrayAcid:
                    return new Color(0.6f, 0.8f, 0.6f, 1f);
                default:
                    return new Color(0.2f, 0.2f, 0.2f, 1f);
            }
        }

        private Color GetMostFrequentColor(List<Color> colors)
        {
            if (colors.Count == 0)
                return new Color(0.2f, 0.2f, 0.2f);

            var colorGroups = new Dictionary<int, List<Color>>();
            float tolerance = 0.1f;

            foreach (var color in colors)
            {
                bool added = false;
                foreach (var key in colorGroups.Keys.ToList())
                {
                    var existingColor = colorGroups[key][0];
                    if (Mathf.Abs(color.r - existingColor.r) < tolerance &&
                        Mathf.Abs(color.g - existingColor.g) < tolerance &&
                        Mathf.Abs(color.b - existingColor.b) < tolerance)
                    {
                        colorGroups[key].Add(color);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    var hash = color.GetHashCode();
                    colorGroups[hash] = new List<Color> { color };
                }
            }

            var mostFrequentGroup = colorGroups.OrderByDescending(g => g.Value.Count).First();

            var avgColor = new Color(0, 0, 0, 0);
            foreach (var c in mostFrequentGroup.Value)
            {
                avgColor += c;
            }
            avgColor /= mostFrequentGroup.Value.Count;

            return avgColor;
        }

        private Color GetFallbackColor(CellType cellType)
        {
            if (MapManager.Instance != null)
            {
                var serverColor = MapManager.Instance.GetCellMinimapColor(cellType);
                if (serverColor.a > 0) return serverColor;
            }

            return cellType switch
            {
                CellType.Empty => new Color(0.2f, 0.2f, 0.2f),
                CellType.Road => new Color(0.8f, 0.8f, 0.8f),
                CellType.Boulder1 => Color.black,
                CellType.WhiteSand => new Color(1f, 0.92f, 0.8f),
                CellType.GrayAcid => new Color(0f, 1f, 0f),
                _ => Color.magenta
            };
        }

        public Texture2D GetCachedTexture(CellType cellType)
        {
            return _textureCache.GetCachedTexture(cellType);
        }

        public string GetCacheStats()
        {
            return _textureCache.GetCacheStats();
        }

        public Texture2D GetEmptyTexture()
        {
            return _cachedEmptyTexture;
        }
    }

    public class TextureRequest
    {
        public CellType CellType { get; }
        private readonly UniTaskCompletionSource<bool> _taskSource;

        public UniTask<bool> Task => _taskSource.Task;

        public TextureRequest(CellType cellType)
        {
            CellType = cellType;
            _taskSource = new UniTaskCompletionSource<bool>();
        }

        public void SetResult(bool success)
        {
            _taskSource.TrySetResult(success);
        }
    }
}