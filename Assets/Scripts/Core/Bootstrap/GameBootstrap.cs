#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Lifecycle;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Fodinae.Core
{
    public sealed class GameBootstrap : IPostStartable
    {
        private readonly Scene _ownScene;
        private readonly GameLifetimeScope _scope;
        private readonly SceneTransitionTicket _ticket;
        private readonly AsyncOperationSupervisor _operations;
        private readonly GameStartupPipeline _pipeline;

        public GameBootstrap(
            Scene ownScene,
            GameLifetimeScope scope,
            SceneTransitionTicket ticket,
            AsyncOperationSupervisor operations,
            GameStartupPipeline pipeline)
        {
            _ownScene = ownScene;
            _scope = scope;
            _ticket = ticket;
            _operations = operations;
            _pipeline = pipeline;
            _ticket.Attach(_ownScene);
        }

        public void PostStart()
        {
            _operations.Run("game_startup", _ => StartAsync());
        }

        private async UniTask StartAsync()
        {
            CancellationToken scopeToken = _scope.destroyCancellationToken;
            try
            {
                await _ticket.WaitForActivationAsync()
                    .AttachExternalCancellation(scopeToken);
                _scope.ActivateSceneServices();
                GameStartupReport report = _pipeline.Initialize();
                _ticket.MarkStartupReady();
                await _pipeline.WaitUntilReadyAsync(_ticket, report, scopeToken);
                _scope.MarkReady();
                _ticket.MarkPresentationReady();
            }
            catch (OperationCanceledException) when (scopeToken.IsCancellationRequested)
            {
                _ticket.Fail(new OperationCanceledException(
                    $"Game scene '{_ownScene.name}' was destroyed during startup."));
            }
            catch (Exception exception)
            {
                _scope.MarkFailed(exception);
                _ticket.Fail(exception);
            }
        }

    }
}
