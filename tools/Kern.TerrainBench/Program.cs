#nullable enable

using System;
using System.Runtime.InteropServices;
using Kern.TerrainBench;
using Kern.World.Terrain;

// Бенчмарк процессорных путей террейна вне Unity: настоящий код игры
// (сдвиги, упаковка, учёт грязных областей, заливка фона, маски, искажение,
// пространственный индекс) на синтетических пещерах. Видеокарта здесь не
// меряется.
//
//   dotnet run -c Release --project tools/Kern.TerrainBench -- [опции]
//     --sizes 128x96,192x128,256x160   размеры сетки
//     --filter flood                   только наборы/замеры с подстрокой
//     --seconds 0.25                   целевое время на замер
//     --quick                          быстрый прогон
//     --threshold 0.1                  порог регрессии в сравнении
//     --out Logs/bench                 папка JSON-результатов

BenchOptions options = BenchOptions.Parse(args);
var runner = new BenchRunner(options);

Console.WriteLine(
    $"Terrain bench · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} · " +
    $"{Environment.ProcessorCount} ядер · вершина {Marshal.SizeOf<TerrainVertex>()} Б");

foreach ((int width, int height) in options.Sizes)
{
    runner.Grid = $"{width}x{height}";
    Console.WriteLine();
    Console.WriteLine($"══ Сетка {width}×{height} ══");
    Suites.All(runner, width, height);
}

string path = BenchReport.Save(runner.Results, runner.Metrics, options);
Console.WriteLine();
Console.WriteLine($"Результаты: {path}");
BenchReport.CompareWithPrevious(runner.Results, options, path);
