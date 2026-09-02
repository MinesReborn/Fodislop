#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Scene-level navigation owned by the application composition root.
    /// Networking and other lower layers resolve this instead of reaching
    /// into <c>BootstrapLifetimeScope</c>, keeping the transition state machine
    /// an implementation detail of the bootstrap layer.
    /// </summary>
    public interface ISceneNavigator
    {
        string? CurrentSceneName { get; }

        event Action<SceneTransitionStatus>? TransitionChanged;

        UniTask TransitionAsync(string sceneName, CancellationToken cancellationToken = default);
    }
}
