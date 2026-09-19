#nullable enable

using System;
using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.Player;
using Kern.Player.Logic;
using Kern.Rendering;
using Kern.World;
using Kern.World.Lighting;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Kern.Tools.Imgui.Windows;

internal static class DebugOverlayTextFormatter
{
    public static void FormatLeftColumn(
        StringBuilder sb,
        ILocalPlayer? player,
        MapManager? mapManager,
        IWorldDataStorage? storage,
        IGameplayCamera? gameplayCamera)
    {
        sb.Clear();
        sb.Append("<b>Игрок</b>\n");

        if (player != null && player.HasServerPosition)
        {
            Vector3 unityPos = player.transform.position;
            int chunkX = player.Position.x / ProjectRuntimeContracts.World.ChunkSize;
            int chunkY = player.Position.y / ProjectRuntimeContracts.World.ChunkSize;
            int inChunkX = player.Position.x % ProjectRuntimeContracts.World.ChunkSize;
            int inChunkY = player.Position.y % ProjectRuntimeContracts.World.ChunkSize;

            sb.Append("Сервер: ").Append(player.Position.x).Append(", ").Append(player.Position.y)
              .Append("  ·  Unity: ").Append(unityPos.x.ToString("F2")).Append(", ")
              .Append(unityPos.y.ToString("F2")).Append("\n")
              .Append("Чанк: ").Append(chunkX).Append(", ").Append(chunkY)
              .Append("  ·  Клетка: ").Append(inChunkX).Append(", ").Append(inChunkY).Append("\n")
              .Append("Направление: ").Append(player.LastDirection)
              .Append("  ·  Автокопка: ").Append(player.AutoDig ? "да" : "нет")
              .Append("  ·  Агрессия: ").Append(player.Aggression ? "да" : "нет").Append("\n");
        }
        else
        {
            sb.Append("Ожидание серверной позиции…\n");
        }

        sb.Append("\n<b>Мир</b>\n");
        if (mapManager != null && mapManager.IsWorldInitialized)
        {
            sb.Append(mapManager.WorldWidth).Append(" × ").Append(mapManager.WorldHeight)
              .Append("  ·  чанки ")
              .Append(mapManager.WorldWidth / ProjectRuntimeContracts.World.ChunkSize).Append(" × ")
              .Append(mapManager.WorldHeight / ProjectRuntimeContracts.World.ChunkSize)
              .Append("  ·  ").Append(mapManager.WorldCodeName).Append("\n");
        }
        else
        {
            sb.Append("Ожидание инициализации мира…\n");
        }

        Camera? cam = gameplayCamera?.Camera;
        if (cam != null && Mouse.current != null && mapManager != null && mapManager.IsWorldInitialized)
        {
            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
            if (worldPos.y >= 0f && worldPos.y < mapManager.WorldHeight && worldPos.x >= 0f && worldPos.x < mapManager.WorldWidth)
            {
                Vector2Int cell = CoordinateUtils.UnityToServerPos(worldPos, mapManager.WorldHeight);
                if (storage?.CellLayer != null &&
                    storage.CellLayer.TryGetCell(cell.x, cell.y, out CellType cellType))
                {
                    if (cellType == CellType.Unloaded)
                    {
                        sb.Append("\n<b>Клетка под курсором  ·  ")
                          .Append(cell.x).Append(", ").Append(cell.y).Append("</b>\n")
                          .Append("kern:unloaded (#0)\n")
                          .Append("Проходимая: нет  ·  Разрушаемая: нет\n");
                    }
                    else
                    {
                        var config = mapManager.GetCellConfig(cellType);
                        bool passable = cellType == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
                        bool breakable = ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Breakable);

                        sb.Append("\n<b>Клетка под курсором  ·  ")
                          .Append(cell.x).Append(", ").Append(cell.y).Append("</b>\n")
                          .Append("kern:").Append(cellType.ToString().ToLowerInvariant()).Append(" (#").Append((int)cellType).Append(")\n")
                          .Append("Проходимая: ").Append(passable ? "да" : "нет")
                          .Append("  ·  Разрушаемая: ").Append(breakable ? "да" : "нет")
                          .Append("  ·  Рельеф: ").Append(config.ReliefGroup).Append("\n");
                    }
                }
            }
        }
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
        sb.Append("<b>Видеосистема</b>\n")
          .Append(SystemInfo.graphicsDeviceName).Append("  ·  ").Append(SystemInfo.graphicsDeviceType).Append("\n")
          .Append(Screen.width).Append(" × ").Append(Screen.height).Append("  ·  ")
          .Append(Screen.currentResolution.refreshRateRatio.value.ToString("F0")).Append(" Гц\n\n");

        HDROutput.AppendDebugInfo(
            sb,
            gameplayCamera?.Camera);

        sb.Append("Editor=").Append(Application.isEditor).Append("\n\n");

