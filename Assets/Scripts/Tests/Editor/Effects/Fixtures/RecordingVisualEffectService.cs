#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Tests.Effects;

internal sealed class RecordingVisualEffectService : IVisualEffectService, System.IDisposable
{
    private readonly List<RecordingVisualEffectSlot> _slots = new();

    public IReadOnlyList<RecordingVisualEffectSlot> Slots => _slots;

    public int ReleasedCount { get; private set; }

    public IVisualEffectSlot? Acquire(int sortingOrder = -500)
    {
        var slot = new RecordingVisualEffectSlot(sortingOrder);
        _slots.Add(slot);
        return slot;
    }

    public void Release(IVisualEffectSlot slot)
    {
        ReleasedCount++;
        if (slot is RecordingVisualEffectSlot recording)
        {
            recording.Released = true;
        }
    }

    public void Dispose()
    {
        foreach (RecordingVisualEffectSlot slot in _slots)
        {
            slot.Dispose();
        }

        _slots.Clear();
    }
}

internal sealed class RecordingVisualEffectSlot : IVisualEffectSlot, System.IDisposable
{
    private readonly GameObject _object;

    public RecordingVisualEffectSlot(int sortingOrder)
    {
        SortingOrder = sortingOrder;
        _object = new UnityEngine.GameObject($"EffectSlot[{sortingOrder}]");
    }

    public GameObject? GameObject => _object;

    public int SortingOrder { get; }

    public Sprite? Sprite { get; private set; }

    public Color Color { get; private set; } = Color.white;

    public bool Enabled { get; private set; }

    public bool Released { get; set; }

    public void SetSprite(Sprite? sprite) => Sprite = sprite;

    public void SetColor(Color color) => Color = color;

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public void Dispose() => UnityEngine.Object.DestroyImmediate(_object);
}
