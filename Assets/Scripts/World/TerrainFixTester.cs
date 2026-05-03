using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Assets.Scripts.World
{
    public class TerrainFixTester : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Enable automatic testing on start")]
        [SerializeField] private bool _autoTestOnStart = true;
        [Tooltip("Enable detailed logging")]
        [SerializeField] private bool _enableDetailedLogging = true;

        [Header("Test Actions")]
        [Tooltip("Force system reinitialization on start")]
        [SerializeField] private bool _forceReinitializeOnStart = false;
        [Tooltip("Create test world if no world data available")]
        [SerializeField] private bool _createTestWorldIfMissing = true;

        private WorldBackgroundRenderer _renderer;
        private TerrainDiagnosticRunner _diagnosticRunner;

        void Start()
        {
            if (_autoTestOnStart)
            {
                StartCoroutine(RunTerrainFixTest());
            }
        }

        public IEnumerator RunTerrainFixTest()
        {
            Debug.Log("=== TERRAIN FIX TESTER STARTED ===");
            Debug.Log("Testing terrain rendering fixes...");

            _renderer = FindObjectOfType<WorldBackgroundRenderer>();
            _diagnosticRunner = FindObjectOfType<TerrainDiagnosticRunner>();

            if (_renderer == null)
            {
                Debug.LogError("❌ WorldBackgroundRenderer not found in scene");
                yield break;
            }

            Debug.Log("✅ Found WorldBackgroundRenderer");

            if (_diagnosticRunner == null)
            {
                Debug.LogWarning("⚠ TerrainDiagnosticRunner not found - adding component");
                _diagnosticRunner = _renderer.gameObject.AddComponent<TerrainDiagnosticRunner>();
            }

            if (_forceReinitializeOnStart)
            {
                Debug.Log("🔄 Forcing system reinitialization...");
                _diagnosticRunner.ForceSystemReinitialize();
                yield return new WaitForSeconds(2f);
            }

            bool hasWorldData = CheckWorldData();

            if (!hasWorldData && _createTestWorldIfMissing)
            {
                Debug.Log("🌍 Creating test world since no world data available...");
                CreateTestWorld();
                yield return new WaitForSeconds(1f);
            }

            Debug.Log("🔍 Running terrain diagnostics...");
            yield return StartCoroutine(_diagnosticRunner.RunDiagnostics());

            Debug.Log("✅ Terrain fix test completed");
            Debug.Log("Check the diagnostic output above for any issues");
        }

        private bool CheckWorldData()
        {
            if (MapStorage.Instance == null || !MapStorage.Instance.IsReady)
            {
                return false;
            }

            try
            {
                var cell1 = MapStorage.Instance.GetCell(0, 0);
                var cell2 = MapStorage.Instance.GetCell(10, 10);

                return cell1 != CellType.Unloaded && cell1 != CellType.Pregener;
            }
            catch
            {
                return false;
            }
        }

        private void CreateTestWorld()
        {
            Debug.Log("Creating test world with dimensions 64x64...");

            if (MapManager.Instance != null)
            {
                var testPacket = new WorldInitPacket
                {
                    CodeName = "test_world",
                    DisplayName = "Test World",
                    Width = 64,
                    Height = 64,
                    Cells = new CellConfigurationPacket[256]
                };

                // Provide a default configuration for cell types to prevent nullrefs
                for (int i = 0; i < 256; i++)
                {
                    testPacket.Cells[i] = new CellConfigurationPacket { Color = unchecked((int)0xFFFFFFFF) };
                }

                MapManager.Instance.LoadWorldInit(testPacket);

                PopulateTestWorldData();

                Debug.Log("✅ Test world created successfully with populated cell data");
            }
            else
            {
                Debug.LogError("❌ MapManager not available to create test world");
            }
        }

        private void PopulateTestWorldData()
        {
            Debug.Log("Populating test world with terrain data...");


            if (MapStorage.Instance == null || MapStorage.Instance.cellLayer == null)
            {
                Debug.LogError("❌ MapStorage cellLayer is not available!");
                return;
            }

            int width = 64;
            int height = 64;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    CellType cellType;

                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    {
                        cellType = CellType.BuildingWall;
                    }
                    else if (x > 10 && x < width - 10 && y > 10 && y < height - 10)
                    {
                        if ((x + y) % 4 == 0)
                        {
                            cellType = CellType.Empty;
                        }
                        else if ((x + y) % 3 == 0)
                        {
                            cellType = CellType.FedBlock;
                        }
                        else
                        {
                            cellType = CellType.Empty;
                        }
                    }
                    else
                    {
                        cellType = CellType.FedBlock;
                    }

                    MapStorage.Instance.cellLayer[x, y] = cellType;
                }
            }

            Debug.Log($"✅ Populated {width*height} cells with test terrain data");
        }

        public void RunManualTest()
        {
            StartCoroutine(RunTerrainFixTest());
        }

        public void ForceDataVerification()
        {
            Debug.Log("🔍 Forcing immediate data verification...");

            if (MapStorage.Instance != null && MapStorage.Instance.IsReady)
            {
                var testCells = new[] { (0, 0), (10, 10), (30, 30), (50, 50) };

                Debug.Log("Testing cell data access:");
                foreach (var (x, y) in testCells)
                {
                    try
                    {
                        var cell = MapStorage.Instance.GetCell(x, y);
                        Debug.Log($"   Cell ({x},{y}): {cell}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"   Cell ({x},{y}): ERROR - {ex.Message}");
                    }
                }

                if (_renderer != null)
                {
                    Debug.Log("🔄 Forcing mesh regeneration...");
                    _renderer.ForceInitialization();
                }
            }
            else
            {
                Debug.LogWarning("MapStorage not ready for data verification");
            }
        }

        public void ForceReinitialize()
        {
            if (_diagnosticRunner != null)
            {
                _diagnosticRunner.ForceSystemReinitialize();
            }
            else
            {
                Debug.LogWarning("No diagnostic runner available for reinitialization");
            }
        }

        public string GetSystemStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine("=== TERRAIN SYSTEM STATUS ===");

            if (MapManager.Instance != null)
            {
                status.AppendLine($"MapManager: {MapManager.Instance.WorldDisplayName} ({MapManager.Instance.WorldCodeName})");
                status.AppendLine($"Dimensions: {MapManager.Instance.WorldWidth}x{MapManager.Instance.WorldHeight}");
            }
            else
            {
                status.AppendLine("MapManager: Not available");
            }

            if (MapStorage.Instance != null)
            {
                status.AppendLine($"MapStorage: Ready={MapStorage.Instance.IsReady}, Initialized={MapStorage.Instance.IsInitialized()}");
                status.AppendLine($"World: {MapStorage.Instance.GetWorldCodeName()}");
                status.AppendLine($"CellLayer: {(MapStorage.Instance.cellLayer != null ? "Available" : "NULL")}");
            }
            else
            {
                status.AppendLine("MapStorage: Not available");
            }
            return status.ToString();
        }

        private void OnValidate()
        {
            if (_forceReinitializeOnStart && !_autoTestOnStart)
            {
                Debug.LogWarning("ForceReinitializeOnStart is enabled but AutoTestOnStart is disabled");
            }
        }
    }
}