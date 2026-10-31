#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Effects;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Visual.Effects.Engine;

/// <summary>Renders engine sprites through the shared VisualEffect pool, which submits them to the world batch.</summary>
public sealed class WorldEffectSpriteSink : IEffectSpriteSink, IDisposable
{
    private readonly IVisualEffectService _visualEffectService;
    private readonly EffectSpriteSheetCache _spriteSheets;
    private readonly int _maximumSlots;
    private readonly List<Slot> _slots = new();
    private int _frameIndex;
    private bool _disposed;

    public WorldEffectSpriteSink(
        IVisualEffectService visualEffectService,
        EffectSpriteSheetCache spriteSheets,
        int maximumSlots = 256)
    {
        _visualEffectService = visualEffectService ?? throw new ArgumentNullException(nameof(visualEffectService));
        _spriteSheets = spriteSheets ?? throw new ArgumentNullException(nameof(spriteSheets));
        if (maximumSlots <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSlots), maximumSlots, "Slot budget must be positive.");
        }

        _maximumSlots = maximumSlots;
    }

    public void Render(EffectEngine engine, float deltaSeconds)
    {
        if (engine == null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorldEffectSpriteSink));
        }

        _frameIndex++;
        engine.Update(deltaSeconds, this);
        for (int index = 0; index < _slots.Count; index++)
        {
            Slot slot = _slots[index];
            if (!slot.Used || slot.LastFrame == _frameIndex)
            {
                continue;
            }

            slot.Visual.SetEnabled(false);
            slot.Used = false;
        }
    }

    public void Draw(in EffectSpriteDraw sprite)
    {
        if (_disposed)
        {
            return;
        }

        Sprite frame = _spriteSheets.GetSprite(
            sprite.SpriteSheet,
            sprite.SpriteSheetColumns,
            sprite.SpriteSheetRows,
            sprite.FrameIndex);
        Slot? slot = AcquireSlot(sprite.SortingOrder);
        GameObject? target = slot?.Visual.GameObject;
        if (slot == null || target == null)
        {
            return;
        }

        float spriteWidthUnits = frame.rect.width / RenderingConstants.PIXELS_PER_UNIT;
        float scale = spriteWidthUnits > 0f ? sprite.SizeUnits / spriteWidthUnits : 1f;
        Transform transform = target.transform;
        transform.position = new Vector3(sprite.Position.x, sprite.Position.y, 0f);
        transform.localScale = new Vector3(scale, scale, 1f);
        slot.Visual.SetSprite(frame);
        slot.Visual.SetColor(sprite.Color);
        slot.Visual.SetEnabled(true);
        slot.Used = true;
        slot.LastFrame = _frameIndex;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Slot slot in _slots)
        {
            slot.Visual.SetEnabled(false);
            slot.Visual.SetSprite(null);
            _visualEffectService.Release(slot.Visual);
        }

        _slots.Clear();
    }

    private Slot? AcquireSlot(int sortingOrder)
    {
        for (int index = 0; index < _slots.Count; index++)
        {
            Slot candidate = _slots[index];
            if (!candidate.Used && candidate.SortingOrder == sortingOrder)
            {
                return candidate;
            }
        }

        if (_slots.Count >= _maximumSlots)
        {
            return null;
        }

        IVisualEffectSlot? visual = _visualEffectService.Acquire(sortingOrder);
        if (visual?.GameObject == null)
        {
            if (visual != null)
            {
                _visualEffectService.Release(visual);
            }

            return null;
        }

        var slot = new Slot(visual, sortingOrder);
        _slots.Add(slot);
        return slot;
    }

    private sealed class Slot
    {
        public readonly IVisualEffectSlot Visual;
        public readonly int SortingOrder;
        public bool Used;
        public int LastFrame = -1;

        public Slot(IVisualEffectSlot visual, int sortingOrder)
        {
            Visual = visual;
            SortingOrder = sortingOrder;
        }
    }
}
