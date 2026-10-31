#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Terrain;
using Kern.World.Textures;
using MinesServer.Data;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;
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

        // Скорость покадровой анимации клетки, когда серверный конфиг её не
        // задал, а текстура — GIF-лента кадров (например, вращение бокса).

        private WorldAtlasCollection _atlasCollection = null!;
        private IReadOnlyDictionary<string, byte[]>? _worldAssetBytes;

        public TextureAtlas _currentAtlas => _atlasCollection.CurrentAtlas;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private IPersistentAssetCache _persistentAssetCache = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        private CellTextureCache _textureCache = null!;
        private readonly WorldTextureAuxiliaryAssets _auxiliaryAssets = new();
        private bool _isWorldAssetsReady;
        private int _worldAssetLoadGeneration;
        public Texture2D? PrismaticFlowMapTexture => _auxiliaryAssets.PrismaticFlowMapTexture;
        public Texture2D? FlowMapTexture => _auxiliaryAssets.FlowMapTexture;
        public Texture2DArray? TerrainDecalAtlasArray => _auxiliaryAssets.TerrainDecalAtlasArray;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = null!;
        private readonly CellTextureRetryTracker _retryTracker = new();
        private readonly SemaphoreSlim _textureLoadSlots = new(
            AssetStreamingLimits.MaximumConcurrentCellTextureLoads);
        private CancellationTokenSource _worldAssetsLifetime = new();

        public int PendingCellTextureRequests => _retryTracker.PendingRequestsCount;
        public bool IsWorldAssetsReady => _isWorldAssetsReady;

        public uint TextureRevision { get; private set; }

        protected void OnDestroy()
        {
            _worldAssetsLifetime.Cancel();
            _worldAssetsLifetime.Dispose();
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

            _auxiliaryAssets.Initialize();
        }

        private void EnsureInitialized()
        {
            if (_textureCache == null || _atlasCollection == null || _pendingRequests == null)
            {
                Initialize();
            }
        }

        public event Action<CellType, Texture2D>? OnTextureLoaded;
        public event Action? OnTerrainDecalAtlasLoaded;

        private void RaiseTerrainDecalAtlasLoaded() =>
            OnTerrainDecalAtlasLoaded?.Invoke();

        public void LoadWorldAssets(
            IReadOnlyList<WorldAssetDefinitionPacket> assetManifest)
        {
            EnsureInitialized();
            ResetWorldAssets();
            int generation = checked(++_worldAssetLoadGeneration);
            CancellationToken worldLifetime = _worldAssetsLifetime.Token;
            WorldAssetDefinitionPacket[] manifestSnapshot = new List<WorldAssetDefinitionPacket>(assetManifest).ToArray();
            _operations.Run(
                "synchronize_world_asset_manifest",
                async cancellationToken =>
                {
                    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        worldLifetime);
                    var synchronizer = new AssetManifestSynchronizer(
                        _persistentAssetCache,
                        _assetLoader);
                    IReadOnlyDictionary<string, byte[]> worldAssetBytes =
                        await synchronizer.SynchronizeAsync(manifestSnapshot, linkedCancellation.Token);
                    if (generation != _worldAssetLoadGeneration)
                    {
                        return;
                    }

                    _worldAssetBytes = worldAssetBytes;
                    _auxiliaryAssets.LoadDecals(
                        worldAssetBytes,
                        _operations,
                        () =>
                        {
                            if (generation == _worldAssetLoadGeneration)
                            {
                                _isWorldAssetsReady = true;
                                RaiseTerrainDecalAtlasLoaded();
                            }
                        });
                });
        }

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
            CancellationToken worldLifetime = _worldAssetsLifetime.Token;
            _operations.Run(
                $"load_world_texture_{cellType}",
                async cancellationToken =>
                {
                    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        worldLifetime);
                    await _retryTracker.RunTrackedRequestAsync(
                        cellType,
                        async (type, ct) =>
                        {
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                            await GetCellTextureCoordinate(type, 0, 0, ct);
                        },
                        linkedCancellation.Token);
                });
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
                    // Скорость — frameAnimationSpeed из cells.json.
                    float speed = BlockRegistry.Get(cellType).FrameAnimationSpeed;
                    frameIndex = (int)(Time.realtimeSinceStartup * speed) % textureInfo.AnimationFrames;
                    frameHeight = textureInfo.FrameSize;
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
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _worldAssetsLifetime.Token);
            cancellationToken = linkedCancellation.Token;
            await UniTask.SwitchToMainThread(cancellationToken);
            EnsureInitialized();
            if (_textureCache.TryGetTexture(cellType, out var textureInfo))
            {
                return GetCellTextureCoordinateSync(cellType, globalX, globalY);
            }

            if (_pendingRequests.TryGetValue(cellType, out var existingRequest))
            {
                await existingRequest.Task.AttachExternalCancellation(cancellationToken);
                await UniTask.SwitchToMainThread(cancellationToken);
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
                    await racingRequest.Task.AttachExternalCancellation(cancellationToken);
                }

                await UniTask.SwitchToMainThread(cancellationToken);
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
                    await LoadTexture(cellType, cancellationToken);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                request.SetResult(false);
                throw;
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

        private async UniTask LoadTexture(CellType cellType, CancellationToken cancellationToken)
        {
            string filename = _mapManager.GetCellConfig(cellType).TextureName;

            if (_textureCache.TryGetTexture(cellType, out CellTextureInfo cachedTextureInfo))
            {
                Texture2D cachedTexture = cachedTextureInfo.BaseTexture;
                if (!_atlasCollection.ContainsCell(cellType))
                {
                    // Атлас и данные декодированной текстуры принадлежат
                    // главному потоку Unity.
                    await UniTask.SwitchToMainThread(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
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
                if (_mapManager.TryGetAssetDefinition(filename, out WorldAssetDefinitionPacket definition))
                {
                    IReadOnlyDictionary<string, byte[]>? worldAssetBytes = _worldAssetBytes;
                    byte[]? bytes;
                    if (worldAssetBytes != null)
                    {
                        if (!worldAssetBytes.TryGetValue(definition.Filename, out bytes))
                        {
                            throw new InvalidDataException(
                                $"Cell texture '{definition.Filename}' is absent from the loaded world asset manifest.");
                        }
                    }
                    else
                    {
                        bytes = await _assetLoader.GetAssetBytesAsync(
                            definition.Filename,
                            definition.ETag,
                            cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (bytes != null && bytes.Length > 0)
                    {
                        string? actualETag = ETagCalculator.Calculate(bytes);
                        if (!string.IsNullOrEmpty(definition.ETag) &&
                            !string.Equals(actualETag, definition.ETag, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Texture '{definition.Filename}' content hash does not match the world manifest.");
                        }

                        await UniTask.SwitchToMainThread(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        texture = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                            bytes,
                            definition.Filename,
                            RuntimeTextureColorSpace.Srgb,
                            FilterMode.Point,
                            TextureWrapMode.Clamp,
                            makeNoLongerReadable: true);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                Debug.LogWarning($"[WorldTextureManager] Warning loading {filename}: {ex.Message}");
            }

            if (texture != null)
            {
                await UniTask.SwitchToMainThread(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                AddTextureToAtlas(cellType, texture, ownsTexture: true);
                return;
            }

            // Missing server data must stay missing. A generated diagnostic image
            // must never invent a colour. The map configuration is authoritative
            // for the visual identity of the cell, so it is the only permitted
            // source for this explicit degraded rendering path.
            Debug.LogError($"[AssetDiag] TEXFAIL {filename} — using map colour fallback");
            Color mapColor = _mapManager.GetCellMinimapColor(cellType);
            await UniTask.SwitchToMainThread(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            texture = WorldTextureGenerator.CreateMapColorCellTexture(
                cellType,
                _cellTextureSize,
                mapColor);
            AddTextureToAtlas(cellType, texture, ownsTexture: true);
        }

        private static readonly Unity.Profiling.ProfilerMarker s_atlasAddMarker = new("Kern.Textures.AtlasAdd");

        private void AddTextureToAtlas(
            CellType cellType,
            Texture2D texture,
            bool ownsTexture)
        {
            using var atlasAddMarker = s_atlasAddMarker.Auto();
            if (_atlasCollection.ContainsCell(cellType) || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            int frameHeight = 0;
            if (texture.width == _cellTextureSize &&
                texture.height > _cellTextureSize &&
                texture.height % _cellTextureSize == 0)
            {
                frameHeight = _cellTextureSize;
            }
            else if (cellType == CellType.Unloaded &&
                texture.height > _cellTextureSize &&
                texture.height % _cellTextureSize == 0)
            {
                frameHeight = _cellTextureSize;
            }

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
            OnTextureLoaded?.Invoke(cellType, texture);
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
            ResetWorldAssets();
            _auxiliaryAssets.RegenerateFlowMap();
        }

        private void ResetWorldAssets()
        {
            CancellationTokenSource previousLifetime = _worldAssetsLifetime;
            _worldAssetsLifetime = new CancellationTokenSource();
            previousLifetime.Cancel();
            previousLifetime.Dispose();
            _worldAssetLoadGeneration = checked(_worldAssetLoadGeneration + 1);
            _isWorldAssetsReady = false;
            _worldAssetBytes = null;
            _retryTracker.ResetFailures();
            _textureCache.Clear();
            _atlasCollection.Reset();
            _auxiliaryAssets.ClearDecals();
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
