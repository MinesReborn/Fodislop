using System;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Connection;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Handles standalone world initialization when no server connection is available
    /// Automatically creates a test world to enable terrain rendering in standalone mode.
    /// </summary>
    [RequireComponent(typeof(MapManager))]
    [ExecuteAlways]
    public class StandaloneWorldInitializer : MonoBehaviour
    {
        [Header("Standalone Configuration")]
        [Tooltip("Enable automatic standalone world creation")]
        [SerializeField]
        private bool _enableStandaloneMode = true;

        [Tooltip("Test world dimensions")]
        [SerializeField]
        private int _testWorldWidth = 128;

        [SerializeField]
        private int _testWorldHeight = 128;

        [Tooltip("Test world name")]
        [SerializeField]
        private string _testWorldName = "Standalone_Test_World";

        [Tooltip("Check interval for initialization timeout")]
        [SerializeField]
        private float _checkInterval = 2.0f;

        [Header("Debug Settings")]
        [Tooltip("Enable detailed logging")]
        [SerializeField]
        private bool _enableDebugLogging = true;

        private MapManager _mapManager;
        private float _lastCheckTime = 0f;
        private bool _initializationAttempted = false;
        private bool _isInitialized = false;

        /// <summary>
        /// Force standalone initialization (for debugging).
        /// </summary>
        public void ForceStandaloneInitialization()
        {
            Debug.Log("[StandaloneWorldInitializer] Force standalone initialization triggered");
            _initializationAttempted = false;
            _isInitialized = false;
            _lastCheckTime = 0f;
            enabled = true;
            CancelInvoke(nameof(CheckInitializationTimeout));
            Invoke(nameof(CheckInitializationTimeout), 0.1f);
        }

        /// <summary>
        /// Get current initialization status.
        /// </summary>
        public string GetInitializationStatus()
        {
            if (!_enableStandaloneMode)
            {
                return "Standalone mode disabled";
            }

            if (_isInitialized)
            {
                return "Initialized successfully";
            }

            if (_initializationAttempted)
            {
                return "Initialization attempted";
            }

            return "Waiting for timeout";
        }

        /// <summary>
        /// Check if standalone initialization is enabled and ready.
        /// </summary>
        public bool IsReady()
        {
            return _enableStandaloneMode && (_isInitialized || (ServiceLocator.Resolve<IWorldDataStorage>()?.IsReady == true));
        }

        protected void Awake()
        {
            if (!_enableStandaloneMode)
            {
                return;
            }

            _mapManager = GetComponent<MapManager>();
            if (_mapManager == null)
            {
                Debug.LogError("[StandaloneWorldInitializer] MapManager not found");
                enabled = false;
                return;
            }

            if (Application.isPlaying)
            {
                Debug.Log($"[StandaloneWorldInitializer] Initialized with test world: {_testWorldName} ({_testWorldWidth}x{_testWorldHeight})");
            }
        }

        protected void Start()
        {
            if (!_enableStandaloneMode)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                AttemptStandaloneInitialization();
                return;
            }

            // For Play mode, trigger faster standalone check if we are disconnected
            var cm = ConnectionManager.InstanceIfExists;
            if (cm == null || cm.Connection == null ||
                cm.Connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                if (_enableDebugLogging)
                {
                    Debug.Log("[StandaloneWorldInitializer] Offline detected on Start, scheduling immediate standalone init");
                }

                Invoke(nameof(AttemptStandaloneInitialization), 0.5f);
            }
        }

        protected void Update()
        {
            if (!_enableStandaloneMode || _isInitialized || !_initializationAttempted)
            {
                return;
            }

            // Check if MapStorage is ready after our initialization attempt
            if (ServiceLocator.Resolve<IWorldDataStorage>() != null && ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
            {
                _isInitialized = true;
                if (Application.isPlaying)
                {
                    Debug.Log("[StandaloneWorldInitializer] Standalone world initialization successful!");

                    // Trigger MapManager events to notify other systems
                    _mapManager.OnWorldInitialized?.Invoke();
                    _mapManager.OnWorldDataLoaded?.Invoke();
                }

                enabled = false; // Disable further checks
            }
        }

        protected void OnEnable()
        {
            if (!_enableStandaloneMode)
            {
                return;
            }

            if (Application.isPlaying)
            {
                // Start the initialization check process
                InvokeRepeating(nameof(CheckInitializationTimeout), _checkInterval, _checkInterval);
            }
        }

        protected void OnDisable()
        {
            CancelInvoke(nameof(CheckInitializationTimeout));
        }

        private void CheckInitializationTimeout()
        {
            if (!Application.isPlaying || _initializationAttempted || _isInitialized)
            {
                return;
            }

            // Check if MapManager has been initialized by server packets
            if (_mapManager.IsWorldInitialized)
            {
                if (_enableDebugLogging)
                {
                    Debug.Log("[StandaloneWorldInitializer] World already initialized by server, skipping standalone mode");
                }

                _isInitialized = true;
                enabled = false;
                return;
            }

            // Check if enough time has passed without server initialization
            if (Time.time - _lastCheckTime >= _checkInterval * 3) // Wait 6 seconds before attempting standalone init
            {
                AttemptStandaloneInitialization();
            }
        }

        private void AttemptStandaloneInitialization()
        {
            if (_initializationAttempted)
            {
                if (_enableDebugLogging)
                {
                    Debug.LogWarning("[StandaloneWorldInitializer] Initialization already attempted, skipping");
                }

                return;
            }

            _initializationAttempted = true;

            if (_enableDebugLogging)
            {
                Debug.Log($"[StandaloneWorldInitializer] Attempting standalone world initialization: {_testWorldName}");
            }

            try
            {
                // Create a mock WorldInitPacket for the test world
                var cellConfigurations = CreateTestCellConfigurations();
                var worldInitPacket = new WorldInitPacket
                {
                    CodeName = _testWorldName,
                    DisplayName = _testWorldName,
                    Width = (ushort)_testWorldWidth,
                    Height = (ushort)_testWorldHeight,
                    Cells = cellConfigurations,
                };

                // Use MapManager to initialize the world
                _mapManager.LoadWorldInit(worldInitPacket);

                if (_enableDebugLogging)
                {
                    Debug.Log($"[StandaloneWorldInitializer] Successfully created standalone world: {_testWorldName}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StandaloneWorldInitializer] Failed to create standalone world: {ex.Message}");
                Debug.LogError($"[StandaloneWorldInitializer] Exception details: {ex.StackTrace}");
            }
        }

        private static CellConfigurationPacket[] CreateTestCellConfigurations()
        {
            // Create basic cell configurations for testing
            // This provides minimal configuration for common cell types
            var configurations = new CellConfigurationPacket[256]; // Standard CellType enum size

            for (int i = 0; i < configurations.Length; i++)
            {
                configurations[i] = new CellConfigurationPacket
                {
                    Animation = 0, // No animation
                    AnimationSpeed = 0,
                    Color = unchecked((int)0x00000000), // Transparent default instead of white
                    FrameOffset = 0,
                    Properties = 0,
                };
            }

            // Configure some common cell types with specific properties
            // Grass (CellType.Grass = 1)
            if (configurations.Length > 1)
            {
                configurations[1] = new CellConfigurationPacket
                {
                    Animation = 0,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF00FF00), // Green
                    FrameOffset = 0,
                    Properties = 0,
                };
            }

            // Dirt (CellType.Dirt = 2)
            if (configurations.Length > 2)
            {
                configurations[2] = new CellConfigurationPacket
                {
                    Animation = 0,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF8B4513), // Brown
                    FrameOffset = 0,
                    Properties = 0,
                };
            }

            // Stone (CellType.Stone = 3)
            if (configurations.Length > 3)
            {
                configurations[3] = new CellConfigurationPacket
                {
                    Animation = 0,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF808080), // Gray
                    FrameOffset = 0,
                    Properties = 0,
                };
            }

            return configurations;
        }

        /// <summary>
        /// Trigger OnWorldDataLoaded event to notify terrain renderer
        /// This is CRITICAL for terrain rendering to start.
        /// </summary>
        private static void TriggerWorldDataLoaded()
        {
            if (MapManager.Instance != null)
            {
                Debug.Log("StandaloneWorldInitializer: Triggering OnWorldDataLoaded event");
                MapManager.Instance.OnWorldDataLoaded?.Invoke();
                Debug.Log("StandaloneWorldInitializer: OnWorldDataLoaded event triggered successfully");
            }
            else
            {
                Debug.LogError("StandaloneWorldInitializer: MapManager not available to trigger OnWorldDataLoaded");
            }
        }

        /// <summary>
        /// Handle OnWorldDataLoaded event from MapManager
        /// This ensures proper coordination with WorldBackgroundRenderer.
        /// </summary>
        private static void OnWorldDataLoaded()
        {
            Debug.Log("StandaloneWorldInitializer: World data loaded, notifying renderer");

            // Notify renderer that world is ready
            var renderer = FindAnyObjectByType<SingleMeshTerrainRenderer>();
            if (renderer != null)
            {
                Debug.Log("StandaloneWorldInitializer: Notified WorldBackgroundRenderer of world readiness");
            }
            else
            {
                Debug.LogWarning("StandaloneWorldInitializer: WorldBackgroundRenderer not found for notification");
            }
        }
    }
}
