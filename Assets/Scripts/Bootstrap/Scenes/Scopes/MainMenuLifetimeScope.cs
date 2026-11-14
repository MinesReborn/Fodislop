#nullable enable

using System;
using Kern;
using Kern.Core.Lifecycle;
using Kern.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

using Kern.Diagnostics.Scenes;
using Kern.UI.Gateway;
using Kern.UI.Menu;
using Kern.UI.Screens;
using Kern.Core;


namespace Kern.Bootstrap.Scenes
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Core", sourceAssembly: null, sourceClassName: "MainMenuLifetimeScope")]
    public sealed class MainMenuLifetimeScope : TransitionSceneLifetimeScope
    {
        [SerializeField]
        private UIDocument _document = null!;
        [SerializeField]
        private MainMenu _controller = null!;

        protected override void Configure(IContainerBuilder builder)
        {
            if (Parent is not BootstrapLifetimeScope)
            {
                throw new InvalidOperationException(
                    "MainMenu scope requires BootstrapLifetimeScope as its runtime parent.");
            }

            ValidateReference(_document, nameof(_document));
            ValidateReference(_controller, nameof(_controller));
            ValidateReferenceScene(_document, nameof(_document));
            ValidateReferenceScene(_controller, nameof(_controller));
            if (_document.panelSettings == null)
            {
                throw new SceneContractException(
                    "MainMenu scene scope is missing serialized _document PanelSettings.");
            }

            Kern.UI.Screens.DynamicAtlasConfigurator.Apply(_document.panelSettings);

            // This scope is already registered by LifetimeScope.InstallTo as
            // RegisterInstance<LifetimeScope>(this).AsSelf() — an explicit
            // RegisterInstance(this) here duplicates the concrete contract and
            // VContainer rejects the conflicting singleton.
            builder.RegisterComponent(_document);
            builder.RegisterComponent(_controller);
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
            // Живое имя и уровень (PlayerStatsModel + PlayerIdentityProcessor)
            // зарегистрированы на постоянном Bootstrap-scope: пакеты приходят
            // в фазе гейтвея, до существования этой области. MainMenu наследует
            // IPlayerStats от родителя.
            // Окно настроек меню меняет разрешение, синхронизацию и HDR; применение настроек дисплея при старте — за игрой и Bootstrap
            builder.Register<Kern.Rendering.Display.DisplayManager>(Lifetime.Singleton);
            // Серверные окна до входа в мир: подключение живёт в Bootstrap, а
            // игровой PacketHandler появляется только в MainGame — окно
            // авторизации сервер присылает раньше. Диспетчер и презентер
            // умирают вместе со сценой меню, и в игре окна разбирает игровой
            // PacketHandler без двойной обработки.
            builder.Register<Kern.UI.Input.UIInputManager>(Lifetime.Singleton);
            builder.Register<Kern.Networking.Processing.WindowPacketProcessor>(Lifetime.Singleton);
            // Токен успешного входа приезжает в фазе гейтвея: игровой
            // AuthTokenProcessor появляется вместе с PacketHandler позже.
            builder.Register<Kern.Networking.Processing.AuthTokenProcessor>(Lifetime.Singleton);
            // Статусная процессорка фазы гейтвея: Pong (ответ на пинг сессии),
            // диалог устаревшего клиента — сервер отвечает ими до входа в мир.
            builder.Register<Kern.Networking.Status.NetworkStatusModel>(Lifetime.Singleton);
            builder.Register<Kern.Networking.Processing.StatusProcessor>(Lifetime.Singleton);
            // Онбординг — слой поверх меню; авторизация теперь серверное окно
            // (GatewayWindowProcessor), но ворота блокируют меню до её подтверждения.
            builder.Register<Kern.UI.Gateway.MenuGate>(Lifetime.Singleton);
            // Презентер меню — без состояния мира: его нет в этой сцене, и
            // держать окна до стабилизации мира нечему (VContainer default-
            // значения параметров конструктора не считает необязательными).
            builder.Register<Kern.UI.ServerWindows.ServerWindowPresenter>(
                resolver => new Kern.UI.ServerWindows.ServerWindowPresenter(
                    resolver.Resolve<Kern.Core.Localization.ILocalizationService>(),
                    resolver.Resolve<Kern.Core.Interfaces.IAssetLoader>(),
                    resolver.Resolve<Kern.UI.Input.UIInputManager>(),
                    resolver.Resolve<Kern.Core.Interfaces.INetworkService>(),
                    resolver.Resolve<UIDocument>(),
                    resolver.Resolve<Kern.Networking.Gateways.WindowCommandStream>(),
                    resolver.Resolve<Kern.IAsyncOperationSupervisor>(),
                    resolver.Resolve<Kern.AssetPipeline.Loading.IWebAssetLoader>()),
                Lifetime.Singleton);
            builder.RegisterEntryPoint<Kern.UI.ServerWindows.GatewayWindowProcessor>();
            builder.RegisterEntryPoint<MainMenuBootstrap>();
        }

        private void ValidateReference(UnityEngine.Object reference, string fieldName)
        {
            if (reference == null)
            {
                throw new SceneContractException($"MainMenu scene scope is missing serialized {fieldName} reference.");
            }
        }

        private void ValidateReferenceScene(Component reference, string fieldName)
        {
            if (reference.gameObject.scene != gameObject.scene)
            {
                throw new SceneContractException($"MainMenu scope reference {fieldName} belongs to another scene.");
            }
        }
    }
}
