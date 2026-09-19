#nullable enable

using System;
using Kern.Core.Lifecycle;
using Kern.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Kern.Core
{
    public sealed class GatewayLifetimeScope : TransitionSceneLifetimeScope
    {
        [SerializeField]
        private UIDocument _document = null!;
        [SerializeField]
        private GatewayController _controller = null!;

        protected override void Configure(IContainerBuilder builder)
        {
            if (Parent is not BootstrapLifetimeScope)
            {
                throw new InvalidOperationException(
                    "Gateway scope requires BootstrapLifetimeScope as its runtime parent.");
            }

            ValidateReference(_document, nameof(_document));
            ValidateReference(_controller, nameof(_controller));
            ValidateReferenceScene(_document, nameof(_document));
            ValidateReferenceScene(_controller, nameof(_controller));

            // This scope is already registered by LifetimeScope.InstallTo as
            // RegisterInstance<LifetimeScope>(this).AsSelf() — an explicit
            // RegisterInstance(this) here duplicates the concrete contract and
            // VContainer rejects the conflicting singleton.
            builder.RegisterComponent(_document);
            builder.RegisterComponent(_controller);
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
            builder.RegisterEntryPoint<GatewayBootstrap>();
        }

        private void ValidateReference(UnityEngine.Object reference, string fieldName)
        {
            if (reference == null)
            {
                throw new InvalidOperationException($"Gateway scene scope is missing serialized {fieldName} reference.");
            }
        }

        private void ValidateReferenceScene(Component reference, string fieldName)
        {
            if (reference.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException($"Gateway scope reference {fieldName} belongs to another scene.");
            }
        }
    }
}
