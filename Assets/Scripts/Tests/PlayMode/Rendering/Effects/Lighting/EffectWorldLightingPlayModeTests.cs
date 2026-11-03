#nullable enable

using System.Collections;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Entities.Robots;
using Kern.Game.Events.Playback;
using Kern.Rendering;
using Kern.Rendering.Display;
using Kern.World.Lighting;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class EffectWorldLightingPlayModeTests
{
    private DummyAuthenticationScope? _authentication;

    [TearDown]
    public void RestoreAuthentication() => _authentication?.Restore();

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator GunShotReusesRenderersAfterEffectIsCleared()
    {
        _authentication = DummyAuthenticationScope.Seed("playmode-gunshot-reuse");
        yield return PlayModeHarness.StartAtGateway();
        var bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        var effects = PlayModeHarness.RequireInGame<ServerEventPlaybackManager>();
        var local = PlayModeHarness.RequireInGame<ILocalPlayerState>().Current!;
        effects.ClearActiveEvents();
        try
        {
            effects.PlayEffect(VisualEffect.GunShot, checked((ushort)local.Position.x), checked((ushort)local.Position.y),
                checked((ushort)local.BotId));
            LineRenderer[] first = Object.FindObjectsByType<LineRenderer>()
                .Where(line => line.transform.parent != null && line.transform.parent.name == "SpriteAnimations/GunShot")
                .ToArray();
            Assert.That(first.Length, Is.EqualTo(44));
            effects.ClearActiveEvents();
            effects.PlayEffect(VisualEffect.GunShot, checked((ushort)local.Position.x), checked((ushort)local.Position.y),
                checked((ushort)local.BotId));
            LineRenderer[] second = Object.FindObjectsByType<LineRenderer>()
                .Where(line => line.transform.parent != null && line.transform.parent.name == "SpriteAnimations/GunShot")
                .ToArray();
            Assert.That(second, Is.EquivalentTo(first));
        }
        finally
        {
            effects.ClearActiveEvents();
        }
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator MainGame_HealHurtMining_EmitAndRemoveDynamicRadiance()
    {
        _authentication = DummyAuthenticationScope.Seed("playmode-effect-world-light");
        yield return PlayModeHarness.StartAtGateway();
        var bootstrap = PlayModeHarness.FindBootstrap()!;
        var config = bootstrap.Container.Resolve<IClientConfigManager>();
        GraphicsPreset originalPreset = config.Config.GraphicsPreset;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        var lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        var effects = PlayModeHarness.RequireInGame<ServerEventPlaybackManager>();
        var graphics = PlayModeHarness.RequireInGame<GraphicsSettingsController>();
        var local = PlayModeHarness.RequireInGame<ILocalPlayerState>().Current!;
        Robot[] robots = Object.FindObjectsByType<Robot>();
        bool[] enabled = robots.Select(robot => robot.enabled).ToArray();
        float originalTimeScale = Time.timeScale;
        bool effectsEnabled = effects.enabled;
        try
        {
            foreach (Robot robot in robots) { robot.enabled = false; }
            effects.ClearActiveEvents();
            lighting.ClearDynamicLights();
            graphics.SelectPreset(GraphicsPreset.Standard);
            yield return PlayModeHarness.Frames(10);
            Time.timeScale = 0f; // Compare one actual animation frame with/without its source.
            foreach (VisualEffect effect in new[] { VisualEffect.Heal, VisualEffect.Hurt, VisualEffect.Bz })
            {
                effects.enabled = true;
                effects.PlayEffect(effect, checked((ushort)local.Position.x), checked((ushort)local.Position.y),
                    checked((ushort)local.BotId));
                yield return PlayModeHarness.WaitUntil(
                    () => lighting.UploadedDynamicLightCount == 1, 20f,
                    "The real effect never published its dynamic source.");
                Assert.That(lighting.DynamicLightCount, Is.EqualTo(1));
                float emitted = 0f;
                yield return ReadRadiancePatch(lighting, local.transform.position, value => emitted = value);
                Assert.That(emitted, Is.GreaterThan(0.0001f),
                    "The production dynamic-light shaders must receive actual effect emission.");
                // Stop only effect ticking: sprites/materials remain present while its source is removed.
                effects.enabled = false;
                lighting.ClearDynamicLights();
                yield return PlayModeHarness.Frames(3);
                float removed = -1f;
                yield return ReadRadiancePatch(lighting, local.transform.position, value => removed = value);
                Assert.That(removed, Is.Zero, "The dynamic target retained stale effect radiance.");
                effects.ClearActiveEvents();
                Assert.That(lighting.DynamicLightCount, Is.Zero);
                TestContext.WriteLine($"effect={effect}; actualPeak={emitted:R}; removedPeak={removed:R}");
            }
        }
        finally
        {
            effects.ClearActiveEvents();
            effects.enabled = effectsEnabled;
            lighting.ClearDynamicLights();
            Time.timeScale = originalTimeScale;
            for (int index = 0; index < robots.Length; index++)
            {
                if (robots[index] != null) { robots[index].enabled = enabled[index]; }
            }
            graphics.SelectPreset(originalPreset);
        }
    }

    private static IEnumerator ReadRadiancePatch(LightingEngine lighting, Vector3 center, System.Action<float> receive)
    {
        RenderTexture target = lighting.GPUResources.Direct.Dynamic!;
        Assert.That(target, Is.Not.Null);
        Vector4 rect = lighting.WorldRect;
        int width = Mathf.Min(64, target.width), height = Mathf.Min(64, target.height);
        int x = Mathf.Clamp(Mathf.FloorToInt((center.x - rect.x) / rect.z * target.width) - width / 2, 0, target.width - width);
        int y = Mathf.Clamp(Mathf.FloorToInt((center.y - rect.y) / rect.w * target.height) - height / 2, 0, target.height - height);
        bool done = false, failed = false;
        float peak = 0f;
        AsyncGPUReadback.Request(target, 0, x, width, y, height, 0, 1, request =>
        {
            failed = request.hasError;
            if (!failed)
            {
                var pixels = request.GetData<ushort>();
                for (int index = 0; index < pixels.Length; index++)
                {
                    if (index % 4 != 3) { peak = Mathf.Max(peak, Mathf.HalfToFloat(pixels[index])); }
                }
            }
            done = true;
        });
        yield return PlayModeHarness.WaitUntil(() => done, 10f, "Effect radiance readback did not finish.");
        Assert.That(failed, Is.False);
        receive(peak);
    }
}
