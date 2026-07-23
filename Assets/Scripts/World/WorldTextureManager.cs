using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    public class WorldTextureManager : SingletonMonoBehaviour<WorldTextureManager>
    {
        [Header("Atlas Configuration")]
        [SerializeField]
        private int _initialAtlasSize = 4096;
        [SerializeField]
        private int _maxAtlasSize = 4096;
        [SerializeField]
        private int _texturePadding = 2;

        [Header("Performance")]
        [SerializeField]
        private int _cellTextureSize = RenderingConstants.CELL_SIZE;

        [System.NonSerialized]
        public TextureAtlas _currentAtlas;
        private CellTextureCache _textureCache;
        private Texture2D _flowMapTexture;
        public const CellType FLOW_MAP_CELL_TYPE = (CellType)254;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests;
        private List<TextureAtlas> _atlases;

        private Texture2D _cachedEmptyTexture;

        protected override void OnAwake()
        {
            Initialize();
        }

        protected override void OnDestroyed()
        {
            if (ClientAssetLoader.InstanceIfExists != null)
            {
                ClientAssetLoader.InstanceIfExists.OnTextureLoaded -= OnTextureLoadedHandler;
            }

            _textureCache?.Clear();
            if (_atlases != null)
            {
                foreach (var atlas in _atlases)
                {
                    atlas?.Dispose();
                }

                _atlases.Clear();
            }
        }

        private void Initialize()
        {
            if (_textureCache != null && _atlases != null && _pendingRequests != null)
            {
                return;
            }

            _textureCache = new CellTextureCache();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);

            _atlases = new List<TextureAtlas>();
            _atlases.Add(_currentAtlas);

            _pendingRequests = new ConcurrentDictionary<CellType, TextureRequest>();

            GenerateFlowMap();
            EnsureFlowMapInAtlas(_currentAtlas);

            if (Application.isPlaying)
            {
                var assetLoader = ClientAssetLoader.InstanceIfExists;
                if (assetLoader != null)
                {
                    assetLoader.OnTextureLoaded -= OnTextureLoadedHandler;
                    assetLoader.OnTextureLoaded += OnTextureLoadedHandler;
                }
                else if (ClientAssetLoader.Instance != null)
                {
                    ClientAssetLoader.Instance.OnTextureLoaded -= OnTextureLoadedHandler;
                    ClientAssetLoader.Instance.OnTextureLoaded += OnTextureLoadedHandler;
                }
            }
        }

        private void EnsureInitialized()
        {
            if (_textureCache == null || _atlases == null || _pendingRequests == null)
            {
                Initialize();
            }
        }

        private void GenerateFlowMap()
        {
            _flowMapTexture = new Texture2D(12, 10, TextureFormat.RGBA32, false);
            _flowMapTexture.name = "ShimmerFlowMap";
            _flowMapTexture.filterMode = FilterMode.Point;
            _flowMapTexture.wrapMode = TextureWrapMode.Repeat;

            var random = new System.Random(42);
            var pixels = new Color[12 * 10];
            for (int i = 0; i < pixels.Length; i++)
            {
                float h = (float)random.NextDouble();
                pixels[i] = Color.HSVToRGB(h, 1f, 1f);
            }

            _flowMapTexture.SetPixels(pixels);
            _flowMapTexture.Apply();

            var textureInfo = new CellTextureInfo
            {
                CellType = FLOW_MAP_CELL_TYPE,
                BaseTexture = _flowMapTexture,
                HasVariations = false,
                VariationCount = 1,
                AnimationFrames = 1,
                FramesPerRow = 1,
                FrameSize = 12,
            };
            _textureCache.AddTexture(FLOW_MAP_CELL_TYPE, textureInfo);
        }

        private void EnsureFlowMapInAtlas(TextureAtlas atlas)
        {
            if (_flowMapTexture == null)
            {
                GenerateFlowMap();
            }

            if (!atlas.ContainsCell(FLOW_MAP_CELL_TYPE))
            {
                atlas.TryAddTexture(FLOW_MAP_CELL_TYPE, _flowMapTexture, out _);
                atlas.CopyTextureToAtlas(FLOW_MAP_CELL_TYPE, _flowMapTexture);
            }
        }

        private void OnTextureLoadedHandler(string filename, Texture2D texture)
        {
            OnTextureLoaded?.Invoke(filename, texture);
        }

        public event Action<string, Texture2D> OnTextureLoaded;

        public void RequestTexture(CellType cellType)
        {
            EnsureInitialized();
            GetCellTextureCoordinate(cellType, 0, 0).Forget();
        }

        public bool HasAnimations(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return textureInfo.AnimationFrames > 1;
            }

            return false;
        }

        public AtlasCoordinate GetFlowMapCoordinate(TextureAtlas atlas)
        {
            EnsureInitialized();
            return atlas.GetCoordinate(FLOW_MAP_CELL_TYPE);
        }

        public AtlasCoordinate GetCellTextureCoordinateSync(CellType cellType, int globalX, int globalY)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var variation = CalculateVariation(textureInfo, globalX, globalY);

                int frameIndex = 0;
                int frameHeight = 0;

                if (textureInfo.AnimationFrames > 1)
                {
                    float speed = textureInfo.ContainerFPS > 0 ? textureInfo.ContainerFPS : MapManager.Instance.GetAnimationSpeed(cellType);
                    if (speed == 0)
                    {
                        speed = 5;
                    }

                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = textureInfo.ContainerFPS > 0 ? textureInfo.FrameSize : MapManager.Instance.GetAnimationFrameHeight(cellType);
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

        public Vector4 GetCellFrameRect(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                var atlas = GetAtlasForCell(cellType);
                if (atlas != null)
                {
                    AtlasCoordinate baseCoord = atlas.GetCoordinate(cellType);
                    float atlasSize = atlas.Size;
                    int frameHeight = textureInfo.FrameSize;
                    return new Vector4(
                        (float)baseCoord.AtlasX / atlasSize,
                        (float)baseCoord.AtlasY / atlasSize,
                        (float)baseCoord.Width / atlasSize,
                        (float)frameHeight / atlasSize);
                }
            }

            return Vector4.zero;
        }

        public int GetAnimationFrameCount(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache.TryGetTexture(cellType, out var info) ? info.AnimationFrames : 1;
        }

        public float GetAnimationSpeedForCell(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var info))
            {
                if (info.ContainerFPS > 0)
                {
                    return info.ContainerFPS;
                }

                byte speed = MapManager.Instance.GetAnimationSpeed(cellType);
                return speed > 0 ? speed : 5f;
            }

            return 5f;
        }

        public int GetFrameSize(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache.TryGetTexture(cellType, out var info) ? info.FrameSize : 0;
        }

        public async UniTask<AtlasCoordinate> GetCellTextureCoordinate(CellType cellType, int globalX, int globalY)
        {
            await UniTask.SwitchToMainThread();
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return GetCellTextureCoordinateSync(cellType, globalX, globalY);
            }

            if (_pendingRequests.TryGetValue(cellType, out var existingRequest))
            {
                await existingRequest.Task;
                await UniTask.SwitchToMainThread();
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
                await UniTask.SwitchToMainThread();
                request.SetResult(true);

                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
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
            var filename = $"Cells/{(int)cellType}";

            if (cellType == CellType.Empty)
            {
                filename = "Cells/32";
            }

            var cachedTexture = _textureCache.GetCachedTexture(cellType);
            if (cachedTexture != null)
            {
                AddTextureToAtlas(cellType, cachedTexture);
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

                await UniTask.SwitchToMainThread();
                AddTextureToAtlas(cellType, texture);
            }
            else
            {
                throw new Exception($"Failed to load texture for {cellType}");
            }
        }

        private void AddTextureToAtlas(CellType cellType, Texture2D texture)
        {
            int frameHeight = MapManager.Instance.GetAnimationFrameHeight(cellType);
            float containerFPS = 0;

            if (texture.name.Contains("|"))
            {
                string[] parts = texture.name.Split('|');
                foreach (string part in parts)
                {
                    if (part.StartsWith("FPS="))
                    {
                        float.TryParse(part.Substring(4), out containerFPS);
                    }
                    else if (part.StartsWith("FrameHeight="))
                    {
                        int.TryParse(part.Substring(12), out frameHeight);
                    }
                }
            }

            int effectiveFrameHeight = frameHeight > 0 ? frameHeight : texture.height;

            var textureInfo = new CellTextureInfo
            {
                CellType = cellType,
                BaseTexture = texture,
                HasVariations = texture.width > _cellTextureSize || effectiveFrameHeight > _cellTextureSize,
                VariationCount = 1,
                AnimationFrames = frameHeight > 0 ? texture.height / frameHeight : 1,
                FramesPerRow = 1,
                FrameSize = effectiveFrameHeight,
                ContainerFPS = containerFPS,
            };

            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(newSize, _cellTextureSize, _texturePadding);
                    _atlases.Add(newAtlas);
                    _currentAtlas = newAtlas;
                    EnsureFlowMapInAtlas(_currentAtlas);

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

            _currentAtlas.CopyTextureToAtlas(cellType, texture);
            _textureCache.AddTexture(cellType, textureInfo);
            OnTextureLoaded?.Invoke($"Cells/{(int)cellType}.png", texture);
        }

        private static CellVariation CalculateVariation(CellTextureInfo textureInfo, int globalX, int globalY)
        {
            if (!textureInfo.HasVariations)
            {
                return CellVariation.None;
            }

            int variationX = ((globalX % 2) + 2) % 2;
            int variationY = ((globalY % 2) + 2) % 2;

            return new CellVariation
            {
                Horizontal = variationX == 1,
                Vertical = variationY == 1,
            };
        }

        public List<TextureAtlas> GetAllAtlases()
        {
            EnsureInitialized();
            return _atlases;
        }

        public void FlushDirtyAtlases()
        {
            for (int i = 0; i < _atlases.Count; i++)
            {
                if (_atlases[i].IsDirty)
                {
                    _atlases[i].SyncApply();
                }
            }
        }

        public TextureAtlas GetAtlasForCell(CellType cellType)
        {
            EnsureInitialized();
            foreach (var atlas in _atlases)
            {
                if (atlas.ContainsCell(cellType))
                {
                    return atlas;
                }
            }

            return null;
        }

        public void Clear()
        {
            EnsureInitialized();
            _textureCache.Clear();
            foreach (var atlas in _atlases)
            {
                atlas.Clear();
            }

            _atlases.Clear();
            _currentAtlas = new TextureAtlas(_initialAtlasSize, _cellTextureSize, _texturePadding);
            _atlases.Add(_currentAtlas);
            GenerateFlowMap();
            EnsureFlowMapInAtlas(_currentAtlas);
            _cachedEmptyTexture = null;
        }

        private void CreateFallbackTexture(CellType cellType, int globalX, int globalY)
        {
            try
            {
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
                    FrameSize = _cellTextureSize,
                };

                if (_currentAtlas.TryAddTexture(cellType, fallbackTexture, out var coordinate))
                {
                    _currentAtlas.CopyTextureToAtlas(cellType, fallbackTexture);
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
            var backgroundColors = new List<Color>();
            const int radius = 2;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    CellType neighborType = GetCellTypeAt(globalX + dx, globalY + dy);

                    if (neighborType == CellType.Empty || neighborType == CellType.Road)
                    {
                        Color cellColor = GetCellBackgroundColor(neighborType);

                        int weight = Mathf.Max(0, radius - Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) + 1);
                        for (int w = 0; w < weight; w++)
                        {
                            backgroundColors.Add(cellColor);
                        }
                    }
                }
            }

            if (backgroundColors.Count > 0)
            {
                return GetMostFrequentColor(backgroundColors);
            }

            return GetCellBackgroundColor(CellType.Empty);
        }

        private static CellType GetCellTypeAt(int x, int y)
        {
            if (ServiceLocator.Resolve<IWorldDataStorage>() != null && ServiceLocator.Resolve<IWorldDataStorage>().CellLayer != null)
            {
                try
                {
                    return ServiceLocator.Resolve<IWorldDataStorage>().GetCell(x, y);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"GetCellTypeAt error: {ex.Message}");
                }
            }

            return CellType.Empty;
        }

        private static Color GetCellBackgroundColor(CellType cellType)
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

        private static Color GetMostFrequentColor(List<Color> colors)
        {
            if (colors.Count == 0)
            {
                return new Color(0.2f, 0.2f, 0.2f);
            }

            var colorGroups = new Dictionary<int, List<Color>>();
            const float tolerance = 0.1f;

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

        private static Color GetFallbackColor(CellType cellType)
        {
            if (MapManager.Instance != null)
            {
                var serverColor = MapManager.Instance.GetCellMinimapColor(cellType);
                if (serverColor.a > 0)
                {
                    return serverColor;
                }
            }

            return cellType switch
            {
                CellType.Empty => new Color(0.2f, 0.2f, 0.2f),
                CellType.Road => new Color(0.8f, 0.8f, 0.8f),
                CellType.Boulder1 => Color.black,
                CellType.WhiteSand => new Color(1f, 0.92f, 0.8f),
                CellType.GrayAcid => new Color(0f, 1f, 0f),
                _ => Color.magenta,
            };
        }

        public Texture2D GetCachedTexture(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache.GetCachedTexture(cellType);
        }

        public string GetCacheStats()
        {
            EnsureInitialized();
            return _textureCache.GetCacheStats();
        }

        public Texture2D GetEmptyTexture()
        {
            EnsureInitialized();
            return _cachedEmptyTexture;
        }

        public class TextureRequest
        {
            private readonly UniTaskCompletionSource<bool> _taskSource;

            public TextureRequest(CellType cellType)
            {
                CellType = cellType;
                _taskSource = new UniTaskCompletionSource<bool>();
            }

            public CellType CellType { get; }

            public UniTask<bool> Task => _taskSource.Task;

            public void SetResult(bool success)
            {
                _taskSource.TrySetResult(success);
            }
        }
    }
}
