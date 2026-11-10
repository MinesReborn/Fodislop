#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

using Kern.AssetPipeline.Loading;
using Kern.Core.Diagnostics;

using Kern.Diagnostics.World;

namespace Kern.Tests.Core;

public sealed class RuntimeAssetPathsTests
{
    [Test]
    public async Task RuntimeAssetPaths_LoadsTextureBytesByPublishedName()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kern-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Skin"));
        byte[] expected = [1, 2, 3];
        File.WriteAllBytes(Path.Combine(root, "Skin/Bee.png"), expected);

        try
        {
            var paths = new RuntimeAssetPaths(root);
            await paths.EnsureReadyAsync();

            Assert.That(paths.GetTextureBytes("Skin/Bee.png"), Is.EqualTo(expected));
            Assert.That(paths.GetTextureBytes("Skin/Bee"), Is.EqualTo(expected));
            Assert.That(paths.GetTextureBytes("skin/bee.png"), Is.Null);
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
            var paths = new RuntimeAssetPaths(root);
            Assert.Throws<ArgumentException>(() => paths.GetTextureBytes(relativePath));
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
        Assert.That(settings.BypassCPUMeshRebuild, Is.False);
        Assert.That(settings.ShowRobotDebugVisuals, Is.False);
        Assert.That(settings.BypassGameUI, Is.False);
        Assert.That(settings.TailMotionDebugMode, Is.EqualTo(TailMotionDebugMode.Normal));
    }

    [Test]
    public void PostProcessRuntimeState_SetLUTRejectsNonFiniteAndClampsIntensity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kern-lut-{Guid.NewGuid():N}.cube");
        File.WriteAllText(
            path,
            "LUT_3D_SIZE 2\n" +
            "0 0 0\n1 0 0\n0 1 0\n1 1 0\n" +
            "0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        ColorGradeCubeLUT? lut = null;
        try
        {
            Assert.That(ColorGradeCubeLUT.TryLoad(path, out lut, out string error), Is.True, error);

            PostProcessRuntimeState.SetLUT(lut, float.NaN);
            Assert.That(PostProcessRuntimeState.LUT, Is.Null);
            Assert.That(PostProcessRuntimeState.LUTIntensity, Is.Zero);

            PostProcessRuntimeState.SetLUT(lut, 2f, ColorGradeLUTColorSpace.SrgbRec709);
            Assert.That(PostProcessRuntimeState.LUT, Is.SameAs(lut));
            Assert.That(PostProcessRuntimeState.LUTIntensity, Is.EqualTo(1f));
            Assert.That(PostProcessRuntimeState.LUTColorSpace, Is.EqualTo(ColorGradeLUTColorSpace.SrgbRec709));

            PostProcessRuntimeState.SetLUT(null, 1f);
            Assert.That(PostProcessRuntimeState.LUT, Is.Null);
            Assert.That(PostProcessRuntimeState.LUTIntensity, Is.Zero);
        }
        finally
        {
            PostProcessRuntimeState.SetLUT(null, 0f);
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
