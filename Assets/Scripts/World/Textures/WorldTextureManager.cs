#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Terrain;
using Kern.World.Textures;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Kern.World
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

        private WorldAtlasCollection _atlasCollection = null!;

        public TextureAtlas _currentAtlas => _atlasCollection.CurrentAtlas;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private ITextureStorageService _textureStorage = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        private CellTextureCache _textureCache = null!;
        private readonly WorldTextureAuxiliaryAssets _auxiliaryAssets = new();
        public Texture2D? PrismaticFlowMapTexture => _auxiliaryAssets.PrismaticFlowMapTexture;
        public Texture2D? FlowMapTexture => _auxiliaryAssets.FlowMapTexture;
        public Texture2D? TerrainDecalAtlasTexture => _auxiliaryAssets.TerrainDecalAtlasTexture;
        public Texture2D? TerrainDecalStoneAtlasTexture => _auxiliaryAssets.TerrainDecalStoneAtlasTexture;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = null!;
        private readonly CellTextureRetryTracker _retryTracker = new();
        private readonly SemaphoreSlim _textureLoadSlots = new(
            ProjectRuntimeContracts.AssetStreaming.MaximumConcurrentTextureLoads);

        public int PendingCellTextureRequests => _retryTracker.PendingRequestsCount;

        private Texture2D? _cachedEmptyTexture;

        public uint TextureRevision { get; private set; }

        protected void OnDestroy()
        {
            _textureCache?.Clear();
            _atlasCollection?.Dispose();

            _auxiliaryAssets.Dispose();
        }

        private void Initialize()
        {
            if (_textureCache != null && _atlasCollection != null && _pendingRequests != null)
            {
                return;
            }

            _textureCache = new CellTextureCache();
            _atlasCollection = new WorldAtlasCollection(
                _initialAtlasSize,
                _maxAtlasSize,
                _cellTextureSize,
                _texturePadding,
                GetCachedTexture);
            _pendingRequests = new ConcurrentDictionary<CellType, TextureRequest>();

            _auxiliaryAssets.Initialize(_textureStorage, _operations, RaiseTextureLoaded);
        }

        private void EnsureInitialized()
        {
            if (_textureCache == null || _atlasCollection == null || _pendingRequests == null)
            {
                Initialize();
            }
        }

        public event Action<string, Texture2D>? OnTextureLoaded;

        private void RaiseTextureLoaded(string name, Texture2D texture) => OnTextureLoaded?.Invoke(name, texture);

        public void RequestTexture(CellType cellType)
        {
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out _) ||
                _pendingRequests.ContainsKey(cellType) ||
                _retryTracker.ShouldThrottle(cellType))
            {
                return;
            }

            // Загрузка стартует в следующем Update, а не внутри вызывающего.
            // RequestTexture зовут посреди заполнения кэша террейна, и
            // текстура из памяти успевала дойти до атласа синхронно: её
            // декодирование ложилось в кадр сборки, а OnTextureLoaded менял
            // набор типов, по которому сборка в этот момент шла.
            _operations.Run(
                $"load_world_texture_{cellType}",
                cancellationToken => _retryTracker.RunTrackedRequestAsync(
                    cellType,
                    async (type, ct) =>
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        await GetCellTextureCoordinate(type, 0, 0, ct);
                    },
                    cancellationToken));
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

                return _atlasCollection.GetWrappedCoordinate(
                    cellType,
                    globalX,
                    globalY,
                    variation,
                    frameHeight,
                    frameIndex);
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

        public UniTask<AtlasCoordinate> GetCellTextureCoordinate(
            CellType cellType,
            int globalX,
            int globalY) =>
            GetCellTextureCoordinate(cellType, globalX, globalY, destroyCancellationToken);

        private async UniTask<AtlasCoordinate> GetCellTextureCoordinate(
            CellType cellType,
            int globalX,
            int globalY,
            CancellationToken cancellationToken)
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
                await _textureLoadSlots.WaitAsync(cancellationToken);
                try
                {
                    await LoadTexture(cellType);
                }
                finally
                {
                    _textureLoadSlots.Release();
                }
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
                if (!_atlasCollection.ContainsCell(cellType))
                {
                    // Атлас и данные декодированной текстуры принадлежат
                    // главному потоку Unity.
                    await UniTask.SwitchToMainThread();
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

            // Missing server data must stay missing. A generated diagnostic image
            // must never invent a colour. The map configuration is authoritative
            // for the visual identity of the cell, so it is the only permitted
            // source for this explicit degraded rendering path.
            Debug.LogError($"[AssetDiag] TEXFAIL {filename} — using map colour fallback");
            Color mapColor = _mapManager.GetCellMinimapColor(cellType);
            await UniTask.SwitchToMainThread();
            texture = WorldTextureGenerator.CreateMapColorCellTexture(
                cellType,
                _cellTextureSize,
                mapColor);
            AddTextureToAtlas(cellType, texture, ownsTexture: true);
        }

        private static readonly Unity.Profiling.ProfilerMarker _AtlasAddMarker = new("Kern.Textures.AtlasAdd");

        private void AddTextureToAtlas(
            CellType cellType,
            Texture2D texture,
            bool ownsTexture)
        {
            using var atlasAddMarker = _AtlasAddMarker.Auto();
            if (_atlasCollection.ContainsCell(cellType) || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            int frameHeight = _mapManager.GetAnimationFrameHeight(cellType);

            _atlasCollection.ValidateDimensions(
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

            // Непрозрачность меряется один раз при загрузке: по ней террейн
            // не рисует фон под сплошными блоками.
            _atlasCollection.AddTexture(cellType, texture, TextureAtlas.MeasureFullyOpaque(texture));
            _textureCache.AddTexture(cellType, textureInfo);
            TextureRevision++;
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

        public IReadOnlyList<IAtlasDescriptor> GetAllAtlases()
        {
            EnsureInitialized();
            return _atlasCollection.GetAllAtlases();
        }

        public void FlushDirtyAtlases()
        {
            _atlasCollection?.FlushDirtyAtlases();
        }

        public TextureAtlas? GetAtlasForCell(CellType cellType)
        {
            EnsureInitialized();
            return _atlasCollection.GetAtlasForCell(cellType);
        }

        public void Clear()
        {
            EnsureInitialized();
            _textureCache.Clear();
            _atlasCollection.Reset();
            _auxiliaryAssets.RegenerateFlowMap();
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
    }
}
