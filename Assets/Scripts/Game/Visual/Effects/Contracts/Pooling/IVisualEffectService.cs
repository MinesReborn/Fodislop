#nullable enable

using UnityEngine;

namespace Kern.Core.Interfaces;
public interface IVisualEffectSlot
{
    GameObject? GameObject { get; }

    void SetSprite(Sprite? sprite);

    void SetColor(Color color);

    void SetEnabled(bool enabled);
}

public interface IVisualEffectService
{
    IVisualEffectSlot? Acquire(int sortingOrder = -500);
    void Release(IVisualEffectSlot slot);
}
