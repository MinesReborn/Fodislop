#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// Жизненный цикл GPU-ресурсов освещения в настоящей игре: создание при входе
// в мир, пересоздание при смене качества без накопления текстур и полное
// освобождение при выходе. Нужна видеокарта с compute-шейдерами.
[TestFixture]
[Category("GPU")]
public sealed class LightingGPULifecyclePlayModeTests
{
    private const string TestDummyToken = "playmode-lighting-gpu-token";

    // Имена целей освещения из LightingResourceManager.CreateTexture.
    private static readonly HashSet<string> _LightingTargetNames =
    [
        "_LightingMaterialField",
        "_StaticEmissionField",
        "_RadianceDirect",
        "_RadianceDirectStatic",
        "_RadianceBounce",
        "_WorldLightTexture",
        "_LightingCellSolidMask",
    ];

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private GraphicsPreset _originalPreset;
    private GraphicsQualitySettings _originalSettings;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Assume.That(SystemInfo.supportsComputeShaders, Is.True, "Lighting needs compute shader support.");
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _config = _bootstrap.Container.Resolve<IClientConfigManager>();
        _originalPreset = _config.Config.GraphicsPreset;
        _originalSettings = _config.Config.GraphicsQualitySettings;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        GraphicsSettingsController? graphics = PlayModeHarness.ResolveInGame<GraphicsSettingsController>();
        if (graphics != null)
        {
            if (GraphicsQualityProfile.IsStandard(_originalPreset))
            {
                graphics.SelectStandardPreset(_originalPreset);
            }
            else
            {
                graphics.SetCustomSettings(_originalSettings);
            }
        }

        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator EnteringWorld_CreatesPipelineThatSolvesOnInvalidation()
    {
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        yield return SelectAndSettle(GraphicsPreset.High);

        Assert.That(lighting.IsInitialized, Is.True);
        Assert.That(lighting.IsGPUPipelineInitialized, Is.True);
        Assert.That(lighting.GPUResources.Output.Lightmap, Is.Not.Null);
        Assert.That(lighting.GPUResources.Output.Lightmap!.IsCreated(), Is.True);
        Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.True);

        yield return AssertSolvesAfterInvalidation(lighting, "Lighting did not re-solve an invalidated world.");
    }

    [UnityTest]
    public IEnumerator CyclingPresets_RecreatesTargetsWithoutLeakingThem()
    {
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        GraphicsPreset[] presets =
        [
            GraphicsPreset.High,
            GraphicsPreset.VeryLow,
            GraphicsPreset.Ultra,
            GraphicsPreset.Low,
            GraphicsPreset.Medium,
            GraphicsPreset.VeryHigh,
        ];

        for (int round = 0; round < 2; round++)
        {
            foreach (GraphicsPreset preset in presets)
            {
                yield return SelectAndSettle(preset);

                Dictionary<string, int> live = LiveLightingTargets();
                string duplicates = string.Join(", ", live.Where(entry => entry.Value > 1).Select(entry => $"{entry.Key}×{entry.Value}"));
                Assert.That(duplicates, Is.Empty, $"Preset {preset} (round {round}) left stale lighting targets alive.");

                if (lighting.IsGPUPipelineInitialized)
                {
                    yield return AssertSolvesAfterInvalidation(lighting, $"Lighting did not solve after switching to {preset}.");
                }
                else
                {
                    Assert.That(live, Is.Empty, $"Preset {preset} disabled lighting but kept its targets.");
                    Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.False);
                }
            }
        }
    }

    [UnityTest]
    public IEnumerator LeavingWorld_ReleasesEveryLightingTarget()
    {
        yield return SelectAndSettle(GraphicsPreset.High);
        Assert.That(LiveLightingTargets(), Is.Not.Empty);

        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu),
            PlayModeHarness.UITimeoutSeconds);
        yield return PlayModeHarness.Frames(3);

        Assert.That(LiveLightingTargets(), Is.Empty, "Lighting targets outlived the game scene.");
        Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.False);

        // Повторный вход создаёт ресурсы заново, а не находит старые.
        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainGame),
            PlayModeHarness.WorldTimeoutSeconds);
        yield return PlayModeHarness.Frames(30);
        Assert.That(LiveLightingTargets().Values.All(count => count == 1), Is.True);
    }

    // Освещение кэширует решение и пересчитывает его только при инвалидации,
    // поэтому живость конвейера проверяется явной инвалидацией.
    private static IEnumerator AssertSolvesAfterInvalidation(LightingEngine lighting, string failureMessage)
    {
        ulong solves = lighting.SolveCount;
        lighting.InvalidateStaticCache();
        yield return PlayModeHarness.WaitUntil(() => lighting.SolveCount > solves, 5f, failureMessage);
    }

    private IEnumerator SelectAndSettle(GraphicsPreset preset)
    {
        PlayModeHarness.RequireInGame<GraphicsSettingsController>().SelectStandardPreset(preset);

        // Destroy освобождает объекты в конце кадра, а ресурсы новой
        // конфигурации создаются в ближайшем обновлении освещения.
        yield return PlayModeHarness.Frames(10);
    }

    private static Dictionary<string, int> LiveLightingTargets() =>
        Resources.FindObjectsOfTypeAll<RenderTexture>()
            .Where(texture => texture != null && _LightingTargetNames.Contains(texture.name))
            .GroupBy(texture => texture.name)
            .ToDictionary(group => group.Key, group => group.Count());
}
