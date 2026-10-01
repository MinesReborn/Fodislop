using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public class Light2DProvider : Provider2D
    {
        /// <returns>The GUIContent used in the Light2D Light Type dropdown.</returns>
        public virtual GUIContent ProviderName() { return new GUIContent("Custom Light Provider", "Implemented by " + this.GetType().Name); }

        /// <returns>The mesh used for rendering.</returns>
        public virtual Mesh GetMesh() { return null; }

        internal override GUIContent Internal_ProviderName(string componentName) { return ProviderName(); }
    }
}
