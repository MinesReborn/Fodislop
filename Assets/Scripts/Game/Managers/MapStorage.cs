using MinesServer.Data;
using System.IO;
using UnityEngine;
using System;
using Cysharp.Threading.Tasks;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class MapStorage
    {
        private static MapStorage _instance;
        public static MapStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MapStorage();
                }
                return _instance;
            }
        }

        public WorldLayer<CellType> cellLayer;
        public bool _isInitialized = false;
        public string _worldCodeName;

        public bool IsReady => _isInitialized && cellLayer != null;

        public event Action OnChunkLoaded;

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Debug.Log($"[MapStorage] InitWorld called: world='{worldCodeName}', dimensions={width}x{height}");

            Dispose();

            if (string.IsNullOrEmpty(worldCodeName))
            {
                Debug.LogError("[MapStorage] World code name cannot be null or empty");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[MapStorage] Invalid world dimensions: {width}x{height}. Dimensions must be positive.");
                return;
            }

            _worldCodeName = worldCodeName;
            var path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
            Debug.Log($"[MapStorage] Initializing cell storage at: {path} for world '{worldCodeName}' with dimensions {width}x{height}");

            try
            {
                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;

                Debug.Log($"Calculating chunks: width={width}, height={height}, chunkSize={chunkSize}");
                Debug.Log($"Resulting chunks: {widthChunks}x{heightChunks} = {widthChunks * heightChunks} total chunks");

                if (widthChunks <= 0 || heightChunks <= 0)
                {
                    Debug.LogError($"MapStorage.InitWorld: Invalid chunk calculation. widthChunks={widthChunks}, heightChunks={heightChunks}");
                    return;
                }

                long totalChunks = (long)widthChunks * heightChunks;
                if (totalChunks > 1000000)
                {
                    Debug.LogWarning($"MapStorage.InitWorld: Very large map detected ({totalChunks} chunks). This may cause performance issues.");
                }

                if (widthChunks > 100000 || heightChunks > 100000)
                {
                    Debug.LogError($"MapStorage.InitWorld: World dimensions too large for WorldLayer. Max supported: 100000x100000 chunks");
                    return;
                }

                if (chunkSize <= 0 || chunkSize > 1024)
                {
                    Debug.LogError($"MapStorage.InitWorld: Invalid chunk size: {chunkSize}. Must be between 1 and 1024.");
                    return;
                }

                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        Debug.Log($"Created directory: {directory}");
                    }
                    catch (System.Exception dirEx)
                    {
                        Debug.LogError($"MapStorage.InitWorld: Cannot create directory '{directory}': {dirEx.Message}");

                        string fallbackDir = Path.Combine(Application.persistentDataPath, "fallback");
                        try
                        {
                            Directory.CreateDirectory(fallbackDir);
                            path = Path.Combine(fallbackDir, $"{worldCodeName}_cells.mapb");
                            Debug.LogWarning($"MapStorage.InitWorld: Using fallback directory: {path}");
                        }
                        catch
                        {
                            Debug.LogError("MapStorage.InitWorld: Fallback directory creation also failed");
                            _isInitialized = false;
                            return;
                        }
                    }
                }

                if (File.Exists(path))
                {
                    Debug.Log($"MapStorage.InitWorld: Existing world file found at {path}, will be used");
                }

                try
                {
                    Debug.Log($"[MapStorage] Creating WorldLayer with parameters: path={path}, widthChunks={widthChunks}, heightChunks={heightChunks}, chunkSize={chunkSize}");

                    cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);

                    if (cellLayer == null)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer creation failed - returned null");
                        _isInitialized = false;
                        return;
                    }

                    // Подписка на событие загрузки чанков
                    cellLayer.OnChunkLoaded += _ => OnChunkLoaded?.Invoke();

                    Debug.Log($"[MapStorage] WorldLayer created successfully");
                    Debug.Log($"[MapStorage] WorldLayer verification: WidthChunks={cellLayer.WidthChunks}, HeightChunks={cellLayer.HeightChunks}, ChunkSize={cellLayer.ChunkSize}");

                    if (cellLayer.WidthChunks != widthChunks || cellLayer.HeightChunks != heightChunks)
                    {
                        Debug.LogWarning($"[MapStorage] WorldLayer dimensions don't match expected: expected {widthChunks}x{heightChunks}, got {cellLayer.WidthChunks}x{cellLayer.HeightChunks}");
                    }

                    if (cellLayer == null)
                    {
                        Debug.LogError("[MapStorage] CRITICAL: cellLayer is null after WorldLayer creation");
                        _isInitialized = false;
                        return;
                    }

                    try
                    {
                        var testCell = cellLayer[0, 0];
                        Debug.Log($"[MapStorage] WorldLayer basic cell access test passed: {testCell}");
                    }
                    catch (System.Exception cellTestEx)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer cell access failed: {cellTestEx.Message}");
                        _isInitialized = false;
                        cellLayer?.Dispose();
                        cellLayer = null;
                        return;
                    }
                }
                catch (System.Exception worldLayerEx)
                {
                    Debug.LogError($"[MapStorage] CRITICAL: WorldLayer constructor failed for '{worldCodeName}': {worldLayerEx.Message}");

                    if (worldLayerEx is System.IO.IOException)
                    {
                        TryCreateFallbackWorld(worldCodeName, width, height);
                    }
                    else if (worldLayerEx is System.ArgumentException)
                    {
                        TryCreateWithDifferentChunkSize(worldCodeName, width, height);
                    }
                    else if (worldLayerEx is System.OutOfMemoryException)
                    {
                        TryCreateSmallerTestWorld(worldCodeName);
                    }
                    else
                    {
                        TryEmergencyFallback(worldCodeName, width, height);
                    }

                    _isInitialized = false;
                    cellLayer?.Dispose();
                    cellLayer = null;
                    return;
                }

                _isInitialized = true;

                Debug.Log($"[MapStorage] SUCCESS: MapStorage initialized successfully for world '{worldCodeName}' with dimensions {width}x{height} ({widthChunks}x{heightChunks} chunks)");

                if (IsReady)
                {
                    Debug.Log($"[MapStorage] VERIFICATION: MapStorage is fully ready for terrain rendering");
                }
                else
                {
                    Debug.LogError($"[MapStorage] CRITICAL: MapStorage initialization completed but not ready for terrain rendering");
                }
            }
            catch (System.IO.IOException ioEx)
            {
                TryCreateFallbackWorld(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.ArgumentException argEx)
            {
                TryCreateWithDifferentChunkSize(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.OutOfMemoryException memEx)
            {
                TryCreateSmallerTestWorld(worldCodeName);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.UnauthorizedAccessException authEx)
            {
                TryCreateFallbackWorld(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.Security.SecurityException secEx)
            {
                TryEmergencyFallback(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
            catch (System.Exception ex)
            {
                TryEmergencyFallback(worldCodeName, width, height);
                _isInitialized = false;
                cellLayer?.Dispose();
                cellLayer = null;
            }
        }

        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || cellLayer == null)
                return CellType.Unloaded;

            // Если чанк не в кэше — не трогаем диск, возвращаем Unloaded
            if (!cellLayer.IsChunkCached(x, y))
                return CellType.Unloaded;

            return cellLayer[x, y];
        }

        public void SetCell(int x, int y, CellType cellType)
        {
            if (!_isInitialized || cellLayer == null)
            {
                Debug.LogWarning($"Cannot set cell at ({x}, {y}) - MapStorage not initialized");
                return;
            }

            try
            {
                cellLayer[x, y] = cellType;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error setting cell at ({x}, {y}) to {cellType}: {ex.Message}");
            }
        }

        public async UniTask PreloadChunkAtCoordinateAsync(int x, int y)
        {
            if (!_isInitialized || cellLayer == null) return;
            if (!cellLayer.GetChunkIndexAndLocal(x, y, out int chunkIndex, out _)) return;
            await cellLayer.PreloadChunkAsync(chunkIndex);
        }

        public string GetWorldCodeName() => _worldCodeName;
        public bool IsInitialized() => _isInitialized;

        private MapStorage()
        {
            _isInitialized = false;
            _worldCodeName = string.Empty;
        }

        private void TryCreateFallbackWorld(string worldCodeName, int width, int height)
        {
            Debug.LogWarning($"[MapStorage] Attempting fallback world creation for '{worldCodeName}'");
            try
            {
                string fallbackPath = Path.Combine(Application.temporaryCachePath, $"{worldCodeName}_fallback_cells.mapb");
                Debug.LogWarning($"[MapStorage] Using fallback path: {fallbackPath}");

                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;

                cellLayer = new WorldLayer<CellType>(fallbackPath, widthChunks, heightChunks, chunkSize);
                cellLayer.OnChunkLoaded += _ => OnChunkLoaded?.Invoke();
                _isInitialized = true;

                Debug.Log($"[MapStorage] Fallback world created successfully at {fallbackPath}");
            }
            catch (System.Exception fallbackEx)
            {
                Debug.LogError($"[MapStorage] Fallback world creation failed: {fallbackEx.Message}");
            }
        }

        private void TryCreateWithDifferentChunkSize(string worldCodeName, int width, int height)
        {
            Debug.LogWarning($"[MapStorage] Attempting world creation with different chunk size for '{worldCodeName}'");
            int[] chunkSizes = { 16, 64, 128 };

            foreach (int chunkSize in chunkSizes)
            {
                try
                {
                    string path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
                    int widthChunks = (width + chunkSize - 1) / chunkSize;
                    int heightChunks = (height + chunkSize - 1) / chunkSize;

                    cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                    cellLayer.OnChunkLoaded += _ => OnChunkLoaded?.Invoke();
                    _isInitialized = true;

                    Debug.Log($"[MapStorage] World created successfully with chunk size {chunkSize}");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[MapStorage] Chunk size {chunkSize} failed: {ex.Message}");
                }
            }

            Debug.LogError("[MapStorage] All chunk size attempts failed");
        }

        private void TryCreateSmallerTestWorld(string worldCodeName)
        {
            Debug.LogWarning($"[MapStorage] Creating smaller test world for '{worldCodeName}' due to memory issues");
            try
            {
                string path = $"{Application.persistentDataPath}/{worldCodeName}_test_cells.mapb";
                const int testWidth = 64;
                const int testHeight = 64;
                const int chunkSize = 32;

                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;

                cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                cellLayer.OnChunkLoaded += _ => OnChunkLoaded?.Invoke();
                _isInitialized = true;

                Debug.Log($"[MapStorage] Smaller test world created: {testWidth}x{testHeight}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Smaller test world creation failed: {ex.Message}");
            }
        }

        private void TryEmergencyFallback(string worldCodeName, int width, int height)
        {
            Debug.LogError($"[MapStorage] Attempting emergency fallback for '{worldCodeName}'");
            try
            {
                string path = $"{Application.persistentDataPath}/emergency_test_cells.mapb";
                const int testWidth = 32;
                const int testHeight = 32;
                const int chunkSize = 32;

                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;

                cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                cellLayer.OnChunkLoaded += _ => OnChunkLoaded?.Invoke();
                _isInitialized = true;
                _worldCodeName = "emergency_test";

                Debug.Log($"[MapStorage] Emergency fallback world created: {testWidth}x{testHeight}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Emergency fallback failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                cellLayer?.Dispose();
                cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                Debug.Log("MapStorage disposed successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error disposing MapStorage: {ex.Message}");
            }
        }
    }
}
