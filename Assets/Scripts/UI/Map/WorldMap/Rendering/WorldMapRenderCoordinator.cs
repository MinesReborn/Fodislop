#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.World;

namespace Kern.UI.Map;

internal sealed class WorldMapRenderCoordinator
{
    private readonly WorldMapPanel _panel;
    private readonly MapViewportRenderer _viewportRenderer;
    private readonly MapTextureController _textureController;
    private readonly WorldMapMipSource _mipSource;
    private readonly WorldMapLayerBinding _layerBinding;
    private readonly WorldMapBounds _bounds;
    private readonly MapCellSampler _cellSampler;
    private readonly WorldMapPathOverlay _pathOverlay;
    private readonly ILocalizationService _localization;
    private readonly Action<IWorldDataStorage> _resetWorldView;

    public WorldMapRenderCoordinator(
        WorldMapPanel panel,
        MapViewportRenderer viewportRenderer,
        MapTextureController textureController,
        WorldMapMipSource mipSource,
        WorldMapLayerBinding layerBinding,
        WorldMapBounds bounds,
        MapCellSampler cellSampler,
        WorldMapPathOverlay pathOverlay,
        ILocalizationService localization,
        Action<IWorldDataStorage> resetWorldView)
    {
        _panel = panel;
        _viewportRenderer = viewportRenderer;
        _textureController = textureController;
        _mipSource = mipSource;
        _layerBinding = layerBinding;
        _bounds = bounds;
        _cellSampler = cellSampler;
        _pathOverlay = pathOverlay;
        _localization = localization;
        _resetWorldView = resetWorldView;
    }

    public void UpdateStatus(
        MapManager manager,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY)
    {
        bool requiresMip = WorldMapViewportReset.RequiresMip(
            manager,
            _textureController,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);
        UpdateMipStatus(requiresMip);
    }

    private void UpdateMipStatus(bool requiresMip) => _panel.UpdatePreparationStatus(
        _mipSource.IsReady,
        requiresMip,
        _mipSource.Failed,
        _mipSource.Progress,
        _mipSource.Total,
        _localization);

    public void Render(
        IWorldDataStorage storage,
        MapManager manager,
        IClickPathWalker? pathWalker,
        ILocalPlayer? player,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY,
        ref bool renderRequested)
    {
        if (!ReferenceEquals(_layerBinding.CellLayer, storage.CellLayer))
        {
            _layerBinding.BindCellLayer(storage.CellLayer);
            _layerBinding.BindMipScan(manager.WorldWidth, manager.WorldHeight, _viewportRenderer.CellColorTable);
            renderRequested = true;
        }

        if (!_bounds.Matches(manager))
        {
            _resetWorldView(storage);
            renderRequested = true;
        }

        if (!renderRequested)
        {
            return;
        }

        bool requiresMip = WorldMapViewportReset.RequiresMip(
            manager,
            _textureController,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);
        if (requiresMip && !_mipSource.IsReady && !_mipSource.Failed)
        {
            _mipSource.Begin();
        }

        UpdateMipStatus(requiresMip);
        _viewportRenderer.Render(
            _textureController.MapTexture,
            manager.WorldWidth,
            manager.WorldHeight,
            _cellSampler,
            _mipSource,
            _textureController.TexWidth,
            _textureController.TexHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY);
        _panel.Image?.MarkDirtyRepaint();
        renderRequested = false;
        _pathOverlay.Draw(
            pathWalker?.Path,
            pathWalker?.PathIndex ?? 0,
            player,
            viewCenterX,
            viewCenterY,
            cellsPerPixel);
    }
}
