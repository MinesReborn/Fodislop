#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.World.Terrain;
using Fodinae.World.Textures;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Fodinae.World
{
    public class WorldTextureManager : MonoBehaviour, ITextureService
    {
        [Header("Atlas Configuration")]
        [SerializeField]
        private int _initialAtlasSize = 2048;
        [SerializeField]
        private int _maxAtlasSize = 4096;
        [SerializeField]
        private int _texturePadding = 2;

        [Header("Performance")]
        [SerializeField]
        private int _cellTextureSize = RenderingConstants.CELL_SIZE;

        [System.NonSerialized]
        public TextureAtlas _currentAtlas = null!;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        private CellTextureCache _textureCache = null!;
        private Texture2D? _flowMapTexture;
        public Texture2D? FlowMapTexture => _flowMapTexture;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = null!;
        private List<TextureAtlas> _atlases = null!;

        private readonly ConcurrentDictionary<CellType, byte> _inFlightCellTypeRequests = new();
        public int PendingCellTextureRequests => _inFlightCellTypeRequests.Count;

        private readonly ConcurrentDictionary<CellType, double> _cellTextureRetryTimes = new();
        private static readonly System.Diagnostics.Stopwatch RetryClock =
            System.Diagnostics.Stopwatch.StartNew();
        private const double FailedCellTextureRetrySeconds = 30.0;

        private Texture2D? _cachedEmptyTexture;

        public uint TextureRevision { get; private set; }

        protected void OnDestroy()
        {
            _textureCache?.Clear();
            if (_atlases != null)
            {
                foreach (var atlas in _atlases)
                {
                    atlas?.Dispose();
                }

                _atlases.Clear();
            }

            if (_flowMapTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_flowMapTexture);
                }
                else
                {
                    DestroyImmediate(_flowMapTexture);
                }

                _flowMapTexture = null;
            }
        }

        private void Initialize()
        {
            if (_textureCache != null && _atlases != null && _pendingRequests != null)
            {
                return;
            }

            _textureCache = new CellTextureCache();
            _currentAtlas = new TextureAtlas(
                _initialAtlasSize,
                _cellTextureSize,
                _texturePadding,
                GetCachedTexture);

            _atlases = new List<TextureAtlas>();
            _atlases.Add(_currentAtlas);

            _pendingRequests = new ConcurrentDictionary<CellType, TextureRequest>();

            GenerateFlowMap();
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
            if (_flowMapTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_flowMapTexture);
                }
                else
                {
                    DestroyImmediate(_flowMapTexture);
                }
            }

            _flowMapTexture = WorldTextureGenerator.CreateFlowMap();
        }

        public event Action<string, Texture2D>? OnTextureLoaded;

        public void RequestTexture(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out _) ||
                _pendingRequests.ContainsKey(cellType))
            {
                return;
            }

            if (_cellTextureRetryTimes.TryGetValue(cellType, out double retryAfterSeconds) &&
                RetryClock.Elapsed.TotalSeconds < retryAfterSeconds)
            {
                return;
            }

            if (!_inFlightCellTypeRequests.TryAdd(cellType, 0))
            {
                return;
            }

            _operations.Run(
                $"load_world_texture_{cellType}",
                cancellationToken => TrackedRequestTextureAsync(cellType, cancellationToken));
        }

        private async UniTask TrackedRequestTextureAsync(
            CellType cellType,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await GetCellTextureCoordinate(cellType, 0, 0);
                cancellationToken.ThrowIfCancellationRequested();
                _cellTextureRetryTimes.TryRemove(cellType, out _);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                bool firstFailure = !_cellTextureRetryTimes.ContainsKey(cellType);
                _cellTextureRetryTimes[cellType] =
                    RetryClock.Elapsed.TotalSeconds + FailedCellTextureRetrySeconds;

                if (firstFailure)
                {
                    Debug.LogWarning(
                        $"[WorldTextureManager] Texture for cell type {cellType} could not be " +
                        $"loaded: {exception.Message}. Retrying at most every " +
                        $"{FailedCellTextureRetrySeconds:F0}s.");
                }
            }
            finally
            {
                _inFlightCellTypeRequests.TryRemove(cellType, out _);
            }
        }

        public AtlasCoordinate GetCellTextureCoordinate(CellType cellType)
        {
            EnsureInitialized();
            return GetCellTextureCoordinateSync(cellType, 0, 0);
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
                    float speed = _mapManager.GetAnimationSpeed(cellType);

                    if (speed <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Server animation speed for cell type {cellType} must be greater than zero.");
                    }

                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = _mapManager.GetAnimationFrameHeight(cellType);
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
            MapManager mapManager = _mapManager;
            if (!mapManager.HasAnimation(cellType))
            {
                return 0f;
            }

            byte serverSpeed = mapManager.GetAnimationSpeed(cellType);
            if (serverSpeed == 0)
            {
                throw new InvalidDataException(
                    $"Server animation speed for cell type {cellType} must be greater than zero.");
            }

            return serverSpeed;
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
            bool ownsRequest = _pendingRequests.TryAdd(cellType, request);
            if (!ownsRequest)
            {
                if (_pendingRequests.TryGetValue(cellType, out var racingRequest))
                {
                    await racingRequest.Task;
                }

                await UniTask.SwitchToMainThread();
                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }

                throw new InvalidOperationException($"Failed to load texture for cell type {cellType} (joined racing request).");
            }

            try
            {
                await LoadTexture(cellType);
                await UniTask.SwitchToMainThread();
                request.SetResult(true);

                if (_textureCache.TryGetTexture(cellType, out textureInfo))
                {
                    return GetCellTextureCoordinateSync(cellType, globalX, globalY);
                }

                throw new InvalidOperationException($"Failed to load texture for cell type {cellType}: texture is not cached after load");
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                request.SetResult(false);
                throw new InvalidOperationException($"Failed to load texture for cell type {cellType}: {ex.Message}", ex);
            }
            finally
            {
                if (ownsRequest)
                {
                    _pendingRequests.TryRemove(cellType, out _);
                }
            }
        }

        private async UniTask LoadTexture(CellType cellType)
        {
            var filename = $"Cells/{(int)cellType}";

            if (cellType == CellType.Empty)
            {
                filename = "Cells/32";
            }

            if (_textureCache.TryGetTexture(cellType, out CellTextureInfo cachedTextureInfo))
            {
                Texture2D cachedTexture = cachedTextureInfo.BaseTexture;
                bool alreadyInAtlas = false;
                foreach (var atlas in _atlases)
                {
                    if (atlas.ContainsCell(cellType))
                    {
                        alreadyInAtlas = true;
                        break;
                    }
                }

                if (!alreadyInAtlas)
                {
                    AddTextureToAtlas(
                        cellType,
                        cachedTexture,
                        cachedTextureInfo.OwnsBaseTexture);
                }

                return;
            }

            Texture2D? texture = null;
            try
            {
                texture = await _assetLoader.GetTextureAsync(filename);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldTextureManager] Warning loading {filename}: {ex.Message}");
            }

            if (texture != null)
            {
                if (cellType == CellType.Empty)
                {
                    _cachedEmptyTexture = texture;
                }

                await UniTask.SwitchToMainThread();
                AddTextureToAtlas(cellType, texture, ownsTexture: false);
                return;
            }

            Debug.LogWarning(
                $"[AssetDiag] TEXFAIL {filename} — using deterministic random diagnostic texture");
            await UniTask.SwitchToMainThread();
            texture = WorldTextureGenerator.CreateMissingCellTexture(cellType, _cellTextureSize);
            AddTextureToAtlas(cellType, texture, ownsTexture: true);
        }

        private void AddTextureToAtlas(
            CellType cellType,
            Texture2D texture,
            bool ownsTexture)
        {
            foreach (var atlas in _atlases)
            {
                if (atlas.ContainsCell(cellType))
                {
                    return;
                }
            }

            if (!_mapManager.IsWorldInitialized)
            {
                return;
            }

            int frameHeight = _mapManager.GetAnimationFrameHeight(cellType);

            ValidateTerrainTextureDimensions(
                cellType,
                texture,
                frameHeight);
            bool hasFrameAtlas = frameHeight > 0;
            int effectiveFrameHeight = hasFrameAtlas
                ? frameHeight
                : texture.height;

            var textureInfo = new CellTextureInfo
            {
                CellType = cellType,
                BaseTexture = texture,
                OwnsBaseTexture = ownsTexture,
                HasVariations = texture.width > _cellTextureSize || effectiveFrameHeight > _cellTextureSize,
                VariationCount = 1,
                AnimationFrames = hasFrameAtlas
                    ? texture.height / frameHeight
                    : 1,
                FramesPerRow = 1,
                FrameSize = effectiveFrameHeight,
            };

            if (_currentAtlas == null)
            {
                throw new InvalidOperationException(
                    "WorldTextureManager atlas is not initialized before adding a terrain texture.");
            }

            if (!_currentAtlas.TryAddTexture(cellType, texture, out var coordinate))
            {
                var newSize = Mathf.Min(_currentAtlas.Size * 2, _maxAtlasSize);
                if (newSize > _currentAtlas.Size)
                {
                    var newAtlas = new TextureAtlas(
                        newSize,
                        _cellTextureSize,
                        _texturePadding,
                        GetCachedTexture);
                    _atlases.Add(newAtlas);
                    _currentAtlas = newAtlas;

                    if (!_currentAtlas.TryAddTexture(cellType, texture, out coordinate))
                    {
                        throw new InvalidOperationException(
                            $"Failed to add terrain texture for cell type {cellType} to new atlas of size {newSize}.");
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Terrain texture atlas size limit reached ({_maxAtlasSize}) while adding cell type {cellType}.");
                }
            }

            _currentAtlas.CopyTextureToAtlas(cellType, texture);
            _textureCache.AddTexture(cellType, textureInfo);
            TextureRevision++;
            OnTextureLoaded?.Invoke($"Cells/{(int)cellType}.png", texture);
        }

        private void ValidateTerrainTextureDimensions(
            CellType cellType,
            Texture2D texture,
            int frameHeight)
        {
            if (texture.width <= 0 || texture.height <= 0 ||
                texture.width % _cellTextureSize != 0 ||
                texture.height % _cellTextureSize != 0)
            {
                throw new InvalidDataException(
                    $"Terrain texture for {cellType} has invalid dimensions " +
                    $"{texture.width}x{texture.height}; both dimensions must be positive " +
                    $"multiples of {_cellTextureSize} pixels.");
            }

            if (frameHeight == 0)
            {
                return;
            }

            if (frameHeight % _cellTextureSize != 0 || texture.height % frameHeight != 0)
            {
                throw new InvalidDataException(
                    $"Terrain texture for {cellType} has height {texture.height} and " +
                    $"frame height {frameHeight}; both must align to " +
                    $"{_cellTextureSize}-pixel cells and frames must divide the atlas exactly.");
            }
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

        private readonly List<IAtlasDescriptor> _atlasDescriptorsCache = new();

        public IReadOnlyList<IAtlasDescriptor> GetAllAtlases()
        {
            EnsureInitialized();
            if (_atlasDescriptorsCache.Count != _atlases.Count)
            {
                _atlasDescriptorsCache.Clear();
                for (int i = 0; i < _atlases.Count; i++)
                {
                    _atlasDescriptorsCache.Add(_atlases[i]);
                }
            }

            return _atlasDescriptorsCache;
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

        public TextureAtlas? GetAtlasForCell(CellType cellType)
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
                atlas.Dispose();
            }

            _atlases.Clear();
            _currentAtlas = new TextureAtlas(
                _initialAtlasSize,
                _cellTextureSize,
                _texturePadding,
                GetCachedTexture);
            _atlases.Add(_currentAtlas);
            GenerateFlowMap();
            _cachedEmptyTexture = null;
            TextureRevision++;
        }

        public Texture2D? GetCachedTexture(CellType cellType)
        {
            EnsureInitialized();
            return _textureCache?.GetCachedTexture(cellType);
        }

        public string GetCacheStats()
        {
            EnsureInitialized();
            return _textureCache != null ? _textureCache.GetCacheStats() : string.Empty;
        }

        public Texture2D? GetEmptyTexture()
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
