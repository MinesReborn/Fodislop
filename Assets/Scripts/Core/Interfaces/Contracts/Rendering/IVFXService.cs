#nullable enable

using Kern.Game;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface IVfxSlot
{
    GameObject? GameObject { get; }

    void SetSprite(Sprite? sprite);

    void SetColor(Color color);

    void SetEnabled(bool enabled);
}

public interface IVfxService
{
    IVfxSlot? Acquire(VfxType vfxType);
    void Release(IVfxSlot slot);
    void Preload(VfxType vfxType, int count);
}
