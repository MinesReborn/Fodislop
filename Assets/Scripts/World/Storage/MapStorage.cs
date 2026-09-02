#nullable enable

using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Persistence;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World
{
    public class MapStorage : IWorldDataStorage, IWorldPersistence
    {
        private WorldLayer<CellType>? _cellLayer;
        private string? _mapFilePath;
        private readonly SemaphoreSlim _persistenceGate = new(1, 1);
        private readonly IAsyncOperationSupervisor _operations;

        private const string MapExtension = ".map";
        private const string BackupMapSuffix = ".backup.map";

        public MapStorage(IAsyncOperationSupervisor operations)
        {
            _operations = operations;
        }

        private bool _isInitialized;
        private string _worldCodeName = string.Empty;
        private int _worldWidth;
        private int _worldHeight;
        private bool _clippedRegionWarningLogged;

        public IWorldLayer<CellType>? CellLayer => _cellLayer;

        public string MapFilePath => _mapFilePath ?? throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

        public string BackupMapFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_worldCodeName))
                {
                    return string.Empty;
                }

                return Path.Combine(Application.persistentDataPath, _worldCodeName + BackupMapSuffix);
            }
        }

        public bool IsReady => _isInitialized && _cellLayer != null;
        public bool HasDirtyChunks => _cellLayer?.HasDirtyChunks == true;

        public long Revision { get; private set; }

        public bool IsDisposed { get; private set; }

        public event Action<int, int>? CellChanged;
        public event Action<int, int, int, int>? RegionChanged;

        public void EnsureEditorInitialized()
        {
#if UNITY_EDITOR
            if (_isInitialized || Application.isPlaying)
            {
                return;
            }

            InitWorld("EditorPreview", 128, 128);
#else
            throw new InvalidOperationException(
                "[MapStorage] EnsureEditorInitialized is available only in the Unity Editor.");
#endif
        }

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Dispose();

            if (string.IsNullOrEmpty(worldCodeName))
            {
                throw new ArgumentException("[MapStorage] World code name cannot be null or empty", nameof(worldCodeName));
            }

            worldCodeName = SanitizeWorldCodeName(worldCodeName);

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException($"[MapStorage] Invalid world dimensions: {width}x{height}");
            }

            _worldCodeName = worldCodeName;
            _worldWidth = width;
            _worldHeight = height;
            _clippedRegionWarningLogged = false;
            int widthChunks = (width + ProjectRuntimeContracts.World.ChunkSize - 1) /
                ProjectRuntimeContracts.World.ChunkSize;
            int heightChunks = (height + ProjectRuntimeContracts.World.ChunkSize - 1) /
                ProjectRuntimeContracts.World.ChunkSize;

            if (widthChunks <= 0 || heightChunks <= 0)
            {
                throw new ArgumentOutOfRangeException($"[MapStorage] Invalid chunk calculation: {widthChunks}x{heightChunks}");
            }

            string path = Path.Combine(Application.persistentDataPath, worldCodeName + MapExtension);
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                CreateBackup(path, worldCodeName);
                _cellLayer = new WorldLayer<CellType>(
                    path,
                    widthChunks,
                    heightChunks,
                    _operations,
                    ProjectRuntimeContracts.World.ChunkSize,
                    maxRamChunks: 2000);
                _mapFilePath = path;
                _isInitialized = true;
                IsDisposed = false;
                Revision++;
            }
            catch (IOException ioEx)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw new IOException($"[MapStorage] Could not open map file '{path}': {ioEx.Message}", ioEx);
            }
            catch (UnauthorizedAccessException authEx)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw new UnauthorizedAccessException($"[MapStorage] Access denied for map file '{path}': {authEx.Message}", authEx);
            }
            catch (OutOfMemoryException)
            {
                _cellLayer = null;
                _mapFilePath = null;
                throw;
            }
        }

        /// <summary>
        /// Заменяет символы, недопустимые в именах файлов Windows, чтобы имя мира
        /// от сервера не роняло путь ({name}.mapb) на любой платформе.
        /// </summary>
        private static string SanitizeWorldCodeName(string worldCodeName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sanitized = new System.Text.StringBuilder(worldCodeName.Length);
            foreach (char c in worldCodeName)
            {
                sanitized.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            // Завершающие точка/пробел недопустимы в именах файлов Windows.
            string result = sanitized.ToString().TrimEnd('.', ' ');
            return string.IsNullOrEmpty(result) ? "world" : result;
        }

        private static void CreateBackup(string mapPath, string worldCodeName)
        {
            if (!File.Exists(mapPath))
            {
                return;
            }

            string backupPath = Path.Combine(
                Application.persistentDataPath,
                worldCodeName + BackupMapSuffix);
            File.Copy(mapPath, backupPath, overwrite: true);
        }

        public bool IsInitialized() => _isInitialized;

        public string GetWorldCodeName() => _worldCodeName;

        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException("[MapStorage] GetCell called before world initialization");
            }

            return _cellLayer.GetCell(x, y, touchLru: true);
        }

        public void SetCell(int x, int y, CellType type)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException(
                    $"[MapStorage] SetCell called before world initialization: ({x},{y}).");
            }

            if (_cellLayer.GetCellSync(x, y, touchLru: true) == type)
            {
                return;
            }

            _cellLayer[x, y] = type;
            Revision++;
            CellChanged?.Invoke(x, y);
        }

        public void SetRegion(
            int startX,
            int startY,
            int width,
            int height,
            CellType[] cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            SetRegion(startX, startY, width, height, cells.AsSpan());
        }

        public void SetRegion(
            int startX,
            int startY,
            int width,
            int height,
            ReadOnlySpan<CellType> cells)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                throw new InvalidOperationException(
                    $"[MapStorage] SetRegion called before world initialization: " +
                    $"({startX},{startY}) {width}x{height}.");
            }

            long expectedCellCount = (long)width * height;
            if (width <= 0 || height <= 0 || cells.Length < expectedCellCount)
            {
                throw new ArgumentException(
                    $"[MapStorage] Invalid region ({startX},{startY}) {width}x{height}: " +
                    $"payload has {cells.Length} cells, expected at least {expectedCellCount}.",
                    nameof(cells));
            }

            if (startX < 0 || startY < 0 || startX >= _worldWidth || startY >= _worldHeight)
            {
                string message =
                    "[MapStorage] Region " +
                    $"({startX},{startY}) {width}x{height} " +
                    $"is outside world bounds {_worldWidth}x{_worldHeight}.";
                throw new ArgumentOutOfRangeException(
                    nameof(startX),
                    message);
            }

            int appliedWidth = Math.Min(width, _worldWidth - startX);
            int appliedHeight = Math.Min(height, _worldHeight - startY);
            if (appliedWidth != width || appliedHeight != height)
            {
                if (!_clippedRegionWarningLogged)
                {
                    Debug.LogWarning(
                        $"[MapStorage] Clipping padded edge regions to world bounds " +
                        $"({_worldWidth}x{_worldHeight}); first region " +
                        $"({startX},{startY}) {width}x{height} -> " +
                        $"{appliedWidth}x{appliedHeight}.");
                    _clippedRegionWarningLogged = true;
                }
            }

            // Bulk write: WorldLayer.SetRegion applies the payload chunk-by-chunk
            // with one LRU touch per chunk instead of per cell (a 32x32 region used
            // to issue ~2048 LRU/Dictionary operations through GetCellSync+SetCell,
            // costing several milliseconds per region and stretching the initial
            // world burst across dozens of frames under the packet-drain budget).
            int changedCells = _cellLayer.SetRegion(
                startX,
                startY,
                width,
                height,
                cells,
                0);

            if (changedCells > 0)
            {
                Revision++;
                RegionChanged?.Invoke(startX, startY, width, height);
            }

            // SetRegion materializes chunks synchronously, so WorldLayer's
            // asynchronous disk-load notification is not emitted. Consumers
            // such as the minimap may already have cached these chunks as
            // unavailable; notify them after the packet has been applied.
            _cellLayer.NotifyRegionLoaded(startX, startY, appliedWidth, appliedHeight);
        }

        /// <summary>
        /// Persists all dirty map chunks immediately.
        /// The layer normally flushes on chunk eviction and dispose, but the
        /// application can be paused or terminated while dirty chunks are
        /// still resident in the RAM cache.
        /// </summary>
        public void Flush()
        {
            // A separate overload rather than an optional parameter: an
            // optional parameter does not match IWorldDataStorage.Flush(),
            // which declares none, so the type stops implementing the
            // interface.
            Flush(durable: true);
        }

        /// <summary>
        /// Persists all dirty map chunks, optionally forcing them onto the
        /// physical drive.
        /// </summary>
        /// <param name="durable">
        /// Whether to force the bytes all the way onto the physical drive.
        /// <para>
        /// This is <c>FileStream.Flush(true)</c>, which on macOS issues
        /// F_FULLFSYNC and blocks until the drive acknowledges the write - tens
        /// of milliseconds, routinely. It belongs to quit, pause and low-memory,
        /// where the process is about to stop and the cost is paid once.
        /// </para>
        /// <para>
        /// It must not be on the five-second autosave from MapManager.Update,
        /// which is on the main thread: that turns a periodic save into a
        /// periodic stall, visible as an evenly spaced comb of spikes through
        /// an otherwise flat frame graph. Passing false still flushes the
        /// managed buffers to the OS, so the data survives a process crash -
        /// only an OS crash or power loss can lose it, and the next durable
        /// flush on quit closes that window.
        /// </para>
        /// </param>
        public void Flush(bool durable)
        {
            _persistenceGate.Wait();
            try
            {
                FlushCore(durable);
            }
            finally
            {
                _persistenceGate.Release();
            }
        }

        public async UniTask FlushAsync(
            bool durable,
            CancellationToken cancellationToken = default)
        {
            await _persistenceGate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(() => FlushCore(durable));
            }
            finally
            {
                _persistenceGate.Release();
                await UniTask.SwitchToMainThread();
            }
        }

        private void FlushCore(bool durable)
        {
            if (_cellLayer == null || !_isInitialized || IsDisposed)
            {
                return;
            }

            try
            {
                _cellLayer.Flush(flushToDisk: durable);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ObjectDisposedException)
            {
                throw new IOException(
                    $"[MapStorage] Failed to persist map '{MapFilePath}'. " +
                    "The world cannot continue with unsaved chunks.",
                    ex);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "SonarAnalyzer.CSharp",
            "S3877",
            Justification = "Persistent map close failures must propagate instead of becoming silent data loss.")]
        public void Dispose()
        {
            _persistenceGate.Wait();
            try
            {
                DisposeCore();
            }
            finally
            {
                _persistenceGate.Release();
            }
        }

        public async UniTask DisposeAsync(CancellationToken cancellationToken = default)
        {
            await _persistenceGate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.RunOnThreadPool(DisposeCore);
            }
            finally
            {
                _persistenceGate.Release();
                await UniTask.SwitchToMainThread();
            }
        }

        private void DisposeCore()
        {
            Exception? disposeFailure = null;
            try
            {
                _cellLayer?.Dispose();
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ObjectDisposedException)
            {
                disposeFailure = ex;
            }
            finally
            {
                _cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                _worldWidth = 0;
                _worldHeight = 0;
                _clippedRegionWarningLogged = false;
                _mapFilePath = null;
                IsDisposed = true;
                Revision++;
            }

            if (disposeFailure != null)
            {
                throw new IOException(
                    "[MapStorage] Failed to close the persistent world map after flushing.",
                    disposeFailure);
            }
        }
    }
}
