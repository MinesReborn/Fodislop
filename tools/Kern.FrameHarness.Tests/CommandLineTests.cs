using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

[TestFixture]
public sealed class CommandLineTests
{
    [Test]
    public void SuccessfulComparisonProducesMachineReadableStatistics()
    {
        var result = RunComparison(_ => { });
        Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
        JsonNode json = JsonNode.Parse(result.Output)!;
        Assert.That(json["performanceStatus"]!.GetValue<string>(), Is.EqualTo("pass"));
        Assert.That(json["statistics"]!["steady.frameDurationMs"]!["after"]!["sampleCount"]!.GetValue<int>(), Is.EqualTo(2));
    }

    [Test]
    public void InvariantFailureExitsNonzeroEvenWithMissingGpu()
    {
        var result = RunComparison(after =>
        {
            after["frames"]![1]!["gpuFrameMs"] = null;
            after["frames"]![1]!["frameCounters"]!["lightingStaticSolves"] = 1;
        });
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(JsonNode.Parse(result.Output)!["invariantStatus"]!.GetValue<string>(), Is.EqualTo("fail"));
    }

    [Test]
    public void UnknownGpuExitsIncomplete()
    {
        var result = RunComparison(after => after["frames"]![1]!["gpuFrameMs"] = null);
        Assert.That(result.ExitCode, Is.EqualTo(3), result.Output);
    }

    [Test]
    public void StaleProducerCannotTurnOldWorkIntoViolation()
    {
        var result = RunComparison(after =>
        {
            after["frames"]![1]!["producerFrameId"] = 100;
            after["frames"]![1]!["frameCounters"]!["lightingStaticSolves"] = 7;
            after["frames"]![1]!["cpuMs"]!["terrainMesh"] = 100;
        });
        Assert.That(result.ExitCode, Is.EqualTo(3), result.Output);
        JsonNode report = JsonNode.Parse(result.Output)!;
        Assert.That(report["invariantStatus"]!.GetValue<string>(), Is.EqualTo("incomplete"));
        Assert.That(report["performanceStatus"]!.GetValue<string>(), Is.EqualTo("incomplete"));
    }

    [Test]
    public void StaleProducerDoesNotHideIndependentGpuRegression()
    {
        var result = RunComparison(after =>
        {
            after["frames"]![1]!["producerLifecycleValid"] = false;
            after["frames"]![1]!["gpuFrameMs"] = 9;
        });
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(JsonNode.Parse(result.Output)!["performanceStatus"]!.GetValue<string>(), Is.EqualTo("fail"));
    }

    [Test]
    public void GpuRegressionExitsFailure()
    {
        var result = RunComparison(after => after["frames"]![1]!["gpuFrameMs"] = 9);
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
    }

    [Test]
    public void InvalidInputExitsFailure()
    {
        var result = RunComparison(after => after["manifest"] = null);
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
    }

    [Test]
    public void InvalidJsonExitsInputError()
    {
        var result = RunComparison(after => after["schemaVersion"] = "not-a-number");
        Assert.That(result.ExitCode, Is.EqualTo(2), result.Output);
    }

    private static (int ExitCode, string Output) RunComparison(Action<JsonObject> change)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"kern-frame-harness-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            JsonObject before = CaptureAnalyzerTests.Fixture();
            JsonObject after = CaptureAnalyzerTests.Fixture();
            after["captureId"] = "after";
            change(after);
            string beforePath = Path.Combine(directory, "before.json");
            string afterPath = Path.Combine(directory, "after.json");
            File.WriteAllText(beforePath, before.ToJsonString());
            File.WriteAllText(afterPath, after.ToJsonString());
            using Process process = new();
            process.StartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            string[] arguments =
            [
                typeof(CaptureAnalyzer).Assembly.Location, "compare", beforePath, afterPath,
                "--cpu-p95-ms", "0", "--cpu-p99-ms", "0", "--cpu-max-ms", "0",
                "--gpu-p95-ms", "0", "--gpu-p99-ms", "0", "--gpu-max-ms", "0", "--json",
            ];
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            Assert.That(process.Start(), Is.True);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Standalone harness CLI timed out.");
            }

            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        finally
        {
            // Only disposable fixtures in this test's uniquely created directory.
            Directory.Delete(directory, recursive: true);
        }
    }
}
