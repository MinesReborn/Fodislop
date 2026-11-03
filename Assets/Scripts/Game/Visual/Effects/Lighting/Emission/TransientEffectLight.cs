#nullable enable

using System;
using System.Threading;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Game.Visual.Effects.Lighting;

internal sealed class TransientEffectLight(LightingEngine lighting) : IDisposable
{
    private static int s_nextId;
    private readonly int _id = AllocateId();
    private bool _disposed;

    internal static int AllocateId() => Interlocked.Decrement(ref s_nextId);

    internal void Submit(Vector3 position, Vector3 scale, Color emission)
    {
        if (_disposed) { return; }
        emission *= Mathf.Abs(scale.x * scale.y);
        lighting.SetDynamicLight(_id, new Vector2(position.x, position.y), emission,
            emission.maxColorComponent > 0f ? 1f : 0f);
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (lighting != null) { lighting.RemoveDynamicLight(_id); }
    }
}
