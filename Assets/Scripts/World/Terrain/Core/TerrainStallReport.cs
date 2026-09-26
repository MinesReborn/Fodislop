#nullable enable

using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Решение кадра и его цена — то, что надо знать про провис.</summary>
///
/// Интервалы кадра делятся на независимые (план, размеры, процесс, выгрузка —
/// из них и считается «прочее») и вложенные в процесс (кэш, атласы). Фоновый
/// шаг не входит в кадр вовсе: его цифры — цена последнего опубликованного
/// шага на рабочем потоке, и в «прочее» они не вычитаются.
public readonly record struct TerrainStallFrame(
    bool Scrolled,
    Vector2Int ScrollDelta,
    Vector2Int Origin,
    Vector2Int Size,
    int DirtyRectCount,
    long DirtyArea,
    float ProcessMs,
    float UploadMs,
    int UploadRectCount,
    long UploadTexels,
    float StageMs,
    float StageCopyMs,
    float StageApplyMs,
    int UploadStrips,
    float PlanMs,
    float DimensionsMs,
    int ResidencyProbeCalls,
    int ResidencyChunkReads,
    int ResidencyCacheHits,
    int ResidencyLruTouches,
    TerrainStallBuildState State,
    TerrainWorkerCost Worker);

/// <summary>Состояние фоновой сборки в кадре отчёта.</summary>
public readonly record struct TerrainStallBuildState(TerrainBuildState Build, bool InFlight);

/// <summary>Чем был шаг фоновой сборки.</summary>
public enum TerrainBuildStepKind
{
    None,
    Full,
    Scroll,
    Patch,
    Textures,
}

/// <summary>Цена последнего опубликованного шага на рабочем потоке.</summary>
public readonly record struct TerrainWorkerCost(
    TerrainBuildStepKind Kind,
    float CacheMs,
    float PrecalculateMs,
    float FloodFillMs,
    float MeshMs,
    float ScrollMs,
    float WarmupMs,
    float FillMs,
    int FilledCells,
    float QuadMs,
    float PackMs,
    float ElapsedMs,
    float LatencyMs);

/// <summary>Сводные счётчики террейна на момент дорогого кадра.</summary>
public readonly record struct TerrainStallTotals(
    int FullPopulates,
    int Patches,
    int ChunkLoads,
    float CacheMs,
    float AtlasMs);

/// <summary>
/// Разбор кадра, в котором террейн съел больше бюджета, — для отчёта о провисе.
/// </summary>
///
/// Зачем это в игре, а не в бенчмарке. Бенчмарк меряет процессорные стадии на
/// синтетических пещерах и показывает доли миллисекунды. Провис при догрузке
/// чанка живёт там, где бенчмарка нет: чтение настоящего хранилища,
/// разрешение метаданных с дозаказом текстур, выгрузка девяти каналов на GPU.
/// Без разбора по стадиям прямо в кадре причина назначается догадкой, а
/// догадка уже один раз стоила шестикратного падения fps.
///
/// Раньше отчёт печатал свою строку <c>[TerrainStall]</c> со своим бюджетом и
/// интервалом, отдельно от <c>[FrameStall]</c>, и по логу нельзя было понять,
/// какая строка про какой провис. Теперь он — источник <see cref="FrameEventLog"/>:
/// последние дорогие кадры хранятся структурами, а текст пишется только когда
/// FrameStallMonitor печатает провис, и выходит в той же записи. Дорогой
/// каждый кадр террейн поэтому ничего не аллоцирует.
public sealed class TerrainStallReport : IFrameEventSource
{
    // Бюджет с запасом. Шаг конвейера на окне 256×160 стоит ~0.2 мс, полная
    // пересборка — единицы миллисекунд. Всё, что выше, — уже заметно глазу.
    private const float BudgetMs = 8f;

    // Отчёт о провисе смотрит на несколько кадров назад; больше не нужно.
    private const int Retained = 4;

    private readonly int[] _frames = new int[Retained];
    private readonly float[] _totalMs = new float[Retained];
    private readonly TerrainStallFrame[] _stallFrames = new TerrainStallFrame[Retained];
    private readonly TerrainStallTotals[] _totals = new TerrainStallTotals[Retained];
    private int _next;
    private int _count;

    public static long Begin() => System.Diagnostics.Stopwatch.GetTimestamp();

