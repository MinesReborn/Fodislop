#nullable enable

using System.Linq;
using Kern.Effects;
using Kern.Game.Visual.Effects.Engine;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Effects;

[TestFixture]
public sealed class WorldEffectSpriteSinkTests
{
    [Test]
    public void Render_DrawsBurstThroughPoolSlots()
    {
        using var sheets = new EffectSpriteSheetCache();
        using var visualEffect = new RecordingVisualEffectService();
        using var engine = new EffectEngine(new EffectCatalog([EffectTestData.SheetSpark()]));
        using var sink = new WorldEffectSpriteSink(visualEffect, sheets);
        engine.Spawn("sheet-spark", new Vector2(2f, 3f));

        sink.Render(engine, EffectInstance.SimulationStepSeconds);

        Assert.That(visualEffect.Slots.Count, Is.EqualTo(6));
        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => slot.Enabled));
        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => slot.Sprite != null));
        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => slot.SortingOrder == 0));
        Assert.That(
            visualEffect.Slots,
            Has.All.Matches<RecordingVisualEffectSlot>(slot =>
                Mathf.Abs(slot.GameObject!.transform.position.x - 2f) <= 2f * EffectInstance.SimulationStepSeconds &&
                Mathf.Abs(slot.GameObject.transform.position.y - 3f) <= 2f * EffectInstance.SimulationStepSeconds));
        Assert.That(
            visualEffect.Slots,
            Has.All.Matches<RecordingVisualEffectSlot>(slot =>
            {
                float scale = slot.GameObject!.transform.localScale.x;
                return scale >= 0.5f && scale <= 0.9f;
            }));
    }

    [Test]
    public void Render_DisablesSlotsAfterParticlesExpire()
    {
        using var sheets = new EffectSpriteSheetCache();
        using var visualEffect = new RecordingVisualEffectService();
        using var engine = new EffectEngine(new EffectCatalog([EffectTestData.SheetSpark()]));
        using var sink = new WorldEffectSpriteSink(visualEffect, sheets);
        engine.Spawn("sheet-spark", Vector2.zero);

        for (int step = 0; step < 60; step++)
        {
            sink.Render(engine, EffectInstance.SimulationStepSeconds);
        }

        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => !slot.Enabled));
    }

    [Test]
    public void Render_RespectsSlotBudget()
    {
        using var sheets = new EffectSpriteSheetCache();
        using var visualEffect = new RecordingVisualEffectService();
        using var engine = new EffectEngine(new EffectCatalog([EffectTestData.SheetSpark()]));
        using var sink = new WorldEffectSpriteSink(visualEffect, sheets, maximumSlots: 2);
        engine.Spawn("sheet-spark", Vector2.zero);

        sink.Render(engine, EffectInstance.SimulationStepSeconds);

        Assert.That(visualEffect.Slots.Count, Is.EqualTo(2));
        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => slot.Enabled));
    }

    [Test]
    public void Render_KeepsSeparateSlotsPerSortingOrder()
    {
        using var sheets = new EffectSpriteSheetCache();
        using var visualEffect = new RecordingVisualEffectService();
        using var engine = new EffectEngine(new EffectCatalog([
            EffectTestData.SheetSpark(0),
            EffectTestData.SheetSpark(5),
        ]));
        using var sink = new WorldEffectSpriteSink(visualEffect, sheets);
        engine.Spawn("sheet-spark-0", Vector2.zero);
        engine.Spawn("sheet-spark-5", Vector2.zero);

        sink.Render(engine, EffectInstance.SimulationStepSeconds);

        Assert.That(visualEffect.Slots.Count(slot => slot.SortingOrder == 0), Is.EqualTo(6));
        Assert.That(visualEffect.Slots.Count(slot => slot.SortingOrder == 5), Is.EqualTo(6));
    }

    [Test]
    public void Dispose_ReleasesEveryAcquiredSlot()
    {
        using var sheets = new EffectSpriteSheetCache();
        using var visualEffect = new RecordingVisualEffectService();
        using var engine = new EffectEngine(new EffectCatalog([EffectTestData.SheetSpark()]));
        var sink = new WorldEffectSpriteSink(visualEffect, sheets);
        engine.Spawn("sheet-spark", Vector2.zero);
        sink.Render(engine, EffectInstance.SimulationStepSeconds);

        sink.Dispose();

        Assert.That(visualEffect.ReleasedCount, Is.EqualTo(visualEffect.Slots.Count));
        Assert.That(visualEffect.Slots, Has.All.Matches<RecordingVisualEffectSlot>(slot => slot.Released));
    }
}
