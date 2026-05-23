using System.IO;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class MapStorage
    {
        private static MapStorage _instance;

        private WorldLayer<CellType> _cellLayer;
        private bool _isInitialized = false;
        private string _worldCodeName;

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

        public WorldLayer<CellType> CellLayer => _cellLayer;

        public bool IsReady => _isInitialized && _cellLayer != null;

#if UNITY_EDITOR
        public void EnsureEditorInitialized()
        {
            if (_isInitialized || Application.isPlaying)
            {
                return;
            }

            InitWorld("EditorPreview", 128, 128);
        }
#endif

        public void InitWorld(string worldCodeName, int width, int height)
        {
            Debug.Log($"[MapStorage] InitWorld called: world='{worldCodeName}', dimensions={width}x{height}");

            // CRITICAL: Reset state before initialization attempt
            Dispose(); // Ensure clean state

            // Validate input parameters
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
                // Use configurable chunk size (default 32) - should match WorldLayer default
                int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;

                Debug.Log($"Calculating chunks: width={width}, height={height}, chunkSize={chunkSize}");
                Debug.Log($"Resulting chunks: {widthChunks}x{heightChunks} = {widthChunks * heightChunks} total chunks");

                // Validate chunk calculations - CRITICAL FIX
                if (widthChunks <= 0 || heightChunks <= 0)
                {
                    Debug.LogError($"MapStorage.InitWorld: Invalid chunk calculation. widthChunks={widthChunks}, heightChunks={heightChunks}");
                    return;
                }

                // Check for potential memory issues with very large maps
                long totalChunks = (long)widthChunks * heightChunks;
                if (totalChunks > 1000000) // 1M chunks limit
                {
                    Debug.LogWarning($"MapStorage.InitWorld: Very large map detected ({totalChunks} chunks). This may cause performance issues.");
                }

                // Additional validation for WorldLayer constructor parameters
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

                // Check if directory exists and is writable
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
                    }
                }

                // Create the WorldLayer
                try
                {
                    Debug.Log($"[MapStorage] Creating WorldLayer with parameters: path={path}, widthChunks={widthChunks}, heightChunks={heightChunks}, chunkSize={chunkSize}");

                    // CRITICAL FIX: Ensure proper parameter order and validation
                    // WorldLayer constructor expects: path, widthChunks, heightChunks, chunkSize
                    _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);

                    // CRITICAL: Verify the WorldLayer was created successfully
                    if (_cellLayer == null)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer creation failed - returned null");
                        Debug.LogError($"[MapStorage] This is a fundamental failure - terrain rendering cannot work");
                        _isInitialized = false;
                        return;
                    }

                    Debug.Log($"[MapStorage] WorldLayer created successfully");
                    Debug.Log($"[MapStorage] WorldLayer verification: WidthChunks={_cellLayer.WidthChunks}, HeightChunks={_cellLayer.HeightChunks}, ChunkSize={_cellLayer.ChunkSize}");

                    // Additional verification - CRITICAL CHECK
                    if (_cellLayer.WidthChunks != widthChunks || _cellLayer.HeightChunks != heightChunks)
                    {
                        Debug.LogWarning($"[MapStorage] WorldLayer dimensions don't match expected: expected {widthChunks}x{heightChunks}, got {_cellLayer.WidthChunks}x{_cellLayer.HeightChunks}");
                    }

                    // Test basic cell access to verify WorldLayer is functional
                    try
                    {
                        var testCell = _cellLayer[0, 0];
                        Debug.Log($"[MapStorage] WorldLayer basic cell access test passed: {testCell}");
                    }
                    catch (System.Exception cellTestEx)
                    {
                        Debug.LogError($"[MapStorage] CRITICAL: WorldLayer cell access failed: {cellTestEx.Message}");
                        Debug.LogError("[MapStorage] WorldLayer appears to be created but not functional");
                        _isInitialized = false;
                        _cellLayer?.Dispose();
                        _cellLayer = null;
                        return;
                    }
                }
                catch (System.Exception worldLayerEx)
                {
                    Debug.LogError($"[MapStorage] CRITICAL: Exception while creating WorldLayer: {worldLayerEx.Message}");
                    Debug.LogError($"[MapStorage] Exception type: {worldLayerEx.GetType().Name}");
                    Debug.LogError($"[MapStorage] Stack trace: {worldLayerEx.StackTrace}");

                    // Attempt to identify specific issue
                    if (worldLayerEx is System.IO.IOException)
                    {
                        Debug.LogError("[MapStorage] This is likely an I/O issue (disk full, permission, file in use)");

                        // Try fallback path if permission error
                        TryCreateFallbackWorld(worldCodeName, width, height);
                    }
                    else
                    {
                        // Try emergency fallback
                        TryEmergencyFallback(worldCodeName, width, height);
                    }

                    if (!_isInitialized)
                    {
                        _cellLayer?.Dispose();
                        _cellLayer = null;
                        return;
                    }
                }

                _isInitialized = true;

                // Set StandaloneMode in MapManager if we are inStandaloneMode
                if (MapManager.Instance != null && MapManager.Instance.IsStandaloneMode)
                {
                    Debug.Log("[MapStorage] Standalone mode detected, MapStorage ready");
                }

                // Final verification that everything is ready - CRITICAL CHECK
                if (IsReady)
                {
                    Debug.Log($"[MapStorage] VERIFICATION: MapStorage is fully ready for terrain rendering");
                    Debug.Log($"[MapStorage] Ready state: IsReady={IsReady}, IsInitialized={IsInitialized()}, _cellLayer={(_cellLayer != null ? "not null" : "NULL")}");

                    // CRITICAL: Log detailed _cellLayer information for debugging
                    if (_cellLayer != null)
                    {
                        Debug.Log($"[MapStorage] _cellLayer details: WidthChunks={_cellLayer.WidthChunks}, HeightChunks={_cellLayer.HeightChunks}, ChunkSize={_cellLayer.ChunkSize}");
                    }
                }
                else
                {
                    Debug.LogError($"[MapStorage] CRITICAL: MapStorage initialization completed but not ready for terrain rendering");
                    Debug.LogError($"[MapStorage] This indicates a fundamental problem - terrain rendering will fail");
                    Debug.LogError($"[MapStorage] Ready state: IsReady={IsReady}, IsInitialized={IsInitialized()}, _cellLayer={(_cellLayer != null ? "not null" : "NULL")}");

                    // CRITICAL: If we got here, something is wrong with our initialization
                    if (_cellLayer == null)
                    {
                        Debug.LogError("[MapStorage] CRITICAL: _cellLayer is NULL - WorldLayer creation failed");
                    }
                    else
                    {
                        Debug.LogError("[MapStorage] CRITICAL: _cellLayer exists but IsReady is false");
                        Debug.LogError($"[MapStorage] _cellLayer state: WidthChunks={_cellLayer.WidthChunks}, HeightChunks={_cellLayer.HeightChunks}");
                    }
                }
            }
            catch (System.IO.IOException ioEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: File I/O error during world initialization: {ioEx.Message}");
                Debug.LogError("[MapStorage] CRITICAL: File I/O errors prevent terrain rendering");

                TryCreateFallbackWorld(worldCodeName, width, height);
                if (!_isInitialized)
                {
                    _cellLayer?.Dispose();
                    _cellLayer = null;
                }
            }
            catch (System.ArgumentException argEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Invalid arguments for WorldLayer creation: {argEx.Message}");
                Debug.LogError($"[MapStorage] World: {worldCodeName}, Width: {width}, Height: {height}, WidthChunks: {(width + 32 - 1) / 32}, HeightChunks: {(height + 32 - 1) / 32}");
                Debug.LogError("[MapStorage] CRITICAL: Invalid arguments prevent terrain rendering");

                TryCreateWithDifferentChunkSize(worldCodeName, width, height);
                if (!_isInitialized)
                {
                    _cellLayer?.Dispose();
                    _cellLayer = null;
                }
            }
            catch (System.OutOfMemoryException memEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Out of memory while creating WorldLayer for '{worldCodeName}': {memEx.Message}");
                Debug.LogError($"[MapStorage] Requested memory for {width}x{height} world may be too large");
                Debug.LogError("[MapStorage] CRITICAL: Memory issues prevent terrain rendering");

                TryCreateSmallerTestWorld(worldCodeName);
                if (!_isInitialized)
                {
                    _cellLayer?.Dispose();
                    _cellLayer = null;
                }
            }
            catch (System.UnauthorizedAccessException authEx)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Access denied while creating WorldLayer for '{worldCodeName}': {authEx.Message}");
                Debug.LogError($"[MapStorage] Check file permissions for path: {path}");
                Debug.LogError("[MapStorage] CRITICAL: Permission issues prevent terrain rendering");

                TryCreateFallbackWorld(worldCodeName, width, height);
                if (!_isInitialized)
                {
                    _cellLayer?.Dispose();
                    _cellLayer = null;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] CRITICAL: Unexpected error while initializing world '{worldCodeName}': {ex.Message}");
                Debug.LogError($"[MapStorage] Exception type: {ex.GetType().Name}");
                Debug.LogError($"[MapStorage] Stack trace: {ex.StackTrace}");
                Debug.LogError("[MapStorage] CRITICAL: Unknown error prevents terrain rendering");

                TryEmergencyFallback(worldCodeName, width, height);
                if (!_isInitialized)
                {
                    _cellLayer?.Dispose();
                    _cellLayer = null;
                }
            }
        }

        public bool IsInitialized() => _isInitialized;

        public string GetWorldCodeName() => _worldCodeName;

        /// <summary>
        /// Get a cell at the specified coordinates
        /// </summary>
        /// <param name="x">World X coordinate</param>
        /// <param name="y">World Y coordinate</param>
        /// <returns>CellType or CellType.Unloaded if not ready</returns>
        public CellType GetCell(int x, int y)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                return CellType.Unloaded;
            }

            try
            {
                // Optimization: Use touchLru=true for high-frequency access (rendering, gizmos)
                return _cellLayer.GetCell(x, y, touchLru: true);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error accessing cell at ({x}, {y}): {ex.Message}");
                return CellType.Unloaded;
            }
        }

        /// <summary>
        /// Set a cell at the specified coordinates
        /// </summary>
        /// <param name="x">World X coordinate</param>
        /// <param name="y">World Y coordinate</param>
        /// <param name="cellType">Cell type to set</param>
        public void SetCell(int x, int y, CellType cellType)
        {
            if (!_isInitialized || _cellLayer == null)
            {
                Debug.LogWarning($"Cannot set cell at ({x}, {y}) - MapStorage not initialized");
                return;
            }

            try
            {
                _cellLayer[x, y] = cellType;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error setting cell at ({x}, {y}) to {cellType}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _cellLayer?.Dispose();
                _cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                Debug.Log("MapStorage disposed successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error disposing MapStorage: {ex.Message}");
            }
        }

        private void TryCreateFallbackWorld(string worldCodeName, int width, int height)
        {
            try
            {
                string fallbackPath = $"{Application.temporaryCachePath}/{worldCodeName}_cells_fallback.mapb";
                Debug.LogWarning($"[MapStorage] Attempting to create fallback world at: {fallbackPath}");

                const int chunkSize = 32;
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;

                _cellLayer = new WorldLayer<CellType>(fallbackPath, widthChunks, heightChunks, chunkSize);
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
            try
            {
                // Try with smaller chunk size
                const int chunkSize = 16;
                Debug.LogWarning($"[MapStorage] Attempting to create world with chunk size {chunkSize}");

                string path = $"{Application.persistentDataPath}/{worldCodeName}_cells.mapb";
                int widthChunks = (width + chunkSize - 1) / chunkSize;
                int heightChunks = (height + chunkSize - 1) / chunkSize;

                _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;

                Debug.Log($"[MapStorage] World created successfully with chunk size {chunkSize}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Creation with different chunk size failed: {ex.Message}");
            }
        }

        private void TryCreateSmallerTestWorld(string worldCodeName)
        {
            try
            {
                const int testWidth = 64;
                const int testHeight = 64;
                string path = $"{Application.temporaryCachePath}/small_test_world.mapb";
                Debug.LogWarning($"[MapStorage] Attempting to create small test world (64x64) at: {path}");

                const int chunkSize = 32;

                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;

                _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;

                Debug.Log($"[MapStorage] Smaller test world created: {testWidth}x{testHeight}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Small test world creation failed: {ex.Message}");
            }
        }

        private void TryEmergencyFallback(string worldCodeName, int width, int height)
        {
            try
            {
                // Create a minimal world in memory if possible, or just a very small file
                const int testWidth = 32;
                const int testHeight = 32;
                string path = $"{Application.temporaryCachePath}/emergency_fallback.mapb";

                const int chunkSize = 32;

                int widthChunks = testWidth / chunkSize;
                int heightChunks = testHeight / chunkSize;

                _cellLayer = new WorldLayer<CellType>(path, widthChunks, heightChunks, chunkSize);
                _isInitialized = true;
                _worldCodeName = "emergency_test";

                Debug.Log($"[MapStorage] Emergency fallback world created: {testWidth}x{testHeight}");
                Debug.Log("[MapStorage] This should allow terrain rendering to proceed");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapStorage] Emergency fallback failed: {ex.Message}");
            }
        }
    }
}
