#nullable enable

using UnityEngine;
using VContainer.Unity;

namespace Kern.Core
{
    public abstract class TransitionSceneLifetimeScope : LifetimeScope
    {
        protected override void Awake()
        {
            // The parent is supplied by LifetimeScope.EnqueueParent during the
            // additive load. A missing runtime parent is a hard contract error.
            base.Awake();
        }
    }
}
