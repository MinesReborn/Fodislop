#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Kern.Core.Interfaces;
public interface ISceneNavigator
{
    string? CurrentSceneName { get; }

    event Action<SceneTransitionStatus>? TransitionChanged;

    UniTask TransitionAsync(string sceneName, CancellationToken cancellationToken = default);
}
