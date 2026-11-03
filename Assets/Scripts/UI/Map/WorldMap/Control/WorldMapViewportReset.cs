#nullable enable

using Kern.Core.Interfaces;
using Kern.World;
using UnityEngine;

namespace Kern.UI.Map;

internal static class WorldMapViewportReset
{
    public static bool RequiresMip(
        MapManager manager,
        MapTextureController textureController,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY) =>
        MapViewportChunkBudget.ShouldUseMip(
            manager.WorldWidth,
            manager.WorldHeight,
            textureController.TexWidth,
            textureController.TexHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);

    public static void Clamp(
        WorldMapBounds bounds,
        ref float viewCenterX,
        ref float viewCenterY,
        float cellsPerPixel,
        int textureWidth,
        int textureHeight) =>
        bounds.Clamp(
            ref viewCenterX,
            ref viewCenterY,
            cellsPerPixel,
            textureWidth,
            textureHeight);

    public static void Apply(
        WorldMapBounds bounds,
        MapManager manager,
        MapViewportRenderer viewportRenderer,
        WorldMapLayerBinding layerBinding,
        IWorldDataStorage storage,
        MapTextureController textureController,
        MapPlayerTracker playerTracker,
        ref float cellsPerPixel,
        ref float maxCellsPerPixel,
        ref float viewCenterX,
        ref float viewCenterY,
        ref bool renderRequested)
    {
        bounds.Bind(manager, manager.WorldWidth, manager.WorldHeight);
        viewportRenderer.InitColorTable(manager);
        viewportRenderer.InvalidateViewState();
        layerBinding.BindCellLayer(storage.CellLayer);
        layerBinding.BindMipScan(manager.WorldWidth, manager.WorldHeight, viewportRenderer.CellColorTable);
        cellsPerPixel = 1f;
        maxCellsPerPixel = bounds.ComputeMaxZoomOut(textureController.TexWidth, textureController.TexHeight);
        cellsPerPixel = Mathf.Min(cellsPerPixel, maxCellsPerPixel);

        ILocalPlayer? player = playerTracker.CurrentPlayer;
        if (player is { HasServerPosition: true })
        {
            viewCenterX = player.Position.x;
            viewCenterY = player.Position.y;
        }
        else
        {
            viewCenterX = bounds.Width * 0.5f;
            viewCenterY = bounds.Height * 0.5f;
        }

        renderRequested = true;
    }
}
