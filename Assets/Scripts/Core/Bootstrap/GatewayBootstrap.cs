#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.UI;
using VContainer.Unity;

namespace Kern.Core;

public sealed class GatewayBootstrap : IStartable
{
    private readonly GatewayLifetimeScope _scope;
    private readonly GatewayController _controller;
    private readonly IAudioSystem _audioSystem;
    private readonly SceneTransitionTicket _ticket;
    private readonly AsyncOperationSupervisor _operations;

    public GatewayBootstrap(
        GatewayLifetimeScope scope,
        GatewayController controller,
        IAudioSystem audioSystem,
        SceneTransitionTicket ticket,
        AsyncOperationSupervisor operations)
    {
        _scope = scope;
        _controller = controller;
        _audioSystem = audioSystem;
        _ticket = ticket;
        _operations = operations;
        _ticket.Attach(_scope.gameObject.scene);
    }

    public void Start()
    {
        _operations.Run("gateway_startup", _ => StartAsync());
    }

    private async UniTask StartAsync()
    {
        try
        {
            await _ticket.WaitForActivationAsync();
            _controller.InitializeScene();
            // Presentation readiness requires the required audio banks to be
            // resident: scene audio must be live the moment the scene is shown.
            await _audioSystem.WaitUntilBanksReadyAsync(_scope.destroyCancellationToken);
            _ticket.MarkStartupReady();
            _ticket.MarkPresentationReady();
        }
        catch (Exception exception)
        {
            _ticket.Fail(exception);
        }
    }
}
