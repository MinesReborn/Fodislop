#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text.Json;

namespace Kern.TerrainBench;

// Один замер: имя, набор, размер сетки и распределение времени.
public sealed record BenchResult(
    string Suite,
    string Name,
    string Grid,
    int Iterations,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double MaxMs,
    double StdDevMs,
    long AllocatedBytesPerIteration,
    int Gen0Collections)
{
    public string Key => $"{Suite}/{Name}/{Grid}";

    public double CpuP50Ms { get; init; }

    public double CpuP95Ms { get; init; }

    public double CpuMaxMs { get; init; }

    public double CpuAtMaxWallMs { get; init; }
}

// Показатель качества, а не времени: доля выгрузки, размер, число расхождений.
public sealed record BenchMetric(string Suite, string Name, string Grid, double Value, string Unit)
{
    public string Key => $"{Suite}/{Name}/{Grid}";
}

public sealed class BenchOptions
{
    // Разница p50 меньше этого — шум таймера, а не регрессия.
    public double NoiseFloorMs { get; set; } = 0.01;

    public List<(int Width, int Height)> Sizes { get; } = [(192, 128)];

    public string? Filter { get; set; }

    public double TargetSeconds { get; set; } = 0.25;

    public int MinIterations { get; set; } = 10;

    public int MaxIterations { get; set; } = 20000;

    public string OutputDirectory { get; set; } = Path.Combine("Logs", "bench");

    public double RegressionThreshold { get; set; } = 0.10;

    public bool Quick { get; set; }

    public static BenchOptions Parse(string[] args)
    {
        var options = new BenchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sizes":
                    options.Sizes.Clear();
                    foreach (string size in args[++i].Split(','))
                    {
                        string[] parts = size.Split('x');
                        options.Sizes.Add((int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)));
                    }

                    break;
                case "--filter":
                    options.Filter = args[++i];
                    break;
                case "--seconds":
                    options.TargetSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--out":
                    options.OutputDirectory = args[++i];
                    break;
                case "--threshold":
                    options.RegressionThreshold = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--quick":
                    options.Quick = true;
                    options.TargetSeconds = 0.05;
                    options.MinIterations = 3;
                    break;
                default:
                    throw new ArgumentException($"Неизвестный аргумент '{args[i]}'.");
            }
        }

        return options;
    }
}

public sealed class BenchRunner(BenchOptions options)
{
    private readonly List<BenchResult> _results = [];
    private readonly List<BenchMetric> _metrics = [];

    public IReadOnlyList<BenchResult> Results => _results;

    public IReadOnlyList<BenchMetric> Metrics => _metrics;

    public void Metric(string name, double value, string unit)
    {
        if (!Wants(name))
        {
            return;
        }

        _metrics.Add(new BenchMetric(Suite, name, Grid, value, unit));
        Console.WriteLine($"  [метрика] {name,-60} {value,12:F3} {unit}");
    }

