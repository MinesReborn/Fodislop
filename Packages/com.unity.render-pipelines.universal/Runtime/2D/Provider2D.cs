using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public abstract class Provider2D
    {
        /// <returns>The menu priority for this provider.</returns>
        public virtual int MenuPriority() { return 0; }

        /// <returns>True if the provider uses component data; otherwise, false.</returns>
        public virtual bool UsesComponentData() { return true; }

        public virtual void OnAwake() { }

#if UNITY_EDITOR
        public virtual void OnSelected() { }
        public virtual void OnDrawGizmos(Transform transform) {}
#endif

        internal abstract GUIContent Internal_ProviderName(string componentName);
        internal virtual bool Internal_IsRequiredComponentData(Component sourceComponent) { return sourceComponent is Transform; }
    }
}
