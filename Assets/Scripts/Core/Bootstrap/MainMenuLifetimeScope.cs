#nullable enable

using System;
using Fodinae;
using Fodinae.Core.Lifecycle;
using Fodinae.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    public sealed class MainMenuLifetimeScope : TransitionSceneLifetimeScope
    {
        [SerializeField]
        private UIDocument _document = null!;
        [SerializeField]
        private MainMenu _controller = null!;
        [SerializeField]
        private MenuStarfield _starfield = null!;
        [SerializeField]
        private MenuSceneryController _scenery = null!;

        /// <summary>Required serialized scenery references owned by the MainMenu scene.</summary>
        public MenuStarfield Starfield => _starfield;

        public MenuSceneryController Scenery => _scenery;

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
            ValidateReference(_starfield, nameof(_starfield));
            ValidateReference(_scenery, nameof(_scenery));
            ValidateReferenceScene(_starfield, nameof(_starfield));
            ValidateReferenceScene(_scenery, nameof(_scenery));
            if (_document.panelSettings == null)
            {
                throw new SceneContractException(
                    "MainMenu scene scope is missing serialized _document PanelSettings.");
            }

            // This scope is already registered by LifetimeScope.InstallTo as
            // RegisterInstance<LifetimeScope>(this).AsSelf() — an explicit
            // RegisterInstance(this) here duplicates the concrete contract and
            // VContainer rejects the conflicting singleton.
            builder.RegisterComponent(_document);
            builder.RegisterComponent(_controller);
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
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
