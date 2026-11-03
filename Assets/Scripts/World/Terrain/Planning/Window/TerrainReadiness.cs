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
            window.CellIdMesh != null &&
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
            IAssetLoader assetLoader,
            IWorldPresentationState presentationState)
        {
            ulong transitionId = presentationState.Current.TransitionId;
            bool ready =
                !window.HasCPUBuildInFlight &&
                !window.NeedsRefresh &&
                window.PendingTextureCellTypes.Count == 0 &&
                !window.HasUnpublishedTextureRefresh &&
                textureService.PendingCellTextureRequests == 0 &&
                assetLoader.PendingAssetCount == 0 &&
                assetLoader.QueuedAssetCount == 0 &&
                (window.HeldOrigin != null || window.CellsCommitted);
            if (!ready)
            {
                presentationState.ClearTeleportReady(transitionId);
                return;
            }

            const int PresentationMarginCells = 4;
            Vector2Int origin = window.ProspectiveOrigin;
            presentationState.MarkTeleportReady(transitionId, new RectInt(
                origin.x + PresentationMarginCells,
                origin.y + PresentationMarginCells,
                window.Width - (PresentationMarginCells * 2),
                window.Height - (PresentationMarginCells * 2)));
        }
    }
}
