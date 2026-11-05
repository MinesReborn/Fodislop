#nullable enable

using System.Text.Json;

namespace Kern.LightingTests;

internal static class FreezeReport
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: freeze-report <LightingDumpDirectory>");
            return 2;
        }

        string directory = args[0];
        string countersPath = Path.Combine(directory, "counters.json");
        if (!File.Exists(countersPath))
        {
            Console.Error.WriteLine($"missing {countersPath}");
            return 2;
        }

        using JsonDocument counters = JsonDocument.Parse(File.ReadAllText(countersPath));
        long rayWork = Number(counters.RootElement, "estimatedCascadeRayWorkUnits");
        Console.WriteLine($"dump: {directory}");
        Console.WriteLine($"estimated cascade ray work: {rayWork}");
        Console.WriteLine($"static solves: {Number(counters.RootElement, "staticSolveCount")}");
        Console.WriteLine($"full cascade entries: {Number(counters.RootElement, "cascadeFullEntries")}");
        Console.WriteLine($"partial cascade entries: {Number(counters.RootElement, "cascadePartialEntries")}");
        Console.WriteLine($"terrain mesh ms: {Number(counters.RootElement, "terrainMeshTimeMs"):0.###}");
        Console.WriteLine($"terrain cache ms: {Number(counters.RootElement, "terrainCacheTimeMs"):0.###}");
        Console.WriteLine($"terrain chunk loads: {Number(counters.RootElement, "terrainChunkLoadCount")}");
        return 0;
    }

    private static long Number(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double floating)
                ? (long)floating
                : 0;
    }
}