    public static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    public void Record(long startTimestamp, IFrameTelemetry telemetry, in TerrainStallFrame frame)
    {
        float totalMs = ElapsedMs(startTimestamp);
        if (totalMs < BudgetMs)
        {
            return;
        }

        _frames[_next] = Time.frameCount;
        _totalMs[_next] = totalMs;
        _stallFrames[_next] = frame;
        _totals[_next] = new TerrainStallTotals(
            telemetry.TerrainFullPopulateCount,
            telemetry.TerrainDirtyPatchCount,
            telemetry.TerrainChunkLoadCount,
            telemetry.TerrainCacheTimeMs,
            telemetry.TerrainAtlasUploadTimeMs);
        _next = (_next + 1) % Retained;
        _count = Mathf.Min(_count + 1, Retained);
    }

    public int AppendRange(StringBuilder text, int firstFrame, int lastFrame, int writtenBefore)
    {
        int written = 0;
        for (int offset = 0; offset < _count; offset++)
        {
            int index = (_next - _count + offset + Retained) % Retained;
            int frame = _frames[index];
            if (frame < firstFrame || frame > lastFrame)
            {
                continue;
            }

            FrameEventLog.AppendEntryPrefix(text, writtenBefore + written, frame, lastFrame)
                .Append(Describe(_totalMs[index], _stallFrames[index], _totals[index]));
            written++;
        }

        return written;
    }

    private static string Describe(float totalMs, in TerrainStallFrame frame, in TerrainStallTotals totals)
    {
        // «Прочее» — это разница между измеренным кадром и суммой
        // независимых интервалов. Кэш и атласы идут внутри процесса и второй
        // раз не вычитаются. Крупное «прочее» означает, что провис не в
        // перечисленных стадиях, и искать надо снаружи: в приёме пакета, в
        // хранилище, в освещении.
        TerrainWorkerCost worker = frame.Worker;
        float accounted = frame.PlanMs + frame.DimensionsMs + frame.ProcessMs + frame.UploadMs;
        return
            $"террейн {totalMs:F1} мс · окно {frame.Size.x}×{frame.Size.y} " +
            $"в ({frame.Origin.x},{frame.Origin.y}) · " +
            $"сборка {frame.State.Build}{(frame.State.InFlight ? " (идёт)" : string.Empty)} · " +
            $"план {frame.PlanMs:F1} · размеры {frame.DimensionsMs:F1} · " +
            $"резидентность {frame.ResidencyProbeCalls} проб (1 цель + поиск) / " +
            $"{frame.ResidencyChunkReads} уникальных статусов в поиске / " +
            $"{frame.ResidencyLruTouches} LRU touch / " +
            $"{frame.ResidencyCacheHits} попаданий · " +
            $"процесс {frame.ProcessMs:F1} (кэш {totals.CacheMs:F1} · атласы {totals.AtlasMs:F1}) · " +
            $"выгрузка {frame.UploadMs:F1} · прочее {totalMs - accounted:F1} · " +
            $"[выгрузка: {(frame.UploadRectCount == 0 ? "целиком" : frame.UploadRectCount + " прямоуг.")} " +
            $"{frame.UploadTexels} текселей · набивка {frame.StageMs:F1} " +
            $"(строки {frame.StageCopyMs:F1} · загрузка {frame.StageApplyMs:F1}) · " +
            $"полосок {frame.UploadStrips}] · " +
            $"заплаток {frame.DirtyRectCount} на {frame.DirtyArea} клеток · " +
            $"[фон, вне кадра: последний шаг " +
            $"{StepLabel(worker.Kind, frame.ScrollDelta)} · " +
            $"{worker.ElapsedMs:F1} мс на потоке, до показа {worker.LatencyMs:F1} мс · " +
            $"кэш {worker.CacheMs:F1} · предрасчёт {worker.PrecalculateMs:F1} · заливка фона {worker.FloodFillMs:F1} · тексели {worker.MeshMs:F1} " +
            $"(кольца {worker.ScrollMs:F1} · прогрев {worker.WarmupMs:F1} · заливка {worker.FillMs:F1} на {worker.FilledCells} клеток; " +
            $"сумма по потокам: квады {worker.QuadMs:F1} · упаковка {worker.PackMs:F1})] · " +
            $"всего: полных {totals.FullPopulates}, заплаток {totals.Patches}, чанков {totals.ChunkLoads}";
    }

    private static string StepLabel(TerrainBuildStepKind kind, Vector2Int delta) => kind switch
    {
        TerrainBuildStepKind.Full => "полная сборка",
        TerrainBuildStepKind.Scroll => $"сдвиг {delta.x},{delta.y}",
        TerrainBuildStepKind.Patch => "заплатки",
        TerrainBuildStepKind.Textures => "перечитывание текстур",
        _ => "нет",
    };
}
