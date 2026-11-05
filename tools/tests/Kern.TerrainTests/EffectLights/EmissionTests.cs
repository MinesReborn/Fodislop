using System;
using System.Linq;
using Kern;
using Kern.AssetPipeline.Animation;
using Kern.Game.Visual.Effects.Lighting;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;

namespace Kern.TerrainTests;

public sealed class EffectEmissionTests
{
    [Test]
    public void IntegralUsesLinearStraightColorCoverageAndWorldArea()
    {
        SpriteEmissionFrame result = SpriteEmissionIntegrator.IntegrateFrame(
            [new Color32(255, 0, 0, 255), new Color32(0, 255, 255, 0)], 2, new RectInt(0, 0, 2, 1), new Vector2(2,2));
        Assert.That(result.Radiance.r, Is.EqualTo(2f));
        Assert.That(result.Radiance.g, Is.Zero);
        Assert.That(result.Radiance.b, Is.Zero);
    }

    [Test]
    public void CoverageIsAppliedAfterSrgbDecode()
    {
        SpriteEmissionFrame result = SpriteEmissionIntegrator.IntegrateFrame(
            [new Color32(128, 128, 128, 128)], 1, new RectInt(0, 0, 1, 1), new Vector2(1,1));
        Assert.That(result.Radiance.r, Is.EqualTo(0.1083535f).Within(0.000002f));
    }

    [Test]
    public void FrameAtlasUsesCorrectRectsAndPhysicalPixelArea()
    {
        var atlas = new Texture2D(4, 1, 1);
        atlas.SetPixelData(new Color32[] { new(255,0,0,255), new(0,0,0,0), new(0,255,0,255), new(0,255,0,255) }, 0);
        SpriteEmissionFrame[] frames = SpriteEmissionIntegrator.Frames(atlas, 2, 1, 2, 1f);
        Assert.That(frames[0].Radiance.r, Is.EqualTo(1f));
        Assert.That(frames[0].Radiance.g, Is.Zero);
        Assert.That(frames[1].Radiance.g, Is.EqualTo(2f));
        Assert.That(frames[0].Centroid.x, Is.EqualTo(-0.5f));
        Assert.That(frames[0].Centroid.y, Is.Zero);
        Assert.That(frames[1].Centroid.x, Is.Zero);
    }

    [Test]
    public void InvalidRegionFailsInsteadOfSubstitutingEmission()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteEmissionIntegrator.IntegrateFrame(
            [new Color32(255,255,255,255)], 1, new RectInt(-1,0,1,1), new Vector2(1,1)));
    }

    [Test]
    public void TransientLightFollowsPositionAndScaleWithoutHdrClamping()
    {
        var backend = new LightingEngine();
        using var light = new TransientEffectLight(backend);
        light.Submit(new Vector3(2,3,0), new Vector3(-2,3,1), new Color(1,.5f,.25f,1));
        var first = backend.Sources.Single();
        Assert.That(first.Key, Is.LessThan(0));
        Assert.That(first.Value.Position, Is.EqualTo(new Vector2(2,3)));
        Assert.That(first.Value.Color.r, Is.EqualTo(6f));
        Assert.That(first.Value.Color.g, Is.EqualTo(3f));
        light.Submit(new Vector3(7,8,0), new Vector3(1,1,1), Color.white);
        Assert.That(backend.Sources.Count, Is.EqualTo(1));
        Assert.That(backend.Sources.Single().Value.Position, Is.EqualTo(new Vector2(7,8)));
    }

    [Test]
    public void DisposalRemovesSourceAndPreventsResurrection()
    {
        var backend = new LightingEngine();
        var light = new TransientEffectLight(backend);
        light.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        light.Dispose(); light.Dispose();
        light.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        Assert.That(backend.Sources, Is.Empty);
        Assert.That(backend.RemovalCount, Is.EqualTo(1));
    }

    [Test]
    public void NextFrameRestoresSourceAfterLightingGenerationReset()
    {
        var backend = new LightingEngine();
        using var light = new TransientEffectLight(backend);
        light.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        int id = backend.Sources.Single().Key;
        backend.Sources.Clear();
        light.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        Assert.That(backend.Sources.Single().Key, Is.EqualTo(id));
    }

    [Test]
    public void AllEffectOwnersShareDistinctNegativeIdsAndBlackFramesRemoveLight()
    {
        int destructionId = TransientEffectLight.AllocateId();
        var backend = new LightingEngine();
        using var heal = new TransientEffectLight(backend);
        using var hurt = new TransientEffectLight(backend);
        heal.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        hurt.Submit(new Vector3(), new Vector3(1,1,1), Color.white);
        Assert.That(backend.Sources.Count, Is.EqualTo(2));
        Assert.That(backend.Sources.ContainsKey(destructionId), Is.False);
        heal.Submit(new Vector3(), new Vector3(1,1,1), Color.black);
        Assert.That(backend.Sources.Count, Is.EqualTo(1));
    }
}
