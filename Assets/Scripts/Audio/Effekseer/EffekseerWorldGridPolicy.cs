#nullable enable

using UnityEngine;

namespace Kern.Effekseer;

/// <summary>
/// Selects the shader backend that participates in the world's vertex grid.
/// Runs before Effekseer scene initialization, without mutating its settings asset.
/// </summary>
internal static class EffekseerWorldGridPolicy
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        global::Effekseer.EffekseerSystem.WorldGridRendering = true;
    }
}
