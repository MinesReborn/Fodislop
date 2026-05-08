using System;
using UnityEngine;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Test script to verify that cell configuration from server is working correctly
    /// </summary>
    public class CellConfigurationTest : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("Test cell type to check")]
        [SerializeField] private CellType testCellType = CellType.Road;
        [Tooltip("Test global position for variation")]
        [SerializeField] private Vector2Int testPosition = new Vector2Int(10, 15);

        [Header("Debug Output")]
        [SerializeField] private bool enableDebugOutput = true;

        private void Start()
        {
            if (enableDebugOutput)
            {
                Debug.Log("[CellConfigurationTest] Starting cell configuration test...");
                TestCellConfiguration();
            }
        }

        private void TestCellConfiguration()
        {
            Debug.Log($"[CellConfigurationTest] Testing cell type: {testCellType}");
            
            // Test MapManager functionality
            if (MapManager.Instance == null)
            {
                Debug.LogError("[CellConfigurationTest] MapManager instance not found!");
                return;
            }

            // Test color retrieval
            var cellColor = MapManager.Instance.GetCellMinimapColor(testCellType);
            Debug.Log($"[CellConfigurationTest] Cell color for {testCellType}: RGBA({cellColor.r:F3}, {cellColor.g:F3}, {cellColor.b:F3}, {cellColor.a:F3})");

            // Test animation properties
            var frameHeight = MapManager.Instance.GetAnimationFrameHeight(testCellType);
            var animationSpeed = MapManager.Instance.GetAnimationSpeed(testCellType);
            var hasAnimation = MapManager.Instance.HasAnimation(testCellType);
            var frameOffset = MapManager.Instance.GetFrameOffset(testCellType);

            Debug.Log($"[CellConfigurationTest] Animation properties for {testCellType}:");
            Debug.Log($"  - Has Animation: {hasAnimation}");
            Debug.Log($"  - Frame Height: {frameHeight} pixels ({frameHeight / RenderingConstants.CELL_SIZE} tiles)");
            Debug.Log($"  - Animation Speed: {animationSpeed} fps");
            Debug.Log($"  - Frame Offset: {frameOffset}");

            // Test WorldTextureManager integration
            if (WorldTextureManager.Instance != null)
            {
                Debug.Log("[CellConfigurationTest] Testing WorldTextureManager integration...");
                TestTextureManagerIntegration();
            }
            else
            {
                Debug.LogWarning("[CellConfigurationTest] WorldTextureManager not found, skipping texture tests");
            }
        }

        private async void TestTextureManagerIntegration()
        {
            try
            {
                var coordinate = await WorldTextureManager.Instance.GetCellTextureCoordinate(
                    testCellType, testPosition.x, testPosition.y);

                Debug.Log($"[CellConfigurationTest] Texture coordinate for {testCellType} at ({testPosition.x}, {testPosition.y}):");
                Debug.Log($"  - AtlasX: {coordinate.AtlasX}, AtlasY: {coordinate.AtlasY}");
                Debug.Log($"  - Width: {coordinate.Width}, Height: {coordinate.Height}");
                Debug.Log($"  - UV Rect: {coordinate.UVRect}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CellConfigurationTest] Error testing texture manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Test ARGB color conversion manually
        /// </summary>
        public void TestColorConversion()
        {
            // Test with a sample ARGB color (Red: 255, Green: 128, Blue: 64, Alpha: 255)
            uint testARGB = 0xFFFF8040; // ARGB format
            
            byte a = (byte)((testARGB >> 24) & 0xFF);
            byte r = (byte)((testARGB >> 16) & 0xFF);
            byte g = (byte)((testARGB >> 8) & 0xFF);
            byte b = (byte)(testARGB & 0xFF);
            
            Color convertedColor = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            
            Debug.Log($"[CellConfigurationTest] Color conversion test:");
            Debug.Log($"  - Original ARGB: 0x{testARGB:X8}");
            Debug.Log($"  - Extracted RGBA: ({r}, {g}, {b}, {a})");
            Debug.Log($"  - Converted Color: RGBA({convertedColor.r:F3}, {convertedColor.g:F3}, {convertedColor.b:F3}, {convertedColor.a:F3})");
        }

        private void Update()
        {
            // Optional: Test real-time updates if needed
        }
    }
}