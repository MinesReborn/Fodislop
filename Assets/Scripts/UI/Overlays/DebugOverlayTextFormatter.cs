#nullable enable

using System;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.World;
using Fodinae.World.Lighting;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Fodinae.UI;

internal static class DebugOverlayTextFormatter
{
    public static void FormatLeftColumn(
        StringBuilder sb,
        float currentFps,
        float currentFrameMs,
        ILocalPlayer? player,
        MapManager? mapManager,
        IWorldDataStorage? storage,
        IGameplayCamera? gameplayCamera)
    {
        sb.Clear();
        sb.Append("<b>Fodinae Client (Unity 6 / URP 2D)</b>\n")
          .Append(currentFps.ToString("F0")).Append(" fps (").Append(currentFrameMs.ToString("F1")).Append(" ms)\n\n");

        if (player != null && player.HasServerPosition)
        {
            Vector3 unityPos = player.transform.position;
            int chunkX = player.Position.x / ProjectRuntimeContracts.World.ChunkSize;
            int chunkY = player.Position.y / ProjectRuntimeContracts.World.ChunkSize;
            int inChunkX = player.Position.x % ProjectRuntimeContracts.World.ChunkSize;
            int inChunkY = player.Position.y % ProjectRuntimeContracts.World.ChunkSize;

            sb.Append("XYZ: ").Append(player.Position.x).Append(" / ").Append(player.Position.y).Append(" (Unity: ").Append(unityPos.x.ToString("F2")).Append(", ").Append(unityPos.y.ToString("F2")).Append(")\n")
              .Append("Block: ").Append(player.Position.x).Append(" ").Append(player.Position.y).Append(" [").Append(inChunkX).Append(" ").Append(inChunkY).Append(" in Chunk ").Append(chunkX).Append(" ").Append(chunkY).Append("]\n")
              .Append("Facing: ").Append(player.LastDirection).Append(" | AutoDig: ").Append(player.AutoDig ? "ON" : "OFF").Append(" | Aggression: ").Append(player.Aggression ? "ON" : "OFF").Append("\n");
        }
        else
        {
            sb.Append("XYZ: Waiting for server spawn...\n");
        }

        if (mapManager != null && mapManager.IsWorldInitialized)
        {
            sb.Append("World: ").Append(mapManager.WorldWidth).Append("x").Append(mapManager.WorldHeight)
              .Append(" [Chunks: ").Append(mapManager.WorldWidth / ProjectRuntimeContracts.World.ChunkSize).Append("x").Append(mapManager.WorldHeight / ProjectRuntimeContracts.World.ChunkSize).Append("] (").Append(mapManager.WorldCodeName).Append(")\n");
        }

        Camera? cam = gameplayCamera?.Camera;
        if (cam != null && Mouse.current != null && mapManager != null && mapManager.IsWorldInitialized)
        {
            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
            if (worldPos.y >= 0f && worldPos.y < mapManager.WorldHeight && worldPos.x >= 0f && worldPos.x < mapManager.WorldWidth)
            {
                Vector2Int cell = CoordinateUtils.UnityToServerPos(worldPos, mapManager.WorldHeight);
                if (storage?.CellLayer != null)
                {
                    CellType cellType = storage.CellLayer.GetCellSync(cell.x, cell.y);
                    var config = mapManager.GetCellConfig(cellType);
                    bool passable = cellType == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
                    bool breakable = ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Breakable);

                    sb.Append("\n<b>Targeted Block: ").Append(cell.x).Append(", ").Append(cell.y).Append("</b>\n")
                      .Append("fodinae:").Append(cellType.ToString().ToLowerInvariant()).Append(" (#").Append((int)cellType).Append(")\n")
                      .Append("passable: ").Append(passable ? "true" : "false")
                      .Append(" | breakable: ").Append(breakable ? "true" : "false")
                      .Append(" | relief: ").Append(config.ReliefGroup).Append("\n");
                }
            }
        }

