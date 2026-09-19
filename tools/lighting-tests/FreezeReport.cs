#nullable enable

using System.Text.Json;

namespace Kern.LightingTests;

internal static class FreezeReport
{
    private const long DefaultStaticRayBudget = 1_500_000_000;

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
        JsonElement config = default;
        string configPath = Path.Combine(directory, "config.json");
        if (File.Exists(configPath))
        {
            config = JsonDocument.Parse(File.ReadAllText(configPath)).RootElement.Clone();
        }

        long rayWork = Number(counters.RootElement, "estimatedCascadeRayWorkUnits");
        long budget = config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("maximumStaticCascadeRayWorkUnits", out JsonElement configuredBudget)
            ? configuredBudget.GetInt64()
            : DefaultStaticRayBudget;
        Console.WriteLine($"dump: {directory}");
        Console.WriteLine($"estimated cascade ray work: {rayWork}");
        Console.WriteLine($"static ray budget: {budget}");
        Console.WriteLine($"static solves: {Number(counters.RootElement, "staticSolveCount")}");
        Console.WriteLine($"full cascade entries: {Number(counters.RootElement, "cascadeFullEntries")}");
        Console.WriteLine($"partial cascade entries: {Number(counters.RootElement, "cascadePartialEntries")}");
        Console.WriteLine($"terrain mesh ms: {Number(counters.RootElement, "terrainMeshTimeMs"):0.###}");
        Console.WriteLine($"terrain cache ms: {Number(counters.RootElement, "terrainCacheTimeMs"):0.###}");
        Console.WriteLine($"terrain chunk loads: {Number(counters.RootElement, "terrainChunkLoadCount")}");
        Console.WriteLine($"classification: {(rayWork > budget ? "static transport over budget" : "no static ray budget violation")}");
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
