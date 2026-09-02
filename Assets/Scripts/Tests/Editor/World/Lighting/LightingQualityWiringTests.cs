#if UNITY_EDITOR
#nullable enable

using System;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting.Quality;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.World.Lighting
{
    // Guardrail for the "many layers, any one can silently drop the value"
    // failure mode: GUI -> ClientConfig -> GraphicsQualityProfile ->
    // LightingEngine -> WorldLighting.compute. Each test below
    // targets one hop that a manual audit already caught breaking once
    // (Ultra profile drifting from PerPixel, standard presets not actually
    // differing) so a future edit that reintroduces the same class of bug
    // fails loudly here instead of only in a debug view nobody is looking
    // at.
    [TestFixture]
    public sealed class LightingQualityWiringTests
    {
        [Test]
        public void ResolverLocksUltraToPerPixelWhenLightingIsOn()
        {
            foreach (LightingQualityMode requested in new[]
                     {
                         LightingQualityMode.PerBlock,
                         LightingQualityMode.PerPixel,
                         LightingQualityMode.PerPixelBilinearFix,
                     })
            {
                Assert.That(
                    LightingQualityResolver.Resolve(GraphicsPreset.Ultra, requested),
                    Is.EqualTo(LightingQualityMode.PerPixel),
                    $"Ultra must resolve to PerPixel even when {requested} was requested.");
            }
        }

        [Test]
        public void ResolverNeverOverridesAnExplicitOffOnAnyPreset()
        {
            // Ultra used to override Off too, which made the control that
            // disables the most expensive subsystem in the frame do nothing at
            // all on the preset that needs it most - with no feedback anywhere
            // that the choice had been discarded.
            foreach (GraphicsPreset preset in new[]
                     {
                         GraphicsPreset.VeryLow,
                         GraphicsPreset.Low,
                         GraphicsPreset.Medium,
                         GraphicsPreset.High,
                         GraphicsPreset.VeryHigh,
                         GraphicsPreset.Ultra,
                         GraphicsPreset.Custom,
                     })
            {
                Assert.That(
                    LightingQualityResolver.Resolve(preset, LightingQualityMode.Off),
                    Is.EqualTo(LightingQualityMode.Off),
                    $"{preset} must not override an explicit Off.");
            }
        }

        [Test]
        public void ResolverPassesRequestedModeThroughForNonUltraPresets()
        {
            foreach (GraphicsPreset preset in new[]
                     {
                         GraphicsPreset.VeryLow,
                         GraphicsPreset.Low,
                         GraphicsPreset.Medium,
                         GraphicsPreset.High,
                         GraphicsPreset.VeryHigh,
                         GraphicsPreset.Custom,
                     })
            {
                foreach (LightingQualityMode requested in new[]
                         {
                             LightingQualityMode.Off,
                             LightingQualityMode.PerBlock,
                             LightingQualityMode.PerPixel,
                             LightingQualityMode.PerPixelBilinearFix,
                         })
                {
                    Assert.That(
                        LightingQualityResolver.Resolve(preset, requested),
                        Is.EqualTo(requested),
                        $"{preset} must not override the requested tier {requested}.");
                }
            }
        }

        [Test]
        public void UltraProfileAssetIsActuallyPerPixel()
        {
            GraphicsQualityProfile profile = Resources.Load<GraphicsQualityProfile>(
                "GraphicsQualityProfile");
            Assert.That(profile, Is.Not.Null, "Resources/GraphicsQualityProfile.asset is missing.");

            GraphicsQualitySettings ultra = profile!.Get(GraphicsPreset.Ultra);
            Assert.That(
                ultra.LightingQuality,
                Is.EqualTo(LightingQualityMode.PerPixel),
                "The Ultra preset asset must carry LightingQuality: PerPixel explicitly - " +
                "if this ever regresses to the enum's zero-default (PerBlock), Ultra silently " +
                "stops being per-pixel and no exception fires anywhere in the chain.");
        }

        [Test]
        public void StandardPresetsBelowUltraDefaultToPerBlock()
        {
            GraphicsQualityProfile profile = Resources.Load<GraphicsQualityProfile>(
                "GraphicsQualityProfile");
            Assert.That(profile, Is.Not.Null, "Resources/GraphicsQualityProfile.asset is missing.");

            foreach (GraphicsPreset preset in new[]
                     {
                         GraphicsPreset.VeryLow,
                         GraphicsPreset.Low,
                         GraphicsPreset.Medium,
                         GraphicsPreset.High,
                         GraphicsPreset.VeryHigh,
                     })
            {
                Assert.That(
                    profile!.Get(preset).LightingQuality,
                    Is.EqualTo(LightingQualityMode.PerBlock),
                    $"{preset} is expected to default to PerBlock. If this is an intentional " +
                    "design change, update this test alongside it - don't let it silently pass.");
            }
        }

        [Test]
        public void PresetsCarryTheIntendedPostProcessTier()
        {
            GraphicsQualityProfile profile = Resources.Load<GraphicsQualityProfile>(
                "GraphicsQualityProfile");
            Assert.That(profile, Is.Not.Null, "Resources/GraphicsQualityProfile.asset is missing.");

            // Тир выводится из пресета в коде (GraphicsQualityProfile.TierFor),
            // а не читается из ассета: иначе смена перечисления требовала бы
            // отдельного шага правки `.asset`. Проверяем и вывод, и то, что
            // Get() действительно им перекрывает сериализованное значение.
            //
            // Тира «выключено» нет ни у одного пресета: без тонмапа света
            // срезаются в плоский белый, то есть это был бы не дешёвый кадр,
            // а неверный. Самый дешёвый тир — Essential.
            foreach (GraphicsPreset preset in new[]
                     {
                         GraphicsPreset.VeryLow,
                         GraphicsPreset.Low,
                     })
            {
                Assert.That(
                    GraphicsQualityProfile.TierFor(preset),
                    Is.EqualTo(PostProcessQualityMode.Essential),
                    $"{preset} is the cheap tier: one-pass effects, no bloom pyramid.");
                Assert.That(
                    profile!.Get(preset).PostProcessQuality,
                    Is.EqualTo(PostProcessQualityMode.Essential),
                    $"{preset}: Get() must override whatever the asset still stores.");
            }

            foreach (GraphicsPreset preset in new[]
                     {
                         GraphicsPreset.Medium,
                         GraphicsPreset.High,
                         GraphicsPreset.VeryHigh,
                         GraphicsPreset.Ultra,
                     })
            {
                Assert.That(
                    GraphicsQualityProfile.TierFor(preset),
                    Is.EqualTo(PostProcessQualityMode.Full),
                    $"{preset} is expected to run the full post-processing stack.");
                Assert.That(
                    profile!.Get(preset).PostProcessQuality,
                    Is.EqualTo(PostProcessQualityMode.Full),
                    $"{preset}: Get() must override whatever the asset still stores.");
            }
        }

        [Test]
        public void ValidateSettingsRejectsAnUndefinedPostProcessQualityValue()
        {
            var settings = new GraphicsQualitySettings(
                lightingPixelsPerCell: 1,
                lightingMaximumTextureDimension: 512,
                lightingMaximumLightCount: 64,
                lightingMaximumRaySteps: 8,
                lightingUpdatesPerSecond: 15f,
                lightingCascadeAtlasLimit: 512,
                renderScale: 0.8f,
                antiAliasing: 0,
                lightingQuality: LightingQualityMode.PerBlock,
                postProcessQuality: (PostProcessQualityMode)99);

            Assert.Throws<InvalidOperationException>(
                () => GraphicsQualityProfile.ValidateSettings(settings, "Custom"));
        }

        [Test]
        public void ValidateSettingsRejectsAnUndefinedLightingQualityValue()
        {
            // A corrupted save, a hand-edited config JSON, or a future enum
            // reorder can put an out-of-range int here. Without this check
            // it sails through validation (every other field is in range)
            // and only blows up later as an IndexOutOfRangeException when
            // PauseMenu indexes its tier-name array with it - a
            // crash on opening Settings instead of a clear error at
            // load/apply time.
            var settings = new GraphicsQualitySettings(
                lightingPixelsPerCell: 1,
                lightingMaximumTextureDimension: 512,
                lightingMaximumLightCount: 64,
                lightingMaximumRaySteps: 8,
                lightingUpdatesPerSecond: 15f,
                lightingCascadeAtlasLimit: 512,
                renderScale: 0.8f,
                antiAliasing: 0,
                lightingQuality: (LightingQualityMode)99);

            Assert.Throws<InvalidOperationException>(
                () => GraphicsQualityProfile.ValidateSettings(settings, "Custom"));
        }

        [Test]
        public void ValidateSettingsRejectsLightingTextureSmallerThanStableViewport()
        {
            var settings = new GraphicsQualitySettings(
                lightingPixelsPerCell: 1,
                lightingMaximumTextureDimension: 128,
                lightingMaximumLightCount: 64,
                lightingMaximumRaySteps: 8,
                lightingUpdatesPerSecond: 15f,
                lightingCascadeAtlasLimit: 512,
                renderScale: 0.8f,
                antiAliasing: 0,
                lightingQuality: LightingQualityMode.PerPixel);

            Assert.Throws<InvalidOperationException>(
                () => GraphicsQualityProfile.ValidateSettings(settings, "Custom"));
        }
    }
}
#endif
