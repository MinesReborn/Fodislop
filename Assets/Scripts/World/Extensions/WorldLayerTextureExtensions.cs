using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using MinesServer.Data;
using Fodinae.Assets.Scripts.World;

namespace Fodinae.Assets.Scripts.World.Extensions
{
    /// <summary>
    /// Extension methods for WorldLayer to integrate with WorldTextureManager
    /// </summary>
    public static class WorldLayerTextureExtensions
    {
        /// <summary>
        /// Get texture coordinates for a world cell at the specified position
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        /// <param name="x">X coordinate in world space</param>
        /// <param name="y">Y coordinate in world space</param>
        /// <returns>Atlas coordinates for the cell texture</returns>
        public static async UniTask<AtlasCoordinate> GetCellTextureCoordinate(this WorldLayer<CellType> worldLayer, int x, int y)
        {
            var cellType = worldLayer[x, y];
            await WorldTextureManager.Instance.PreloadTexturesAsync(new[] { cellType });
            return WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, x, y);
        }

        /// <summary>
        /// Get texture coordinates for a rectangular region of the world
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        /// <param name="x">Starting X coordinate</param>
        /// <param name="y">Starting Y coordinate</param>
        /// <param name="width">Region width</param>
        /// <param name="height">Region height</param>
        /// <returns>Dictionary mapping positions to atlas coordinates</returns>
        public static async UniTask<Dictionary<Vector2Int, AtlasCoordinate>> GetRegionTextureCoordinates(
            this WorldLayer<CellType> worldLayer, 
            int x, int y, int width, int height)
        {
            var uniqueCellTypes = new HashSet<CellType>();
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    var cellType = worldLayer[xx, yy];
                    if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                        uniqueCellTypes.Add(cellType);
                }
            }

            await WorldTextureManager.Instance.PreloadTexturesAsync(uniqueCellTypes);

            var coordinates = new Dictionary<Vector2Int, AtlasCoordinate>();
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    var cellType = worldLayer[xx, yy];
                    coordinates[new Vector2Int(xx, yy)] = WorldTextureManager.Instance.GetCellTextureCoordinateSync(cellType, xx, yy);
                }
            }

            return coordinates;
        }

        /// <summary>
        /// Preload textures for a region to improve performance
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        /// <param name="x">Starting X coordinate</param>
        /// <param name="y">Starting Y coordinate</param>
        /// <param name="width">Region width</param>
        /// <param name="height">Region height</param>
        /// <returns>Task representing the preload operation</returns>
        public static async UniTask PreloadRegionTextures(
            this WorldLayer<CellType> worldLayer, 
            int x, int y, int width, int height)
        {
            var uniqueCellTypes = new HashSet<CellType>();
            
            // Collect unique cell types in the region
            for (int yy = y; yy < y + height; yy++)
            {
                for (int xx = x; xx < x + width; xx++)
                {
                    var cellType = worldLayer[xx, yy];
                    if (cellType != CellType.Unloaded && cellType != CellType.Pregener)
                    {
                        uniqueCellTypes.Add(cellType);
                    }
                }
            }

            await WorldTextureManager.Instance.PreloadTexturesAsync(uniqueCellTypes);
        }

        /// <summary>
        /// Get all active texture atlases from the WorldTextureManager
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        /// <returns>List of active texture atlases</returns>
        public static List<TextureAtlas> GetActiveAtlases(this WorldLayer<CellType> worldLayer)
        {
            return WorldTextureManager.Instance.GetAllAtlases();
        }

        /// <summary>
        /// Clear all cached textures and atlases
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        public static void ClearTextureCache(this WorldLayer<CellType> worldLayer)
        {
            WorldTextureManager.Instance.Clear();
        }

        /// <summary>
        /// Get texture cache statistics
        /// </summary>
        /// <param name="worldLayer">The world layer</param>
        /// <returns>Cache statistics string</returns>
        public static string GetTextureCacheStats(this WorldLayer<CellType> worldLayer)
        {
            // This would need to be implemented in WorldTextureManager
            // For now, return a placeholder
            return "Texture cache stats not available";
        }
    }
}