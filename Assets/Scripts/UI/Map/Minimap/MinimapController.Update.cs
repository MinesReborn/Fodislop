#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MinimapRefreshLoop
{
    public void Process(
        UIDocument document,
        MinimapUiController? ui,
        MapModeState mapModeState,
        MapStorage mapStorage,
        IWorldLayer<CellType>? cellLayer,
        ILocalPlayer? player,
        int worldWidth,
        int worldHeight,
        bool ready,
        bool lastRefreshHadLoadedCells,
        MinimapCellInvalidation invalidation,
        MinimapRefreshPolicy refreshPolicy,
        MapCellSampler cellSampler,
        Action reinitializeWorldState,
        Action<int, int, bool> refreshTexture,
        Action<bool> setVisible)
    {
        bool hadUi = ui?.IsCreated == true;
        ui?.TryCreate();
        if (!hadUi && ui?.IsCreated == true && ready)
        {
            setVisible(!mapModeState.IsOpen);
        }

        if (document == null || !document.enabled)
        {
            return;
        }

        if (ready && !ReferenceEquals(cellLayer, mapStorage.CellLayer))
        {
            reinitializeWorldState();
            return;
        }

        if (ready && invalidation.IsPending && !mapModeState.IsOpen &&
            refreshPolicy.CanRefresh(Time.time))
        {
            bool hasServerPosition = player is { HasServerPosition: true };
            int centerX = hasServerPosition ? player!.Position.x : worldWidth / 2;
            int centerY = hasServerPosition ? player!.Position.y : worldHeight / 2;
            refreshTexture(centerX, centerY, hasServerPosition);
            refreshPolicy.RecordRefresh(Time.time, mapStorage.Revision, lastRefreshHadLoadedCells);
            invalidation.Clear();
        }

        if (player == null || !player.HasServerPosition)
        {
            return;
        }

        long currentRevision = mapStorage.Revision;
        if (!refreshPolicy.InitialRefreshDone && ready && refreshPolicy.CanRefresh(Time.time))
        {
            ui?.UpdateCoordinates(player.Position.x, player.Position.y);
            bool minimapVisible = !mapModeState.IsOpen;
            if (minimapVisible)
            {
                refreshTexture(player.Position.x, player.Position.y, true);
            }

            refreshPolicy.RecordInitialRefresh(
                Time.time,
                player.Position,
                currentRevision,
                minimapVisible,
                lastRefreshHadLoadedCells);
        }
        else if (refreshPolicy.ShouldRefreshOnStorageOrMove(
            Time.time,
            currentRevision,
            ready,
            !mapModeState.IsOpen,
            true))
        {
            cellSampler.Invalidate();
            refreshTexture(player.Position.x, player.Position.y, true);
            refreshPolicy.RecordRefresh(Time.time, currentRevision, lastRefreshHadLoadedCells);
        }
        else if (refreshPolicy.ShouldRefreshOnChunkLoad(
            Time.time,
            ready,
            !mapModeState.IsOpen,
            true))
        {
            refreshTexture(player.Position.x, player.Position.y, true);
            refreshPolicy.RecordChunkLoadRefresh(Time.time, currentRevision, lastRefreshHadLoadedCells);
        }
    }
}
