#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>Owns world-mutation side effects and requested terrain revision changes.</summary>
    internal sealed class TerrainWorldChangeHandler
    {
        private readonly TerrainWindow _window;
        private readonly IFrameTelemetry _telemetry;
        private readonly TerrainContentRevisionJournal _contentRevision = new();
        private readonly TerrainTexturePrefetch _texturePrefetch = new();
        private readonly Func<IWorldDataStorage?> _getStorage;
        private readonly Func<MapManager?> _getMapManager;
        private readonly Func<ITextureService?> _getTextureService;
        private readonly Func<Shader?> _getTerrainShader;
        private readonly Func<TerrainLightingFramePublisher> _getLightingFramePublisher;

        public TerrainWorldChangeHandler(
            TerrainWindow window,
            IFrameTelemetry telemetry,
            Func<IWorldDataStorage?> getStorage,
            Func<MapManager?> getMapManager,
            Func<ITextureService?> getTextureService,
            Func<Shader?> getTerrainShader,
            Func<TerrainLightingFramePublisher> getLightingFramePublisher)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _getStorage = getStorage ?? throw new ArgumentNullException(nameof(getStorage));
            _getMapManager = getMapManager ?? throw new ArgumentNullException(nameof(getMapManager));
            _getTextureService = getTextureService ?? throw new ArgumentNullException(nameof(getTextureService));
            _getTerrainShader = getTerrainShader ?? throw new ArgumentNullException(nameof(getTerrainShader));
            _getLightingFramePublisher = getLightingFramePublisher ??
                throw new ArgumentNullException(nameof(getLightingFramePublisher));
        }

        public ulong RequestedContentRevision => _contentRevision.RequestedRevision;

        public ulong RecordConfigurationChange()
        {
            ulong revision = _contentRevision.RecordConfigurationChange();
            _getLightingFramePublisher().RequestFullReset(
                TerrainLightingFullResetReason.TerrainConfigurationChanged);
            return revision;
        }

        public void HandleCellChanged(int serverX, int serverY) =>
            HandleRegionChanged(serverX, serverY, 1, 1);

        public void HandleRegionChanged(int serverX, int serverY, int width, int height)
        {
            MapManager? mapManager = _getMapManager();
            if (mapManager == null)
            {
                _window.NeedsRefresh = true;
                _contentRevision.RecordUnboundedGeometryChange();
                _getLightingFramePublisher().RequestFullReset(
                    TerrainLightingFullResetReason.UnboundedGeometryChange);
                return;
            }

            _window.RecordWorldChange(serverX, serverY, width, height, mapManager.WorldHeight);
            _contentRevision.RecordBoundedRegionChange();
        }

        public void HandleTextureLoaded(string filename, Texture2D texture)
        {
            if (TerrainCellTextureName.TryParseCellType(filename, out CellType cellType))
            {
                _window.Driver.Presentation.SetTerrainShader(_getTerrainShader());
                _window.Driver.Presentation.InitializeShader();
                _window.PendingTextureCellTypes.Add(cellType);
                _contentRevision.RecordLightingVisibleTextureChange();
                _getLightingFramePublisher().RequestFullReset(
                    TerrainLightingFullResetReason.LightingVisibleTextureChanged);
            }
            else if (TerrainCellTextureName.IsDecalAtlas(filename))
            {
                ITextureService? textureService = _getTextureService();
                if (textureService != null)
                {
                    _window.Driver.Presentation.BindAtlasTextures(
                        textureService.GetAllAtlases(), textureService);
                }

                _window.NeedsRefresh = true;
            }
        }

        public void HandleWorldDataLoaded(Action ensureSubscriptions)
        {
            ensureSubscriptions();
            _window.InvalidateWorld();
            _contentRevision.RecordWorldLoad();
            BeginWorldGeneration();
        }

        public void HandleChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _telemetry.TerrainChunkLoadCount++;
            IWorldDataStorage? storage = _getStorage();
            ITextureService? textureService = _getTextureService();
            MapManager? mapManager = _getMapManager();
            if (storage != null && textureService != null && mapManager != null &&
                storage.CellLayer is { } layer &&
                _window.IsNearBuildWindow(
                    TerrainDirtyTracker.ToUnityRect(serverX, serverY, width, height, mapManager.WorldHeight),
                    layer.ChunkSize))
            {
                _texturePrefetch.PrefetchRegion(storage, textureService, serverX, serverY, width, height);
            }

            HandleRegionChanged(serverX, serverY, width, height);
        }

        public void BeginWorldGeneration() => _getLightingFramePublisher().BeginWorldGeneration();
    }
}