        long totalMemMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
        long totalAllocMb = Profiler.GetMonoHeapSizeLong() / (1024 * 1024);
        long totalReservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        float gcAllocKb = telemetry.GcAllocPerFrameBytes / 1024f;
        float gcAllocPerSecMb = telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f);

        sb.Append("<b>Память</b>\n")
          .Append("Mono: ").Append((totalMemMb * 100) / Math.Max(1, totalAllocMb)).Append("%  ·  ")
          .Append(totalMemMb).Append(" / ").Append(totalAllocMb).Append(" МБ")
          .Append("  ·  резерв ").Append(totalReservedMb).Append(" МБ\n")
          .Append("Аллокации: ").Append(gcAllocKb.ToString("F1")).Append(" КБ/кадр  ·  ")
          .Append(gcAllocPerSecMb.ToString("F2")).Append(" МБ/с  ·  GC ")
          .Append(telemetry.GcCollectionCount).Append("\n\n");

        sb.Append("<b>Террейн</b>\n")
          .Append("Окно: ").Append(StreamingPlanLabel(telemetry.StreamingPlanKind))
          .Append(" @ ").Append(telemetry.StreamingWindowOriginX).Append(",")
          .Append(telemetry.StreamingWindowOriginY).Append(" ")
          .Append(telemetry.StreamingWindowWidth).Append("×")
          .Append(telemetry.StreamingWindowHeight)
          .Append("  Δ").Append(telemetry.StreamingDeltaX).Append(",")
          .Append(telemetry.StreamingDeltaY).Append("\n")
          .Append("Меш: ").Append(telemetry.TerrainMeshTimeMs.ToString("F2")).Append(" мс  ·  заливка: ")
          .Append(telemetry.TerrainFloodFillTimeMs.ToString("F2")).Append(" мс\n")
          .Append("Кэш: ").Append(telemetry.TerrainCacheTimeMs.ToString("F2")).Append(" мс  ·  GPU: ")
          .Append(telemetry.TerrainGpuUploadTimeMs.ToString("F2")).Append(" мс\n")
          .Append("Перестроения: ").Append(telemetry.TerrainRebuildCount)
          .Append("  ·  патчи: ").Append(telemetry.TerrainDirtyPatchCount).Append("\n\n");

        string lightPassState = !debugSettings.BypassLightingCompute ? "работает" : "обход";
        string terrainDrawState = !debugSettings.BypassTerrainDraw ? "работает" : "обход";
        string cpuMeshState = !debugSettings.BypassCpuMeshRebuild ? "работает" : "обход";

        sb.Append("<b>Radiance Cascades</b>\n")
          .Append("Решения: ").Append(solvesPerSecond.ToString("F1")).Append("/с  ·  источники: ")
          .Append(lighting != null ? lighting.UploadedDynamicLightCount : 0).Append("\n")
          .Append("Подготовка: ").Append(telemetry.LightingBuildCommandsTimeMs.ToString("F2"))
          .Append(" мс  ·  выполнение: ")
          .Append(telemetry.LightingExecuteCommandsTimeMs.ToString("F2")).Append(" мс\n")
          .Append("Статические: ").Append(telemetry.LightingStaticSolveCount)
          .Append("  ·  динамические: ").Append(telemetry.LightingDynamicSolveCount)
          .Append("  ·  инвалид.: ").Append(telemetry.LightingRegionInvalidationCount).Append("\n")
          .Append("Стоимость cascade: ").Append(telemetry.LightingEstimatedCascadeRayWorkUnits)
          .Append(" шагов  ·  dispatch: ").Append(telemetry.LightingEstimatedCascadeDispatchThreads)
          .Append(" потоков\n")
          .Append("Источников перетрассировано: ").Append(telemetry.LightingDynamicTraceCount)
          .Append("  ·  в среднем за решение: ")
          .Append(telemetry.LightingDynamicSolveCount > 0
              ? (telemetry.LightingDynamicTraceCount / (float)telemetry.LightingDynamicSolveCount).ToString("F1")
              : "—")
          .Append("\n\n");

        // Постпроцесс из этого списка изъят намеренно: его нельзя
        // выключить ничем. Без тонмапа света срезаются в плоский белый,
        // то есть «выключенный» кадр не проще, а неверен.
        sb.Append("<b>Подсистемы</b>\n")
          .Append("Свет: ").Append(lightPassState)
          .Append("  ·  террейн: ").Append(terrainDrawState)
          .Append("  ·  меш: ").Append(cpuMeshState);
    }

    private static string StreamingPlanLabel(int kind) => kind switch
    {
        0 => "Keep",
        1 => "ScrollTerrain",
        2 => "BuildPrefetch",
        3 => "SwapPrefetch",
        4 => "FullRebuild",
        5 => "Resize",
        _ => "Unknown",
    };
}