    // Результат из уже снятых выборок: сквозные сценарии меряют каждый шаг
    // сами, потому что шаги не одинаковы.
    public void Record(
        string name,
        List<double> samples,
        long allocatedBytes,
        int gen0,
        List<double>? cpuSamples = null)
    {
        if (!Wants(name) || samples.Count == 0)
        {
            return;
        }

        double[] sorted = samples.OrderBy(s => s).ToArray();
        double[]? sortedCpu = cpuSamples?.OrderBy(s => s).ToArray();
        double mean = sorted.Average();
        double variance = sorted.Sum(s => (s - mean) * (s - mean)) / sorted.Length;
        int slowestWallSample = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i] > samples[slowestWallSample])
            {
                slowestWallSample = i;
            }
        }

        var result = new BenchResult(
            Suite, name, Grid, sorted.Length, mean,
            Percentile(sorted, 0.50), Percentile(sorted, 0.95), sorted[^1],
            Math.Sqrt(variance), allocatedBytes / sorted.Length, gen0)
        {
            CpuP50Ms = sortedCpu == null ? 0 : Percentile(sortedCpu, 0.50),
            CpuP95Ms = sortedCpu == null ? 0 : Percentile(sortedCpu, 0.95),
            CpuMaxMs = sortedCpu == null ? 0 : sortedCpu[^1],
            CpuAtMaxWallMs = cpuSamples == null ? 0 : cpuSamples[slowestWallSample],
        };
        _results.Add(result);
        Console.WriteLine(
            $"  {name,-62} {mean,9:F4} мс  p50 {result.P50Ms,9:F4}  p95 {result.P95Ms,9:F4}  " +
            $"p99 {Percentile(sorted, 0.99),9:F4}  max {result.MaxMs,9:F3}  " +
            FormatCpu(result) +
            $"{result.AllocatedBytesPerIteration,8} Б  gen0 {gen0,3}  ×{sorted.Length}");
    }

    public string Grid { get; set; } = "";

    public string Suite { get; set; } = "";

    public bool Wants(string name)
    {
        if (options.Filter == null)
        {
            return true;
        }

        string target = $"{Suite}/{name}";
        foreach (string filter in options.Filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (target.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Прогон до целевого времени: дешёвые операции получают больше итераций,
    // дорогие — не меньше MinIterations. Первая итерация — прогрев JIT.
    public void Run(string name, Action action, Action? setup = null)
    {
        if (!Wants(name))
        {
            return;
        }

        setup?.Invoke();
        action();
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new List<double>();
        var cpuSamples = new List<double>();
        int slowestWallSample = 0;
        using Process currentProcess = Process.GetCurrentProcess();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        var budget = Stopwatch.StartNew();
        while (samples.Count < options.MaxIterations &&
            (samples.Count < options.MinIterations || budget.Elapsed.TotalSeconds < options.TargetSeconds))
        {
            setup?.Invoke();
            long cpuStart = currentProcess.TotalProcessorTime.Ticks;
            long start = Stopwatch.GetTimestamp();
            action();
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double cpuMs =
                (currentProcess.TotalProcessorTime.Ticks - cpuStart) /
                (double)TimeSpan.TicksPerMillisecond;
            if (samples.Count == 0 || elapsedMs > samples[slowestWallSample])
            {
                slowestWallSample = samples.Count;
            }

            samples.Add(elapsedMs);
            cpuSamples.Add(cpuMs);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        GCSettings.LatencyMode = GCLatencyMode.Interactive;

        double[] sorted = samples.OrderBy(s => s).ToArray();
        double[] sortedCpu = cpuSamples.OrderBy(s => s).ToArray();
        double mean = sorted.Average();
        double variance = sorted.Sum(s => (s - mean) * (s - mean)) / sorted.Length;
        var result = new BenchResult(
            Suite,
            name,
            Grid,
            sorted.Length,
            mean,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            sorted[^1],
            Math.Sqrt(variance),
            allocated / sorted.Length,
            gen0)
        {
            CpuP50Ms = Percentile(sortedCpu, 0.50),
            CpuP95Ms = Percentile(sortedCpu, 0.95),
            CpuMaxMs = sortedCpu[^1],
            CpuAtMaxWallMs = cpuSamples[slowestWallSample],
        };
        _results.Add(result);
        Console.WriteLine(
            $"  {name,-62} {mean,9:F4} мс  p50 {result.P50Ms,9:F4}  p95 {result.P95Ms,9:F4}  " +
            $"max {result.MaxMs,9:F3}  cpu p50 {result.CpuP50Ms,7:F3}  cpu p95 {result.CpuP95Ms,7:F3}  " +
            $"cpu max {result.CpuMaxMs,7:F3} / max-wall {result.CpuAtMaxWallMs,7:F3}  " +
            $"±{result.StdDevMs,7:F4}  {result.AllocatedBytesPerIteration,8} Б  gen0 {gen0,3}  ×{sorted.Length}");
    }

    public void RunAlternating(string firstName, Action firstAction, string secondName, Action secondAction)
    {
        if (!Wants(firstName) || !Wants(secondName))
        {
            return;
        }

        firstAction();
        secondAction();
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var firstWall = new List<double>();
        var secondWall = new List<double>();
        var firstCpu = new List<double>();
        var secondCpu = new List<double>();
        using Process currentProcess = Process.GetCurrentProcess();
        long firstAllocated = 0;
        long secondAllocated = 0;
        int gen0Before = GC.CollectionCount(0);
        var budget = Stopwatch.StartNew();
        bool firstOrder = true;
        while (firstWall.Count < options.MaxIterations &&
            secondWall.Count < options.MaxIterations &&
            (firstWall.Count < options.MinIterations || budget.Elapsed.TotalSeconds < options.TargetSeconds * 2))
        {
            if (firstOrder)
            {
                Measure(firstAction, firstWall, firstCpu, ref firstAllocated);
                Measure(secondAction, secondWall, secondCpu, ref secondAllocated);
            }
            else
            {
                Measure(secondAction, secondWall, secondCpu, ref secondAllocated);
                Measure(firstAction, firstWall, firstCpu, ref firstAllocated);
            }

            firstOrder = !firstOrder;
        }

        int gen0 = GC.CollectionCount(0) - gen0Before;
        GCSettings.LatencyMode = GCLatencyMode.Interactive;
        Record(firstName, firstWall, firstAllocated, gen0, firstCpu);
        Record(secondName, secondWall, secondAllocated, gen0, secondCpu);

        void Measure(Action action, List<double> wallSamples, List<double> cpuSamples, ref long allocated)
        {
            long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
            long cpuStart = currentProcess.TotalProcessorTime.Ticks;
            long start = Stopwatch.GetTimestamp();
            action();
            wallSamples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            cpuSamples.Add(
                (currentProcess.TotalProcessorTime.Ticks - cpuStart) /
                (double)TimeSpan.TicksPerMillisecond);
            allocated += GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
        }
    }

    private static string FormatCpu(BenchResult result) => result.CpuP50Ms == 0 && result.CpuP95Ms == 0
        ? string.Empty
        : $"cpu p50 {result.CpuP50Ms,7:F3}  cpu p95 {result.CpuP95Ms,7:F3}  " +
          $"cpu max {result.CpuMaxMs,7:F3} / max-wall {result.CpuAtMaxWallMs,7:F3}  ";

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
}

public static class BenchReport
{
    private static readonly JsonSerializerOptions _Json = new() { WriteIndented = true };

    public static string Save(IReadOnlyList<BenchResult> results, IReadOnlyList<BenchMetric> metrics, BenchOptions options)
    {
        string path = Save(results, options, metrics);
        File.WriteAllText(Path.ChangeExtension(path, ".md"), Markdown(results, metrics));
        return path;
    }

    private static string Markdown(IReadOnlyList<BenchResult> results, IReadOnlyList<BenchMetric> metrics)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"# Terrain bench {DateTime.Now:yyyy-MM-dd HH:mm}").AppendLine();
        foreach (IGrouping<string, BenchResult> grid in results.GroupBy(r => r.Grid))
        {
            text.AppendLine($"## Сетка {grid.Key}").AppendLine()
                .AppendLine("| Набор | Замер | p50, мс | p95, мс | max, мс | CPU p50/p95, мс | CPU max/max-wall, мс | аллок, Б |")
                .AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (BenchResult r in grid)
            {
                string cpu = r.CpuP50Ms == 0 && r.CpuP95Ms == 0
                    ? "—"
                    : $"{r.CpuP50Ms:F3}/{r.CpuP95Ms:F3}";
                string cpuMax = r.CpuMaxMs == 0 && r.CpuAtMaxWallMs == 0
                    ? "—"
                    : $"{r.CpuMaxMs:F3}/{r.CpuAtMaxWallMs:F3}";
                text.AppendLine($"| {r.Suite} | {r.Name} | {r.P50Ms:F4} | {r.P95Ms:F4} | {r.MaxMs:F3} | {cpu} | {cpuMax} | {r.AllocatedBytesPerIteration} |");
            }

            List<BenchMetric> gridMetrics = metrics.Where(m => m.Grid == grid.Key).ToList();
            if (gridMetrics.Count > 0)
            {
                text.AppendLine().AppendLine("| Набор | Метрика | Значение |").AppendLine("|---|---|---:|");
                foreach (BenchMetric m in gridMetrics)
                {
                    text.AppendLine($"| {m.Suite} | {m.Name} | {m.Value:F3} {m.Unit} |");
                }
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Save(IReadOnlyList<BenchResult> results, BenchOptions options, IReadOnlyList<BenchMetric> metrics)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        string path = Path.GetFullPath(Path.Combine(
            options.OutputDirectory,
            $"terrain_{DateTime.Now:yyyyMMdd_HHmmss}.json"));
        var document = new
        {
            timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            machine = Environment.MachineName,
            processors = Environment.ProcessorCount,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            results,
            metrics,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, _Json));
        return path;
    }

    // Сравнение с последним прошлым прогоном из той же папки.
    public static void CompareWithPrevious(IReadOnlyList<BenchResult> results, BenchOptions options, string currentPath)
    {
        string? previous = Directory.Exists(options.OutputDirectory)
            ? Directory.GetFiles(options.OutputDirectory, "terrain_*.json")
                .Select(Path.GetFullPath)
                .Where(path => path != currentPath)
                .OrderByDescending(path => path)
                .FirstOrDefault()
            : null;
        if (previous == null)
        {
            Console.WriteLine("Прошлого прогона нет — сравнивать не с чем.");
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(previous));
        var baseline = new Dictionary<string, double>();
        foreach (JsonElement item in document.RootElement.GetProperty("results").EnumerateArray())
        {
            string key = $"{item.GetProperty("Suite").GetString()}/{item.GetProperty("Name").GetString()}/{item.GetProperty("Grid").GetString()}";
            baseline[key] = item.GetProperty("P50Ms").GetDouble();
        }

        Console.WriteLine();
        Console.WriteLine($"Сравнение p50 с {Path.GetFileName(previous)} (порог {options.RegressionThreshold:P0}):");
        int regressions = 0;
        int improvements = 0;
        foreach (BenchResult result in results)
        {
            if (!baseline.TryGetValue(result.Key, out double before) || before <= 0)
            {
                continue;
            }

            if (Math.Abs(result.P50Ms - before) < options.NoiseFloorMs)
            {
                continue;
            }

            double change = (result.P50Ms - before) / before;
            string mark = change > options.RegressionThreshold ? "ХУЖЕ " : change < -options.RegressionThreshold ? "лучше" : "     ";
            if (change > options.RegressionThreshold)
            {
                regressions++;
            }
            else if (change < -options.RegressionThreshold)
            {
                improvements++;
            }

            if (mark.Trim().Length > 0)
            {
                Console.WriteLine($"  {mark} {change,8:+0.0%;-0.0%}  {before,9:F4} → {result.P50Ms,9:F4} мс  {result.Key}");
            }
        }

        Console.WriteLine($"Итого: хуже {regressions}, лучше {improvements}.");
    }
}
