#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class GraphicsPresetSwitchPlayModeTests
{
    private const string TestDummyToken = "playmode-graphics-preset-switch-token";

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private GraphicsPreset _originalPreset;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Assume.That(SystemInfo.supportsComputeShaders, Is.True, "Graphics switching test needs compute shader support.");

        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _config = _bootstrap.Container.Resolve<IClientConfigManager>();
        _originalPreset = _config.Config.GraphicsPreset;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        GraphicsSettingsController? graphics = PlayModeHarness.ResolveInGame<GraphicsSettingsController>();
        graphics?.SelectPreset(_originalPreset);

        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator BothPresets_SwitchThroughProductionPath()
    {
        GraphicsSettingsController graphics = PlayModeHarness.RequireInGame<GraphicsSettingsController>();
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        GraphicsPreset[] presets =
        [
            GraphicsPreset.Standard,
            GraphicsPreset.Overdrive,
        ];

        foreach (GraphicsPreset preset in presets)
        {
            graphics.SelectPreset(preset);
            yield return PlayModeHarness.Frames(12);

            Assert.That(graphics.SelectedPreset, Is.EqualTo(preset));
            Assert.That(_config.Config.GraphicsPreset, Is.EqualTo(preset));
            Assert.That(lighting.IsInitialized, Is.True, $"Lighting was lost after selecting {preset}.");
        }
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator RepeatedPresetShrinkAndGrow_DoesNotCorruptDynamicLightSlots()
    {
        GraphicsSettingsController graphics = PlayModeHarness.RequireInGame<GraphicsSettingsController>();
        GraphicsPreset[] sequence =
        [
            GraphicsPreset.Overdrive,
            GraphicsPreset.Standard,
            GraphicsPreset.Overdrive,
            GraphicsPreset.Standard,
            GraphicsPreset.Overdrive,
            GraphicsPreset.Standard,
        ];

        foreach (GraphicsPreset preset in sequence)
        {
            graphics.SelectPreset(preset);
            yield return PlayModeHarness.Frames(20);
            Assert.That(graphics.SelectedPreset, Is.EqualTo(preset));
        }

        // Any IndexOutOfRangeException from DynamicLightTileCache is an
        // unhandled PlayMode failure; this assertion also verifies that the
        // live scene remained in the game after all reallocations.
        Assert.That(PlayModeHarness.FindBootstrap()!.CurrentSceneName,
            Is.EqualTo(ProjectRuntimeContracts.SceneNames.MainGame));
    }
}
