using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public static class URP2D_GraphicsExtensions
    {
        /// <param name="meshRenderer"> The <see cref="MeshRenderer"/> instance to query.</param>
        /// <returns>Returns the SpriteMaskInteraction</returns>
        public static SpriteMaskInteraction GetSpriteMaskInteraction(this MeshRenderer meshRenderer) { return meshRenderer.Internal_GetSpriteMaskInteraction(); }

        /// <param name="skinnedMeshRenderer"> The <see cref="SkinnedMeshRenderer"/> instance to query.</param>
        /// <returns>Returns the SpriteMaskInteraction</returns>
        public static SpriteMaskInteraction GetSpriteMaskInteraction(this SkinnedMeshRenderer skinnedMeshRenderer) { return skinnedMeshRenderer.Internal_GetSpriteMaskInteraction(); }

        /// <param name="meshRenderer"> The <see cref="MeshRenderer"/> instance to modify.</param>
        /// <param name="maskInteraction"> The mask interaction state to set.</param>
        public static void SetSpriteMaskInteraction(this MeshRenderer meshRenderer, SpriteMaskInteraction maskInteraction) { meshRenderer.Internal_SetSpriteMaskInteraction(maskInteraction); }

        /// <param name="skinnedMeshRenderer"> The <see cref="SkinnedMeshRenderer"/> instance to modify.</param>
        /// <param name="maskInteraction"> The mask interaction state to set.</param>
        public static void SetSpriteMaskInteraction(this SkinnedMeshRenderer skinnedMeshRenderer, SpriteMaskInteraction maskInteraction) { skinnedMeshRenderer.Internal_SetSpriteMaskInteraction(maskInteraction); }
    }
}
