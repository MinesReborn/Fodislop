#nullable enable

using System;
using System.IO;
using Kern.Effects;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Effects;

[TestFixture]
public sealed class EffectCatalogTests
{
    [Test]
    public void ShippedCatalog_LoadsEveryEffect()
    {
        EffectCatalog catalog = EffectCatalog.Load("Effects/Catalog/Effects");

        Assert.That(catalog.Definitions.Count, Is.EqualTo(2));
        Assert.That(catalog.TryGet("spark", out EffectDefinition spark), Is.True);
        Assert.That(spark.Emitters.Length, Is.EqualTo(1));
        Assert.That(spark.Emitters[0].SpriteSheet,
            Is.EqualTo("Visual/Sprites/Textures/CrystallSparkle/CrystallSparkleImage"));
        Assert.That(spark.Emitters[0].SpriteSheetColumns, Is.EqualTo(6));
        Assert.That(spark.Emitters[0].BurstCount, Is.EqualTo(10));
        Assert.That(catalog.TryGet("missing", out _), Is.False);
    }

    [Test]
    public void DuplicateIds_AreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            new EffectCatalog([Definition("dup"), Definition("dup")]));
    }

    [Test]
    public void InvalidRange_IsRejected()
    {
        EffectDefinition definition = Definition("bad");
        definition.Emitters[0].LifetimeSeconds = new Vector2(2f, 1f);

        Assert.Throws<InvalidDataException>(() => EffectCatalog.Validate(definition));
    }

    [Test]
    public void MissingEmitter_IsRejected()
    {
        var definition = new EffectDefinition { Id = "empty", DurationSeconds = 1f };

        Assert.Throws<InvalidDataException>(() => EffectCatalog.Validate(definition));
    }

    [Test]
    public void ValidDefinition_IsAccepted()
    {
        Assert.DoesNotThrow(() => EffectCatalog.Validate(EffectTestData.Spark()));
        Assert.DoesNotThrow(() => EffectCatalog.Validate(EffectTestData.LoopingSmoke()));
    }

    [Test]
    public void MissingResource_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EffectCatalog.Load("Effects/Catalog/Absent"));
    }

    private static EffectDefinition Definition(string id) => new()
    {
        Id = id,
        DurationSeconds = 1f,
        Emitters =
        [
            new EffectEmitterDefinition
            {
                Id = "e",
                SpriteSheet = "Effects/Sprites/Spark",
                FrameCount = 1,
                BurstCount = 1,
            },
        ],
    };
}