        sb.Append("\n<b>[Channels: 1:Grid 2:Ents 3:Cursor]</b>");
    }

    public static void FormatRightColumn(
        StringBuilder sb,
        IFrameTelemetry telemetry,
        LightingEngine? lighting,
        IRuntimeDebugSettings debugSettings,
        IGameplayCamera? gameplayCamera,
        float solvesPerSecond)
    {
        sb.Clear();
        sb.Append("<b>").Append(SystemInfo.graphicsDeviceName).Append("</b>\n")
          .Append(SystemInfo.graphicsDeviceType).Append(" | ").Append(Screen.width).Append("x").Append(Screen.height).Append("@").Append(Screen.currentResolution.refreshRateRatio.value.ToString("F0")).Append("Hz\n\n");

        DisplayManager.HDROutput.AppendDebugInfo(
            sb,
            gameplayCamera?.Camera);

        long totalMemMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
        long totalAllocMb = Profiler.GetMonoHeapSizeLong() / (1024 * 1024);
        long totalReservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        float gcAllocKb = telemetry.GcAllocPerFrameBytes / 1024f;
        float gcAllocPerSecMb = telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f);

        sb.Append("Mem: ").Append((totalMemMb * 100) / Math.Max(1, totalAllocMb)).Append("% ").Append(totalMemMb).Append("/").Append(totalAllocMb).Append("MB (Res: ").Append(totalReservedMb).Append("MB)\n")
          .Append("Alloc: ").Append(gcAllocKb.ToString("F1")).Append("KB/f (").Append(gcAllocPerSecMb.ToString("F2")).Append("MB/s) | GC: ").Append(telemetry.GcCollectionCount).Append("\n\n");

        sb.Append("<b>[Terrain Engine]</b>\n")
          .Append("Mesh: ").Append(telemetry.TerrainMeshTimeMs.ToString("F2")).Append("ms | Flood: ").Append(telemetry.TerrainFloodFillTimeMs.ToString("F2")).Append("ms\n")
          .Append("Cache: ").Append(telemetry.TerrainCacheTimeMs.ToString("F2")).Append("ms | Upload: ").Append(telemetry.TerrainGpuUploadTimeMs.ToString("F2")).Append("ms\n")
          .Append("Rebuilds: ").Append(telemetry.TerrainRebuildCount).Append(" | Patches: ").Append(telemetry.TerrainDirtyPatchCount).Append("\n\n");

        string lightPassState = !debugSettings.BypassLightingCompute ? "ON" : "MUTE";
        string terrainDrawState = !debugSettings.BypassTerrainDraw ? "ON" : "MUTE";
        string cpuMeshState = !debugSettings.BypassCpuMeshRebuild ? "ON" : "MUTE";

        sb.Append("<b>[Radiance Cascades]</b>\n")
          .Append("Solves/s: ").Append(solvesPerSecond.ToString("F1")).Append(" | DynLights: ").Append(lighting != null ? lighting.UploadedDynamicLightCount : 0).Append("\n")
          .Append("RC Build: ").Append(telemetry.LightingBuildCommandsTimeMs.ToString("F2")).Append("ms | Exec: ").Append(telemetry.LightingExecuteCommandsTimeMs.ToString("F2")).Append("ms\n")
          .Append("Static: ").Append(telemetry.LightingStaticSolveCount).Append(" | Dyn: ").Append(telemetry.LightingDynamicSolveCount).Append(" | Inval: ").Append(telemetry.LightingRegionInvalidationCount).Append("\n\n");

        // Постпроцесс из этого списка изъят намеренно: его нельзя
        // выключить ничем. Без тонмапа света срезаются в плоский белый,
        // то есть «выключенный» кадр не проще, а неверен.
        sb.Append("<b>[Pass Toggles: 4:RC 6:Terr 7:Mesh 8:Dyn]</b>\n")
          .Append("RC: ").Append(lightPassState).Append(" | Terr: ").Append(terrainDrawState).Append(" | Mesh: ").Append(cpuMeshState);
    }
}
