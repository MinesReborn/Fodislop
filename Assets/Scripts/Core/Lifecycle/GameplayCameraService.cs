#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Core.Lifecycle;

public sealed class GameplayCameraService : IGameplayCamera
{
    public Camera Camera { get; }

    public GameplayCameraService(Camera camera)
    {
        Camera = camera ?? throw new System.ArgumentNullException(nameof(camera));
    }
}
