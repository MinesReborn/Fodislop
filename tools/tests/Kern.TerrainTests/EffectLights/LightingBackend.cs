using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Lighting;

// Recording backend, not the Unity lighting solver.
internal sealed class LightingEngine
{
    internal readonly Dictionary<int, (Vector2 Position, Color Color)> Sources = new();
    internal int RemovalCount;
    public void SetDynamicLight(int id, Vector2 position, Color color, float intensity)
    {
        if (intensity <= 0f) { RemoveDynamicLight(id); return; }
        Sources[id] = (position, color * intensity);
    }
    public void RemoveDynamicLight(int id) { if (Sources.Remove(id)) { RemovalCount++; } }
}
