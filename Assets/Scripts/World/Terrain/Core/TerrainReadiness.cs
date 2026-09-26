#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>Defines when published terrain can be consumed by gameplay or view transitions.</summary>
    internal static class TerrainReadiness
    {
        public static bool IsReadyForGameplay(TerrainWindow window, ITextureService textureService) =>
            window.IsInitialized &&
            window.CellIDMesh != null &&
            window.CellsCommitted &&
            window.Driver.Presentation.HasMaterials &&
            window.PendingTextureCellTypes.Count == 0 &&
            !window.HasUnpublishedTextureRefresh &&
            textureService.PendingCellTextureRequests == 0;

        /// <summary>
        /// Готово ли место назначения: окно, которое окажется на экране после
        /// публикации, собрано, нового шага не идёт и текстуры его типов на
        /// месте. Готовая область сужена на запас меша показа — камера
        /// встанет только туда, где её кадр рисуется целиком.
        /// </summary>
        public static void PublishViewTransitionReadiness(
            TerrainWindow window,
            ITextureService textureService,
            Kern.World.Streaming.WorldViewTransition transition)
        {
            bool ready =
                !window.HasCpuBuildInFlight &&
                !window.NeedsRefresh &&
                window.PendingTextureCellTypes.Count == 0 &&
                !window.HasUnpublishedTextureRefresh &&
                textureService.PendingCellTextureRequests == 0 &&
                (window.HeldOrigin != null || window.CellsCommitted);
            if (!ready)
            {
                transition.ClearReady();
                return;
            }

            const int PresentationMarginCells = 4;
            Vector2Int origin = window.ProspectiveOrigin;
            transition.MarkReady(new RectInt(
                origin.x + PresentationMarginCells,
                origin.y + PresentationMarginCells,
                window.Width - (PresentationMarginCells * 2),
                window.Height - (PresentationMarginCells * 2)));
        }
    }
}
