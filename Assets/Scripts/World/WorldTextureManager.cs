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
        [SerializeField] private int _initialAtlasSize = 4096; // Increased to easily fit multiple 512x352 atlases
        [SerializeField] private int _maxAtlasSize = 4096;
        [SerializeField] private int _texturePadding = 2;

        [Header("Performance")]
        [SerializeField] private int _cellTextureSize = 16;
        [SerializeField] private bool _enableCompression = true;

        public TextureAtlas _currentAtlas;
        private CellTextureCache _textureCache;
        private readonly ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = new();
        private readonly List<TextureAtlas> _atlases = new();

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

            ClientAssetLoader.Instance.OnTextureLoaded += OnTextureLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                ClientAssetLoader.Instance.OnTextureLoaded -= OnTextureLoaded;
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
                    if (speed == 0) speed = 5; // Default speed if not provided

                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);
                }

                // Find which atlas contains this cell
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

            if (_pendingRequests.TryGetValue(cellType, out var request))
            {
                await request.Task;
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }
            }

            request = new TextureRequest(cellType);
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

                CreateFallbackTexture(cellType);

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

            // FIX: If the cell type is Empty, force it to map strictly to 32.png
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
                if (cellType == CellType.Empty) Debug.Log($"[WorldTextureManager] Successfully loaded texture for Empty cell (32.png)");
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
                HasVariations = texture.width >= 32, // Only wide textures support variations
                VariationCount = 1,
                AnimationFrames = frameHeight > 0 ? texture.height / frameHeight : 1,
                FramesPerRow = 1,
                FrameSize = 16
            };

            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(newSize, 16, _texturePadding);
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
        }

        private void CreateFallbackTexture(CellType cellType)
        {
            try
            {
                var fallbackTexture = new Texture2D(16, 16);
                var color = GetFallbackColor(cellType);
                var pixels = new Color[16 * 16];

                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
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
                    FrameSize = 16
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
    }

    internal class TextureRequest
    {
        public CellType CellType { get; }
        public UniTaskCompletionSource<bool> TaskSource { get; }

        public UniTask<bool> Task => TaskSource.Task;

        public TextureRequest(CellType cellType)
        {
            CellType = cellType;
            TaskSource = new UniTaskCompletionSource<bool>();
        }

        public void SetResult(bool success)
        {
            TaskSource.TrySetResult(success);
        }
    }
}