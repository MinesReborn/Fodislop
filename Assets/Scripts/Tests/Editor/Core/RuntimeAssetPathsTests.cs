#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;

namespace Kern.Tests.Core;

public sealed class RuntimeAssetPathsTests
{
    [Test]
    public void RuntimeAssetPaths_UsesPersistentOverrideAndCaseInsensitiveBundledLookup()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kern-paths-{Guid.NewGuid():N}");
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
        string root = Path.Combine(Path.GetTempPath(), $"kern-paths-{Guid.NewGuid():N}");
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
        Assert.That(settings.BypassGameUI, Is.False);
    }

    [Test]
    public void PostProcessRuntimeState_SetLutRejectsNonFiniteAndClampsIntensity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kern-lut-{Guid.NewGuid():N}.cube");
        File.WriteAllText(
            path,
            "LUT_3D_SIZE 2\n" +
            "0 0 0\n1 0 0\n0 1 0\n1 1 0\n" +
            "0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        ColorGradeCubeLut? lut = null;
        try
        {
            Assert.That(ColorGradeCubeLut.TryLoad(path, out lut, out string error), Is.True, error);

            PostProcessRuntimeState.SetLut(lut, float.NaN);
            Assert.That(PostProcessRuntimeState.Lut, Is.Null);
            Assert.That(PostProcessRuntimeState.LutIntensity, Is.Zero);

            PostProcessRuntimeState.SetLut(lut, 2f, ColorGradeLutColorSpace.SrgbRec709);
            Assert.That(PostProcessRuntimeState.Lut, Is.SameAs(lut));
            Assert.That(PostProcessRuntimeState.LutIntensity, Is.EqualTo(1f));
            Assert.That(PostProcessRuntimeState.LutColorSpace, Is.EqualTo(ColorGradeLutColorSpace.SrgbRec709));

            PostProcessRuntimeState.SetLut(null, 1f);
            Assert.That(PostProcessRuntimeState.Lut, Is.Null);
            Assert.That(PostProcessRuntimeState.LutIntensity, Is.Zero);
        }
        finally
        {
            PostProcessRuntimeState.SetLut(null, 0f);
            lut?.Dispose();
            File.Delete(path);
        }
    }

    [Test]
    public void PostProcessRuntimeState_DisplayCalibrationRepairsNonFiniteValues()
    {
        PostProcessRuntimeState.SetDisplayCalibration(
            float.NaN,
            float.PositiveInfinity);

        Assert.That(
            PostProcessRuntimeState.DisplayPaperWhiteNits,
            Is.EqualTo(DisplaySettings.DefaultPaperWhite));
        Assert.That(
            PostProcessRuntimeState.DisplayPeakBrightnessNits,
            Is.EqualTo(DisplaySettings.DefaultPeakBrightness));
    }

    [Test]
    public void PostProcessRuntimeState_DisplayPeakNeverFallsBelowPaperWhite()
    {
        PostProcessRuntimeState.SetDisplayCalibration(
            DisplaySettings.PaperWhiteMax,
            DisplaySettings.PeakBrightnessMin);

        Assert.That(
            PostProcessRuntimeState.DisplayPeakBrightnessNits,
            Is.EqualTo(DisplaySettings.PaperWhiteMax));
    }
}
