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
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private IConnectionService _connection = null!;
        private CellTextureCache _textureCache = null!;
        private readonly WorldTextureAuxiliaryAssets _auxiliaryAssets = new();
        private bool _isWorldAssetsReady;
        private UniTaskCompletionSource? _worldAssetsSync;
        private int _worldAssetLoadGeneration;
        public Texture2D? PrismaticFlowMapTexture => _auxiliaryAssets.PrismaticFlowMapTexture;
        public Texture2D? FlowMapTexture => _auxiliaryAssets.FlowMapTexture;
        public Texture2DArray? TerrainDecalAtlasArray => _auxiliaryAssets.TerrainDecalAtlasArray;
        private ConcurrentDictionary<CellType, TextureRequest> _pendingRequests = null!;
        private readonly CellTextureRetryTracker _retryTracker = new();
        private readonly SemaphoreSlim _textureLoadSlots = new(
            AssetStreamingLimits.MaximumConcurrentCellTextureLoads);
        private CancellationTokenSource _worldAssetsLifetime = new();

        // Откуда взята текстура каждого типа (файл и его ETag). Тип без записи — запасная цветная текстура. По этому
        // списку обновление мира посреди игры находит, какие текстуры действительно изменились
        private readonly Dictionary<CellType, (string Filename, string ETag)> _textureSources = new();
        private int _contentUpdateSerial;

        // ETag каждого файла в последнем применённом манифесте: по нему видно, какие файлы сервер заменил
        private Dictionary<string, string> _appliedManifestETags = new(StringComparer.Ordinal);

        // Прежний атлас живёт ещё немного после подмены: террейн перепривязывает атласы при следующей публикации,
        // а до неё на экране старая сетка, и она должна рисоваться со своей текстурой, а не с уничтоженной
        private const int RetiredAtlasLifetimeMilliseconds = 5000;

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
            _atlasCollection = CreateAtlasCollection();
            _pendingRequests = new ConcurrentDictionary<CellType, TextureRequest>();

            _auxiliaryAssets.Initialize();
        }

        private WorldAtlasCollection CreateAtlasCollection() =>
            new(
                _initialAtlasSize,
                _maxAtlasSize,
                _cellTextureSize,
                _texturePadding,
                GetCachedTexture);

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
            IReadOnlyList<AssetsDefinitionPacket> assetManifest)
        {
            EnsureInitialized();
            ResetWorldAssets();
            int generation = checked(++_worldAssetLoadGeneration);
            CancellationToken worldLifetime = _worldAssetsLifetime.Token;
            AssetsDefinitionPacket[] manifestSnapshot = new List<AssetsDefinitionPacket>(assetManifest).ToArray();
            _appliedManifestETags = CreateETagMap(manifestSnapshot);
            // Завершается при любом исходе синхронизации (и сбое тоже), чтобы ждущие текстуры не зависли
            var sync = new UniTaskCompletionSource();
            _worldAssetsSync = sync;
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
                    IReadOnlyDictionary<string, byte[]> worldAssetBytes;
                    try
                    {
                        worldAssetBytes = await synchronizer.SynchronizeAsync(
                            manifestSnapshot,
                            linkedCancellation.Token,
                            (done, total) =>
                            {
                                if (generation == _worldAssetLoadGeneration)
                                {
                                    _loadProgress?.ReportAssetBytes(done, total);
                                }
                            },
                            () => _connection?.ReceivedBytes ?? 0,
                            (checkedFiles, totalFiles) =>
                            {
                                if (generation == _worldAssetLoadGeneration)
                                {
                                    _loadProgress?.ReportAssetCheck(checkedFiles, totalFiles);
                                }
                            });
                    }
                    catch
                    {
                        sync.TrySetResult();
                        throw;
                    }

                    if (generation != _worldAssetLoadGeneration)
                    {
                        sync.TrySetResult();
                        return;
                    }

                    _worldAssetBytes = worldAssetBytes;
                    sync.TrySetResult();
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

        public void UpdateWorldAssets(
            IReadOnlyList<AssetsDefinitionPacket> assetManifest)
        {
            EnsureInitialized();
            if (!_isWorldAssetsReady)
            {
                // Мир ещё входит: подменять нечего, ассеты грузятся целиком
                LoadWorldAssets(assetManifest);
                return;
            }

            int generation = _worldAssetLoadGeneration;
            int update = ++_contentUpdateSerial;
            CancellationToken worldLifetime = _worldAssetsLifetime.Token;
            AssetsDefinitionPacket[] manifestSnapshot = new List<AssetsDefinitionPacket>(assetManifest).ToArray();

            // Описания клеток уже новые, а байты ещё старые: пока докачивается манифест, типам, которых нет в памяти,
            // файл берётся по новому описанию напрямую, а не из прежнего набора
            _worldAssetBytes = null;
            _operations.Run(
                "update_world_assets",
                async cancellationToken =>
                {
                    var replacements = new List<TextureReplacement>();
                    try
                    {
                        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken,
                            worldLifetime);
                        var synchronizer = new AssetManifestSynchronizer(
                            _persistentAssetCache,
                            _assetLoader);
                        IReadOnlyDictionary<string, byte[]> newBytes = await synchronizer.SynchronizeAsync(
                            manifestSnapshot,
                            linkedCancellation.Token);
                        var evictions = new List<CellType>();
                        await PrepareTextureReplacementsAsync(
                            newBytes,
                            replacements,
                            evictions,
                            linkedCancellation.Token);
                        await UniTask.SwitchToMainThread(linkedCancellation.Token);
                        if (generation != _worldAssetLoadGeneration || update != _contentUpdateSerial)
                        {
                            // Мир сменился или пришло более свежее обновление: этот результат устарел
                            return;
                        }

                        CommitContentUpdate(newBytes, manifestSnapshot, replacements, evictions, generation);
                        replacements.Clear();
                    }
                    finally
                    {
                        if (replacements.Count > 0)
                        {
                            await UniTask.SwitchToMainThread();
                            foreach (TextureReplacement replacement in replacements)
                            {
                                DestroyUnusedTexture(replacement.Texture);
                            }
                        }
                    }
                });
        }

        // Декодирует заранее, до подмены, текстуры тех загруженных типов, у которых файл изменился: сама подмена тогда
        // идёт за один кадр и экран не успевает показать пустые клетки
        private async UniTask PrepareTextureReplacementsAsync(
            IReadOnlyDictionary<string, byte[]> newBytes,
            List<TextureReplacement> replacements,
            List<CellType> evictions,
            CancellationToken cancellationToken)
        {
            // Источники текстур и каталог клеток принадлежат главному потоку
            await UniTask.SwitchToMainThread(cancellationToken);
            foreach (CellTextureInfo info in _textureCache.Snapshot())
            {
                cancellationToken.ThrowIfCancellationRequested();
                CellType cellType = info.CellType;
                if (!TryGetCurrentDefinition(cellType, out AssetsDefinitionPacket definition))
                {
                    evictions.Add(cellType);
                    continue;
                }

                if (_textureSources.TryGetValue(cellType, out (string Filename, string ETag) source) &&
                    string.Equals(source.Filename, definition.Filename, StringComparison.Ordinal) &&
                    string.Equals(source.ETag, definition.ETag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!newBytes.TryGetValue(definition.Filename, out byte[]? bytes) || bytes.Length == 0)
                {
                    evictions.Add(cellType);
                    continue;
                }

                await UniTask.SwitchToMainThread(cancellationToken);
                Texture2D? texture = null;
                try
                {
                    texture = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                        bytes,
                        definition.Filename,
                        RuntimeTextureColorSpace.Srgb,
                        FilterMode.Point,
                        TextureWrapMode.Clamp,
                        makeNoLongerReadable: true);
                    _atlasCollection.ValidateDimensions(cellType, texture, ComputeFrameHeight(cellType, texture));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Одна негодная текстура не должна ронять всё обновление: тип освобождается и загрузится
                    // обычным путём, где сбой так же виден в журнале
                    Debug.LogError(
                        $"[WorldTextureManager] Updated texture '{definition.Filename}' for {cellType} is unusable: {exception}");
                    DestroyUnusedTexture(texture);
                    evictions.Add(cellType);
                    continue;
                }

                replacements.Add(new TextureReplacement(cellType, texture, definition));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private bool TryGetCurrentDefinition(CellType cellType, out AssetsDefinitionPacket definition)
        {
            definition = default;
            string textureName;
            try
            {
                textureName = _mapManager.GetCellConfig(cellType).TextureName;
            }
            catch (InvalidOperationException)
            {
                // Нового описания у типа нет: сервер больше не определяет его
                return false;
            }

            return _mapManager.TryGetAssetDefinition(textureName, out definition);
        }

        // Одним синхронным куском: описания клеток уже новые, здесь подменяются байты, текстуры и атлас
        private void CommitContentUpdate(
            IReadOnlyDictionary<string, byte[]> newBytes,
            AssetsDefinitionPacket[] manifest,
            List<TextureReplacement> replacements,
            List<CellType> evictions,
            int generation)
        {
            // Загрузки, начатые по прежним описаниям, отменяются: их имена и байты больше не актуальны
            CancellationTokenSource previousLifetime = _worldAssetsLifetime;
            _worldAssetsLifetime = new CancellationTokenSource();
            previousLifetime.Cancel();
            previousLifetime.Dispose();
            _retryTracker.ResetFailures();
            _worldAssetBytes = newBytes;

            foreach (CellType cellType in evictions)
            {
                _textureCache.Remove(cellType);
                _textureSources.Remove(cellType);
            }

            var replacedTypes = new HashSet<CellType>();
            foreach (TextureReplacement replacement in replacements)
            {
                replacedTypes.Add(replacement.CellType);
            }

            // Запасные цветные текстуры строились по прежнему цвету карты: пусть строятся заново
            foreach (CellTextureInfo info in _textureCache.Snapshot())
            {
                if (!_textureSources.ContainsKey(info.CellType) && !replacedTypes.Contains(info.CellType))
                {
                    _textureCache.Remove(info.CellType);
                }
            }

            // Атлас не умеет заменять одну клетку, поэтому собирается заново из кэша: прежние текстуры остаются,
            // изменившиеся встают на их место. Прежний атлас дорисовывает старую сетку, пока террейн не перепривяжет
            List<CellTextureInfo> kept = _textureCache.Snapshot();
            WorldAtlasCollection retired = _atlasCollection;
            _atlasCollection = CreateAtlasCollection();
            foreach (CellTextureInfo info in kept)
            {
                if (replacedTypes.Contains(info.CellType))
                {
                    continue;
                }

                TextureAtlas? previousAtlas = retired.GetAtlasForCell(info.CellType);
                AddTextureToAtlas(
                    info.CellType,
                    info.BaseTexture,
                    info.OwnsBaseTexture,
                    previousAtlas != null ? previousAtlas.IsFullyOpaque(info.CellType) : null);
            }

            foreach (TextureReplacement replacement in replacements)
            {
                AddTextureToAtlas(replacement.CellType, replacement.Texture, ownsTexture: true);
                _textureSources[replacement.CellType] = (replacement.Definition.Filename, replacement.Definition.ETag);
            }

            _operations.Run(
                "retire_world_texture_atlases",
                cancellationToken => RetireAtlasesAsync(retired, cancellationToken));

            // Файлы, которые сервер заменил, отбрасываются из памяти загрузчика, и тот, кто их показывает, берёт новые
            Dictionary<string, string> current = CreateETagMap(manifest);
            List<string> replaced = FindReplacedFiles(_appliedManifestETags, current);
            _appliedManifestETags = current;
            if (replaced.Count > 0)
            {
                _assetLoader.ReplaceAssets(replaced);
            }

            _auxiliaryAssets.LoadDecals(
                newBytes,
                _operations,
                () =>
                {
                    if (generation == _worldAssetLoadGeneration)
                    {
                        RaiseTerrainDecalAtlasLoaded();
                    }
                },
                keepCurrentUntilReady: true);
        }

        private static Dictionary<string, string> CreateETagMap(IEnumerable<AssetsDefinitionPacket> manifest)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AssetsDefinitionPacket asset in manifest)
            {
                map[asset.Filename] = asset.ETag;
            }

            return map;
        }

        private static List<string> FindReplacedFiles(
            Dictionary<string, string> previous,
            Dictionary<string, string> current)
        {
            var replaced = new List<string>();
            foreach (KeyValuePair<string, string> pair in current)
            {
                if (previous.TryGetValue(pair.Key, out string? previousETag) &&
                    !string.Equals(previousETag, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    replaced.Add(pair.Key);
                }
            }

            foreach (string filename in previous.Keys)
            {
                if (!current.ContainsKey(filename))
                {
                    replaced.Add(filename);
                }
            }

            return replaced;
        }

        private static async UniTask RetireAtlasesAsync(
            WorldAtlasCollection retired,
            CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Delay(RetiredAtlasLifetimeMilliseconds, cancellationToken: cancellationToken);
            }
            finally
            {
                retired.Dispose();
            }
        }

        private static void DestroyUnusedTexture(Texture2D? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        private readonly struct TextureReplacement
        {
            public TextureReplacement(CellType cellType, Texture2D texture, AssetsDefinitionPacket definition)
            {
                CellType = cellType;
                Texture = texture;
                Definition = definition;
            }

            public CellType CellType { get; }

            public Texture2D Texture { get; }

            public AssetsDefinitionPacket Definition { get; }
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
            (string Filename, string ETag)? decodedFrom = null;
            try
            {
                if (_mapManager.TryGetAssetDefinition(filename, out AssetsDefinitionPacket definition))
                {
                    // Манифест ещё скачивается — ждём его, а не качаем тот же файл вторым запросом наперегонки с ним
                    if (_worldAssetBytes == null && _worldAssetsSync != null)
                    {
                        await _worldAssetsSync.Task.AttachExternalCancellation(cancellationToken);
                    }

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
                        // Байты из манифеста уже сверены с ETag при синхронизации — второй MD5 на каждую текстуру не нужен
                        string? actualETag = worldAssetBytes != null ? definition.ETag : ETagCalculator.Calculate(bytes);
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
                        decodedFrom = (definition.Filename, definition.ETag);
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
                if (decodedFrom is { } source)
                {
                    _textureSources[cellType] = source;
                }

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
            bool ownsTexture,
            bool? fullyOpaque = null)
        {
            using var atlasAddMarker = s_atlasAddMarker.Auto();
            if (_atlasCollection.ContainsCell(cellType) || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            int frameHeight = ComputeFrameHeight(cellType, texture);

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
            _atlasCollection.AddTexture(cellType, texture, fullyOpaque ?? TextureAtlas.MeasureFullyOpaque(texture));
            _textureCache.AddTexture(cellType, textureInfo);
            TextureRevision++;
            OnTextureLoaded?.Invoke(cellType, texture);
        }

        private int ComputeFrameHeight(CellType cellType, Texture2D texture)
        {
            if (texture.width == _cellTextureSize &&
                texture.height > _cellTextureSize &&
                texture.height % _cellTextureSize == 0)
            {
                return _cellTextureSize;
            }

            if (cellType == CellType.Unloaded &&
                texture.height > _cellTextureSize &&
                texture.height % _cellTextureSize == 0)
            {
                return _cellTextureSize;
            }

            return 0;
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
            _worldAssetsSync?.TrySetResult();
            _worldAssetsSync = null;
            _retryTracker.ResetFailures();
            _textureCache.Clear();
            _textureSources.Clear();
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
