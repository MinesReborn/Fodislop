#nullable enable

using System;
using Kern.Game.Visual.Effects.Engine;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Effects;

[TestFixture]
public sealed class EffectSpriteSheetCacheTests
{
    private const string SparkSheet = "Visual/Sprites/Textures/CrystallSparkle/CrystallSparkleImage";

    [Test]
    public void SlicesSheetIntoRowMajorFrames()
    {
        using var cache = new EffectSpriteSheetCache();

        Sprite first = cache.GetSprite(SparkSheet, 6, 1, 0);
        Sprite last = cache.GetSprite(SparkSheet, 6, 1, 5);

        Assert.That(first.texture.width, Is.EqualTo(96));
        Assert.That(first.texture.height, Is.EqualTo(16));
        Assert.That(first.rect, Is.EqualTo(new Rect(0f, 0f, 16f, 16f)));
        Assert.That(last.rect, Is.EqualTo(new Rect(80f, 0f, 16f, 16f)));
        Assert.That(first.pixelsPerUnit, Is.EqualTo(RenderingConstants.PIXELS_PER_UNIT));
        Assert.That(cache.GetSprite(SparkSheet, 6, 1, 0), Is.SameAs(first));
    }

    [Test]
    public void SheetThatDoesNotDivideIntoGrid_IsRejected()
    {
        using var cache = new EffectSpriteSheetCache();

        Assert.Throws<InvalidOperationException>(() => cache.GetSprite(SparkSheet, 5, 1, 0));
    }

    [Test]
    public void FrameOutsideGrid_IsRejected()
    {
        using var cache = new EffectSpriteSheetCache();
        cache.GetSprite(SparkSheet, 6, 1, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.GetSprite(SparkSheet, 6, 1, 6));
    }

    [Test]
    public void MissingSheet_Throws()
    {
        using var cache = new EffectSpriteSheetCache();

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetSprite("Effects/Sprites/Absent", 1, 1, 0));
    }

    [Test]
    public void DisposedCache_RejectsLookups()
    {
        var cache = new EffectSpriteSheetCache();
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.GetSprite(SparkSheet, 6, 1, 0));
    }
}
