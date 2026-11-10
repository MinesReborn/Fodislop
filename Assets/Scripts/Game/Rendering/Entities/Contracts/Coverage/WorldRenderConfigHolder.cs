#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.Game;

public static class WorldRenderConfigHolder
{
    public const float SpriteAlphaCull = 0.003f;
    public const float GlowFieldThreshold = 0.05f;

    // Доля альбедо, которая уходит в glow-поле как эмиссия спрайта. Полное
    // альбедо транспорт умножал на GlowScale = 12, и здания засвечивали себя,
    // игроков и эффекты. 0.25 даёт в поле ≈3× альбедо: здание светится,
    // но не выжигает окружение белым.
    public const float EntityGlowEmission = 0.25f;
    public const float SurfaceFieldThreshold = 0.05f;
}
