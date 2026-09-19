#nullable enable

using Kern.World.Lighting;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.LightingTests;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string command = args.Length == 0 ? "all" : args[0];
            string repositoryRoot = FindRepositoryRoot();
            return command switch
            {
                "all" => RunAll(),
                "oracle" or "golden" => LightingOracle.Run(),
                "streaming" => RunStreamingChecks(),
                "freeze-report" => FreezeReport.Run(args[1..]),
                "transport" => NativeHarness.RunTransport(repositoryRoot),
                "rects" => ProbeRectTests.Run(),
                "equivalence" => RunEquivalence(repositoryRoot, args[1..]),
                "compile" => NativeHarness.CompileShaders(repositoryRoot, args.Length > 1 ? args[1] : null),
                "layout" => RunLayoutChecks(),
                _ => Usage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int RunEquivalence(string repositoryRoot, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("usage: dotnet run -- equivalence <reference.compute> [candidate.compute]");
            return 2;
        }

        string candidate = args.Length == 2
            ? args[1]
            : "Assets/Resources/Shaders/Lighting/WorldLighting.compute";
        return NativeHarness.RunEquivalence(repositoryRoot, args[0], candidate);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Fodislop repository root.");
    }

    private static int RunAll()
    {
        int result = RunLayoutChecks();
        if (result != 0)
        {
            return result;
        }

        return LightingOracle.Run();
    }

    private static int RunStreamingChecks()
    {
        StreamingPolicy policy = StreamingPolicy.Default;
        int windowSize = policy.QuantizeDimensionWithHeadroom(
            StreamingPolicy.DefaultMapWindowDimensionCells);
        int margin = policy.ResolvePrefetchMarginCells(windowSize);
        int origin = policy.AlignOrigin(100 - windowSize / 2);
        var batches = new List<int>();
        for (int cell = 100; cell < 356; cell++)
        {
            int offset = cell - origin;
            if (policy.ContainsViewportWithMargin(
                    new Vector2Int(windowSize, windowSize),
                    new Vector2Int(offset, offset),
                    Vector2Int.one,
                    margin))
            {
                continue;
            }

            if (offset + 1 > windowSize - margin)
            {
                origin += policy.AllocationQuantumCells;
                batches.Add(cell);
            }
        }

        for (int index = 1; index < batches.Count; index++)
        {
            Check(batches[index] - batches[index - 1] >= policy.AllocationQuantumCells, "prefetch quantum");
        }

        var governor = new StreamingGovernor(policy);
        StreamingPlan plan = governor.Plan(
            new StreamingWindow(Vector2Int.zero, new Vector2Int(windowSize, windowSize)),
            new Vector2Int(policy.AllocationQuantumCells, 0),
            new Vector2Int(windowSize, windowSize),
            dimensionsChanged: false);
        Check(plan.Kind == StreamingPlanKind.ScrollTerrain, "scroll plan");
        Check(plan.Delta.x == policy.AllocationQuantumCells, "scroll delta");
        Console.WriteLine($"C# streaming checks passed: window={windowSize}, margin={margin}, batches={batches.Count}.");
        return 0;
    }

    private static int RunLayoutChecks()
    {
        var cascades = new List<CascadeLayout>();
        CascadeLayoutBuilder.BuildCascadeLayouts(768, 512, 4096, cascades);
        Check(cascades[^1].IntervalEnd >= MathF.Sqrt(768f * 768f + 512f * 512f), "last cascade covers field");
        Check(CascadeLayoutBuilder.SelectMaximumCascadeDirections(768, 512, 4096, 64, 200_000_000) == 4, "ray work governor");
        Check(CascadeLayoutBuilder.SelectMaximumCascadeDirections(768, 512, 4096, 64, 1_500_000_000) == 64, "quality direction cap");
        Check(LightingComputeBinder.ResolveTransmittanceDebugDistance() == 1f, "debug distance binding");
        Console.WriteLine("C# layout and binding checks passed.");
        return 0;
    }

    private static void Check(bool value, string name)
    {
        if (!value)
        {
            throw new InvalidOperationException($"Layout check failed: {name}");
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: dotnet run -- [all|layout|oracle|golden|streaming|transport|equivalence|compile|freeze-report <dump>]");
        return 2;
    }
}
