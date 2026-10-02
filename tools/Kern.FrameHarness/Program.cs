using System.Globalization;
using Kern.FrameHarness;

bool jsonOutput = args.Length > 0 && args[^1] == "--json";
if (jsonOutput)
{
    args = args[..^1];
}

if (args.Length == 2 && args[0] == "validate")
{
    try
    {
        ValidationReport report = CaptureAnalyzer.Validate(CaptureJson.Read(args[1]));
        if (jsonOutput)
        {
            Console.WriteLine(CaptureJson.WriteReport(report));
        }
        else
        {
            PrintValidation(report);
        }
        return ExitCode(report.InputStatus, report.InvariantStatus, report.PerformanceStatus, report.CoverageStatus);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
    {
        Console.Error.WriteLine($"input=FAIL: {exception.Message}");
        return 2;
    }
}

if (args.Length == 15 && args[0] == "compare" &&
    TryOption(args, "--cpu-p95-ms", out double cpuP95) &&
    TryOption(args, "--cpu-p99-ms", out double cpuP99) &&
    TryOption(args, "--cpu-max-ms", out double cpuMax) &&
    TryOption(args, "--gpu-p95-ms", out double gpuP95) &&
    TryOption(args, "--gpu-p99-ms", out double gpuP99) &&
    TryOption(args, "--gpu-max-ms", out double gpuMax))
{
    try
    {
        FrameCapture before = CaptureJson.Read(args[1]);
        FrameCapture after = CaptureJson.Read(args[2]);
        ComparisonReport report = CaptureAnalyzer.Compare(before, after,
            new ComparisonBudget(cpuP95, cpuP99, cpuMax, gpuP95, gpuP99, gpuMax));
        if (jsonOutput)
        {
            Console.WriteLine(CaptureJson.WriteReport(report));
            return ExitCode(report.CompatibilityStatus, report.InvariantStatus, report.PerformanceStatus,
                report.CoverageStatus, report.ArtifactIdentityStatus);
        }

        Console.WriteLine($"scope={report.RuleScope}");
        Console.WriteLine($"compatibility={report.CompatibilityStatus.ToString().ToUpperInvariant()}");
        Console.WriteLine($"invariant={report.InvariantStatus.ToString().ToUpperInvariant()}");
        Console.WriteLine($"performance={report.PerformanceStatus.ToString().ToUpperInvariant()}");
        Console.WriteLine($"coverage={report.CoverageStatus.ToString().ToUpperInvariant()}");
        Console.WriteLine($"artifactIdentity={report.ArtifactIdentityStatus.ToString().ToUpperInvariant()}");
        Console.WriteLine($"artifacts: {report.BeforeArtifactIdentity ?? "unknown"} -> {report.AfterArtifactIdentity ?? "unknown"}");
        foreach ((string metric, (MetricStatistics oldStats, MetricStatistics newStats)) in report.Statistics)
        {
            Console.WriteLine($"{metric}: samples={oldStats.SampleCount} p50={oldStats.P50:F3}/{newStats.P50:F3}ms " +
                $"p95={oldStats.P95:F3}/{newStats.P95:F3}ms p99={oldStats.P99:F3}/{newStats.P99:F3}ms " +
                $"max={oldStats.Max:F3}/{newStats.Max:F3}ms");
        }

        PrintFindings(report.Findings);
        return ExitCode(report.CompatibilityStatus, report.InvariantStatus, report.PerformanceStatus,
            report.CoverageStatus, report.ArtifactIdentityStatus);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
    {
        Console.Error.WriteLine($"input=FAIL: {exception.Message}");
        return 2;
    }
}

Console.Error.WriteLine("Usage:");
Console.Error.WriteLine("  Append --json for a machine-readable report. Rule scope: OPT-1/S0,S1,S4; other OPT rules and GRID-32 are not implemented.");
Console.Error.WriteLine("  dotnet run --project tools/Kern.FrameHarness -- validate <capture.json>");
Console.Error.WriteLine("  dotnet run --project tools/Kern.FrameHarness -- compare <before.json> <after.json> --cpu-p95-ms <n> --cpu-p99-ms <n> --cpu-max-ms <n> --gpu-p95-ms <n> --gpu-p99-ms <n> --gpu-max-ms <n>");
return 64;

static bool TryOption(string[] arguments, string name, out double value)
{
    value = 0d;
    int index = Array.IndexOf(arguments, name, 3);
    return index >= 3 && index % 2 == 1 && index + 1 < arguments.Length &&
        double.TryParse(arguments[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value >= 0d;
}

static int ExitCode(params CheckStatus[] statuses) =>
    statuses.Contains(CheckStatus.Fail) ? 1 :
    statuses.Contains(CheckStatus.Incomplete) ? 3 : 0;

static void PrintValidation(ValidationReport report)
{
    Console.WriteLine($"scope={report.RuleScope}");
    Console.WriteLine($"input={report.InputStatus.ToString().ToUpperInvariant()}");
    Console.WriteLine($"invariant={report.InvariantStatus.ToString().ToUpperInvariant()}");
    Console.WriteLine($"performance={report.PerformanceStatus.ToString().ToUpperInvariant()}");
    Console.WriteLine($"coverage={report.CoverageStatus.ToString().ToUpperInvariant()}");
    foreach ((string metric, MetricStatistics stats) in report.Statistics)
    {
        Console.WriteLine($"{metric}: samples={stats.SampleCount} p50={stats.P50:F3}ms p95={stats.P95:F3}ms " +
            $"p99={stats.P99:F3}ms max={stats.Max:F3}ms");
    }

    PrintFindings(report.Findings);
}

static void PrintFindings(IEnumerable<string> findings)
{
    foreach (string finding in findings)
    {
        Console.WriteLine($"- {finding}");
    }
}
