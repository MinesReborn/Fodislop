#nullable enable

using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using NUnit.Framework;

namespace Kern.Tests.World.Lighting;

public sealed class LightingQualityTransitionPolicyTests
{
    [TestCase(LightingQualityMode.Off, LightingQualityMode.PerPixel, false, true)]
    [TestCase(LightingQualityMode.PerPixel, LightingQualityMode.Off, false, true)]
    [TestCase(LightingQualityMode.PerPixel, LightingQualityMode.PerPixel, true, true)]
    [TestCase(LightingQualityMode.PerPixel, LightingQualityMode.PerPixel, false, false)]
    public void RequiresFullSolveForEnableDisableOrTechnicalSettingsChange(
        LightingQualityMode previous,
        LightingQualityMode next,
        bool technicalSettingsChanged,
        bool expected)
    {
        Assert.That(
            LightingQualityTransitionPolicy.RequiresFullSolve(
                technicalSettingsChanged,
                previous,
                next),
            Is.EqualTo(expected));
    }
}
