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
            // Вход и онбординг — слой поверх меню (MenuGate), отдельной сцены ворот нет.
            builder.Register<MenuGate>(Lifetime.Singleton);
            // Окно настроек меню меняет разрешение, синхронизацию и HDR; применение настроек дисплея при старте — за игрой и Bootstrap
            builder.Register<Kern.Rendering.Display.DisplayManager>(Lifetime.Singleton);
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
