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
    public void ColorGradeState_BypassNeutralizesOnlySelectedLayer()
    {
        var state = new ColorGradeState
        {
            Exposure = 1.5f,
            Slope = new Vector3(1.2f, 0.9f, 1.1f),
        };
        state.SetBypassed(ColorGradeLayer.Cdl, bypassed: true);

        ColorGradeSnapshot snapshot = state.ToSnapshot();

        Assert.That(snapshot.Slope, Is.EqualTo(Vector3.one));
        Assert.That(state.EffectiveExposure, Is.EqualTo(1.5f));
        Assert.That(state.Slope, Is.EqualTo(new Vector3(1.2f, 0.9f, 1.1f)));

        state.SetBypassed(ColorGradeLayer.Cdl, bypassed: false);
        state.SetBypassed(ColorGradeLayer.Exposure, bypassed: true);
        snapshot = state.ToSnapshot();
        Assert.That(snapshot.Exposure, Is.Zero);
    }

    [Test]
    public void ColorGradeState_DisabledLayerIsNeutralAndUndoable()
    {
        var state = new ColorGradeState
        {
            Exposure = 2f,
        };

        state.BeginHistoryFrame();
        state.SetEnabled(ColorGradeLayer.Exposure, enabled: false);
        state.CommitHistoryFrame();

        Assert.That(state.IsEnabled(ColorGradeLayer.Exposure), Is.False);
        Assert.That(state.ToSnapshot().Exposure, Is.Zero);
        Assert.That(state.ToAuthoredSnapshot().EnabledMask & 1, Is.Zero);

        Assert.That(state.Undo(), Is.True);
        Assert.That(state.IsEnabled(ColorGradeLayer.Exposure), Is.True);
        Assert.That(state.ToSnapshot().Exposure, Is.EqualTo(2f));

        state.Solo = ColorGradeLayer.Exposure;
        state.SetEnabled(ColorGradeLayer.Exposure, enabled: false);
        Assert.That(state.Solo, Is.Null);
        Assert.That(state.IsActive(ColorGradeLayer.Cdl), Is.True);
    }

    [Test]
    public void ColorGradeSnapshot_BlendPreservesEnabledMask()
    {
        var left = new ColorGradeState();
        left.SetEnabled(ColorGradeLayer.Exposure, enabled: false);
        var right = new ColorGradeState();
        right.SetEnabled(ColorGradeLayer.Cdl, enabled: false);

        ColorGradeSnapshot blended = left.ToAuthoredSnapshot().BlendTo(
            right.ToAuthoredSnapshot(),
            0.25f);

        Assert.That(blended.EnabledMask & 1, Is.Zero);
        Assert.That(blended.EnabledMask & (1 << 2), Is.Not.Zero);
    }

    [Test]
    public void ColorGradeState_ClearPreviewOverridesPreservesAuthoredValues()
    {
        var state = new ColorGradeState
        {
            Contrast = 0.25f,
            Solo = ColorGradeLayer.Curve,
        };
        state.SetBypassed(ColorGradeLayer.Contrast, bypassed: true);

        state.ClearPreviewOverrides();

        Assert.That(state.HasPreviewOverrides, Is.False);
        Assert.That(state.Contrast, Is.EqualTo(0.25f));
    }

    [Test]
    public void ColorGradeState_AuthoredSnapshotIgnoresPreviewOverrides()
    {
        var state = new ColorGradeState
        {
            Temperature = 30f,
            Solo = ColorGradeLayer.Exposure,
        };
        state.SetBypassed(ColorGradeLayer.WhiteBalance, bypassed: true);

        ColorGradeSnapshot preview = state.ToSnapshot();
        ColorGradeSnapshot authored = state.ToAuthoredSnapshot();

        Assert.That(preview.Temperature, Is.Zero);
        Assert.That(authored.Temperature, Is.EqualTo(30f));
        Assert.That(authored.Transform, Is.EqualTo(state.Transform));
    }

    [Test]
    public void ColorGradeState_SanitizeRepairsNonFiniteAndOutOfRangeValues()
    {
        var state = new ColorGradeState
        {
            Exposure = float.NaN,
            Slope = new Vector3(float.PositiveInfinity, -2f, 8f),
        };

        state.Sanitize();

        Assert.That(state.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(state.Slope.x, Is.EqualTo(PostProcessLook.Grade.Slope.x));
        Assert.That(state.Slope.y, Is.EqualTo(ColorGradeState.SlopeMin));
        Assert.That(state.Slope.z, Is.EqualTo(ColorGradeState.SlopeMax));
    }

    [Test]
    public void ColorGradeState_DefaultsAreIdentityForEveryGradeLayer()
    {
        var state = new ColorGradeState();
        ColorGradeSnapshot snapshot = state.ToAuthoredSnapshot();

        Assert.That(snapshot.Transform, Is.EqualTo(DisplayTransform.None));
        Assert.That(snapshot.EnabledMask, Is.EqualTo((1 << 6) - 1));
        Assert.That(snapshot.Exposure, Is.Zero);
        Assert.That(snapshot.Temperature, Is.Zero);
        Assert.That(snapshot.Tint, Is.Zero);
        Assert.That(snapshot.Slope, Is.EqualTo(Vector3.one));
        Assert.That(snapshot.Offset, Is.EqualTo(Vector3.zero));
        Assert.That(snapshot.Power, Is.EqualTo(Vector3.one));
        Assert.That(snapshot.PrimaryLift, Is.EqualTo(Vector3.zero));
        Assert.That(snapshot.PrimaryGamma, Is.EqualTo(Vector3.one));
        Assert.That(snapshot.PrimaryGain, Is.EqualTo(Vector3.one));
        Assert.That(snapshot.PrimaryOffset, Is.EqualTo(Vector3.zero));
        Assert.That(snapshot.PrimaryMaster, Is.EqualTo(new Vector4(0f, 1f, 1f, 0f)));
        Assert.That(snapshot.CdlMaster, Is.EqualTo(new Vector3(1f, 0f, 1f)));
        Assert.That(snapshot.CdlSaturation, Is.EqualTo(1f));
        Assert.That(snapshot.Saturation, Is.EqualTo(1f));
        Assert.That(snapshot.Vibrance, Is.Zero);
        Assert.That(snapshot.Hue, Is.Zero);
        Assert.That(snapshot.Contrast, Is.Zero);
        Assert.That(snapshot.Pivot, Is.EqualTo(0.5f));
        Assert.That(snapshot.Shadows, Is.Zero);
        Assert.That(snapshot.Highlights, Is.Zero);
        Assert.That(snapshot.Blacks, Is.Zero);
        Assert.That(snapshot.Whites, Is.Zero);
        Assert.That(snapshot.Toe, Is.Zero);
        Assert.That(snapshot.Shoulder, Is.Zero);
        Assert.That(snapshot.MasterCurve.Evaluate(0.25f), Is.EqualTo(0.25f));
        Assert.That(snapshot.RedCurve.Evaluate(0.75f), Is.EqualTo(0.75f));
        Assert.That(snapshot.Qualifier.Enabled, Is.False);
        Assert.That(snapshot.Lut, Is.Null);
        Assert.That(snapshot.LutIntensity, Is.Zero);
    }

    [Test]
    public void ColorGradeCurve_NonFinitePointsCannotEscapeAsNonFiniteValues()
    {
        var curve = new ColorGradeCurve();
        curve.Load(
            [
                new Vector2(float.NaN, float.PositiveInfinity),
                new Vector2(0.5f, 0.25f),
                new Vector2(float.PositiveInfinity, float.NaN),
            ],
            (int)ColorCurveInterpolation.Smooth);

        Assert.That(curve.GetPoint(0), Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(curve.GetPoint(curve.PointCount - 1).x, Is.EqualTo(1f));
        Assert.That(float.IsFinite(curve.Evaluate(float.NaN)), Is.True);
        Assert.That(float.IsFinite(curve.Evaluate(float.PositiveInfinity)), Is.True);
    }

    [Test]
    public void ColorGradeQualifier_RejectsNonFiniteHueSamples()
    {
        var qualifier = new ColorGradeQualifier();

        qualifier.AddHueSample(float.NaN);
        qualifier.AddHueSample(float.PositiveInfinity);

        Assert.That(qualifier.HueSamples, Is.Empty);
    }

    [Test]
    public void ColorGradeZone_WeightIsStableAcrossCoreFeatherAndOutside()
    {
        var zone = new ColorGradeZone(
            "depth",
            centerY: 100f,
            halfHeight: 10f,
            feather: 20f,
            grade: ColorGradeSnapshot.FromLook());

        Assert.That(zone.WeightAt(100f), Is.EqualTo(1f));
        Assert.That(zone.WeightAt(110f), Is.EqualTo(1f));
        Assert.That(zone.WeightAt(120f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(zone.WeightAt(130f), Is.Zero);
        Assert.That(zone.WeightAt(float.NaN), Is.Zero);
    }

    [Test]
    public void ColorGradeZone_DefaultsKeepTheAuthoredBaseLook()
    {
        var zone = new ColorGradeZone(
            "depth",
            centerY: 100f,
            halfHeight: 10f,
            feather: 20f,
            grade: ColorGradeSnapshot.FromLook());

        Assert.That(zone.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(zone.Contrast, Is.EqualTo(PostProcessLook.ColorGrading.Contrast));
        Assert.That(zone.Saturation, Is.EqualTo(PostProcessLook.ColorGrading.Saturation));
    }

    // with-вирази на init-пропах снапшота вимагають IsExternalInit, якого нема
    // в тестовій збірці: йдемо через mutable стейт (ctor = ResetToLook, тобто
    // ті ж дефолти що FromLook) і чисту авторську конверсію без превью.
    private static ColorGradeSnapshot GradeWithTemperature(float temperature)
    {
        var state = new ColorGradeState();
        state.Temperature = temperature;
        return state.ToAuthoredSnapshot();
    }

    [Test]
    public void ColorGradeZoneDriver_DisabledZonesRestoreBaseGrade()
    {
        ColorGradeSnapshot modified = GradeWithTemperature(40f);
        PostProcessRuntimeState.SetColorGrade(modified);
        var zones = new ColorGradeZones
        {
            Enabled = false,
        };

        ColorGradeZones.Resolution resolution = ColorGradeZoneDriver.Push(zones, camera: null);

        Assert.That(
            PostProcessRuntimeState.ColorGrade,
            Is.EqualTo(ColorGradeSnapshot.FromLook()));
        Assert.That(
            resolution.Exposure,
            Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
        Assert.That(
            resolution.Contrast,
            Is.EqualTo(PostProcessLook.ColorGrading.Contrast));
        Assert.That(
            resolution.Saturation,
            Is.EqualTo(PostProcessLook.ColorGrading.Saturation));
    }

    [Test]
    public void ColorGradeZones_ResolveIncludesTheFullAuthoredLook()
    {
        var zones = new ColorGradeZones
        {
            Enabled = true,
        };
        zones.Add(new ColorGradeZone(
            "depth",
            centerY: 20f,
            halfHeight: 5f,
            feather: 5f,
            grade: GradeWithTemperature(25f),
            exposure: 1.25f,
            contrast: 0.2f,
            saturation: 0.7f));

        ColorGradeZones.Resolution result = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            20f);

        Assert.That(result.Exposure, Is.EqualTo(1.25f));
        Assert.That(result.Contrast, Is.EqualTo(0.2f));
        Assert.That(result.Saturation, Is.EqualTo(0.7f));
        Assert.That(result.Grade.Temperature, Is.EqualTo(25f));
    }

    [Test]
    public void ColorGradeZone_2DBounds_RestrictsWeightOutsideWidth()
    {
        var zone = new ColorGradeZone(
            "room",
            centerY: 50f,
            halfHeight: 10f,
            feather: 5f,
            grade: ColorGradeSnapshot.FromLook(),
            centerX: 100f,
            halfWidth: 20f);

        // Center inside
        Assert.That(zone.WeightAt(100f, 50f), Is.EqualTo(1f));
        // Along X inside width
        Assert.That(zone.WeightAt(115f, 50f), Is.EqualTo(1f));
        // Outside X feather
        Assert.That(zone.WeightAt(130f, 50f), Is.Zero);
        // Inside X but outside Y
        Assert.That(zone.WeightAt(100f, 70f), Is.Zero);
    }

    [Test]
    public void ColorGradeZones_2DResolve_AppliesZoneWithinBounds()
    {
        var zones = new ColorGradeZones
        {
            Enabled = true,
        };
        zones.Add(new ColorGradeZone(
            "cave",
            centerY: 50f,
            halfHeight: 10f,
            feather: 5f,
            grade: GradeWithTemperature(-30f),
            exposure: -1.0f,
            centerX: 200f,
            halfWidth: 50f));

        // Inside 2D bounds
        ColorGradeZones.Resolution inside = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            200f,
            50f);
        Assert.That(inside.Exposure, Is.EqualTo(-1.0f));
        Assert.That(inside.Grade.Temperature, Is.EqualTo(-30f));

        // Outside X bounds
        ColorGradeZones.Resolution outside = zones.Resolve(
            ColorGradeZones.Resolution.FromLook(),
            300f,
            50f);
        Assert.That(outside.Exposure, Is.EqualTo(PostProcessLook.ColorGrading.Exposure));
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
