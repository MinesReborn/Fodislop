using System;
using System.Reflection;
using Unity.Collections;


namespace UnityEngine.Rendering.Universal
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SupportedOnRendererAttribute : Attribute
    {
        public Type[] rendererTypes { get; }

        /// <param name="renderer">The compatible renderer to set.</param>
        public SupportedOnRendererAttribute(Type renderer)
            : this(new[] { renderer }) {}

        /// <param name="renderers">The compatible renderer(s) to set.</param>
        public SupportedOnRendererAttribute(params Type[] renderers)
        {
            if (renderers == null)
            {
                Debug.LogError($"The {nameof(SupportedOnRendererAttribute)} parameters cannot be null.");
                rendererTypes = Array.Empty<Type>();
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !typeof(ScriptableRendererData).IsAssignableFrom(r))
                {
                    Debug.LogError($"The {nameof(SupportedOnRendererAttribute)} Attribute targets an invalid {nameof(ScriptableRendererData)}. One of the types cannot be assigned from {nameof(ScriptableRendererData)}.");
                    rendererTypes = Array.Empty<Type>();
                    return;
                }
            }

            rendererTypes = renderers;
        }
    }
}
