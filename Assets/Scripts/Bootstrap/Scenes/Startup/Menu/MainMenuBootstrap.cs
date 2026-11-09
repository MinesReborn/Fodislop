#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Kern.Core.Lifecycle;
using Kern.UI;
using VContainer.Unity;

using Kern.UI.Menu;
using Kern.Core;


namespace Kern.Bootstrap.Scenes;

public sealed class MainMenuBootstrap : IStartable
{
    private readonly MainMenuLifetimeScope _scope;
    private readonly MainMenu _controller;
    private readonly SceneTransitionTicket _ticket;
    private readonly AsyncOperationSupervisor _operations;

    public MainMenuBootstrap(
        MainMenuLifetimeScope scope,
        MainMenu controller,
        SceneTransitionTicket ticket,
        AsyncOperationSupervisor operations)
    {
        _scope = scope;
        _controller = controller;
        _ticket = ticket;
        _operations = operations;
        _ticket.Attach(_scope.gameObject.scene);
    }

    public void Start()
    {
        _operations.Run("main_menu_startup", _ => StartAsync());
    }

    private async UniTask StartAsync()
    {
        try
        {
            await _ticket.WaitForActivationAsync();
            _controller.InitializeScene();
            _ticket.MarkStartupReady();
            // Меню показывается игроку только собранным.
            await _controller.WaitUntilReadyAsync(_scope.destroyCancellationToken);
            _ticket.MarkPresentationReady();
        }
        catch (Exception exception)
        {
            _ticket.Fail(exception);
        }
    }
}
