#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using NUnit.Framework;
using System;
using System.IO;

namespace Fodinae.Tests.Core;

public sealed class RuntimeDiagnosticsStateTests
{
    [Test]
    public void RuntimeAssetPaths_UsesPersistentOverrideAndCaseInsensitiveBundledLookup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"fodinae-paths-{Guid.NewGuid():N}");
        string bundled = Path.Combine(root, "bundled");
        string persistent = Path.Combine(root, "persistent");
        Directory.CreateDirectory(Path.Combine(bundled, "Skin"));
        Directory.CreateDirectory(Path.Combine(persistent, "skin"));
        File.WriteAllText(Path.Combine(bundled, "Skin", "Bee.png"), "bundled");
        File.WriteAllText(Path.Combine(persistent, "skin", "bee.png"), "persistent");

        try
        {
            var paths = new RuntimeAssetPaths(bundled, persistent);

            Assert.That(
                paths.FindBundledTextureFile("skin/bee.png"),
                Is.EqualTo(Path.Combine(bundled, "Skin", "Bee.png")));
            Assert.That(
                paths.FindTextureFile("Skin/Bee.png"),
                Is.EqualTo(Path.Combine(persistent, "skin", "bee.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("../secret.png")]
    [TestCase("/absolute.png")]
    [TestCase("skin//bee.png")]
    public void RuntimeAssetPaths_RejectsUnsafeRelativePaths(string relativePath)
    {
        string root = Path.Combine(Path.GetTempPath(), $"fodinae-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var paths = new RuntimeAssetPaths(root, root);
            Assert.Throws<ArgumentException>(() => paths.FindBundledTextureFile(relativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FrameTelemetry_InstancesDoNotShareMeasurements()
    {
        using var first = new FrameTelemetry();
        using var second = new FrameTelemetry();

        first.TerrainRebuildCount = 3;
        first.LightingBuildCommandsTimeMs = 4.5f;

        Assert.That(second.TerrainRebuildCount, Is.Zero);
        Assert.That(second.LightingBuildCommandsTimeMs, Is.Zero);
    }

    [Test]
    public void ResetFrameTimers_PreservesCumulativeCounters()
    {
        using var telemetry = new FrameTelemetry
        {
            TerrainMeshTimeMs = 2f,
            LightingExecuteCommandsTimeMs = 3f,
            TerrainRebuildCount = 4,
        };

        telemetry.ResetFrameTimers();

        Assert.That(telemetry.TerrainMeshTimeMs, Is.Zero);
        Assert.That(telemetry.LightingExecuteCommandsTimeMs, Is.Zero);
        Assert.That(telemetry.TerrainRebuildCount, Is.EqualTo(4));
    }

    [Test]
    public void RuntimeDebugSettings_DefaultsAreDisabled()
    {
        var settings = new RuntimeDebugSettings();

        Assert.That(settings.IgnoreCollision, Is.False);
        Assert.That(settings.BypassLightingCompute, Is.False);
        Assert.That(settings.BypassTerrainDraw, Is.False);
        Assert.That(settings.BypassCpuMeshRebuild, Is.False);
        Assert.That(settings.ShowRobotDebugVisuals, Is.False);
    }
}
