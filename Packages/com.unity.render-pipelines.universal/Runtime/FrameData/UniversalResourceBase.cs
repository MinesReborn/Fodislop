using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public abstract class UniversalResourceDataBase : ContextItem
    {
        internal enum ActiveID
        {
            /// <summary>The camera buffer.</summary>
            Camera,

            /// <summary>The backbuffer.</summary>
            BackBuffer
        }

        internal bool isAccessible { get; set; }

        internal void InitFrame()
        {
            isAccessible = true;
        }

        internal void EndFrame()
        {
            isAccessible = false;
        }

        /// <param name="handle">Handle to update.</param>
        /// <param name="newHandle">Handle of the new data.</param>
        protected void CheckAndSetTextureHandle(ref TextureHandle handle, in TextureHandle newHandle)
        {
            if (!CheckAndWarnAboutAccessibility())
                return;

            handle = newHandle;
        }

        /// <param name="handle">Handle to the texture you want to retrieve</param>
        /// <returns>Returns the handle if the texture is accessible and a null handle otherwise.</returns>
        protected TextureHandle CheckAndGetTextureHandle(ref TextureHandle handle)
        {
            if (!CheckAndWarnAboutAccessibility())
                return TextureHandle.nullHandle;

            return handle;
        }

        /// <param name="handle">Handles to update.</param>
        /// <param name="newHandle">Handles of the new data.</param>
        protected void CheckAndSetTextureHandle(ref TextureHandle[] handle, in TextureHandle[] newHandle)
        {
            if (!CheckAndWarnAboutAccessibility())
                return;

            if (handle == null || handle.Length != newHandle.Length)
                handle = new TextureHandle[newHandle.Length];

            for (int i = 0; i < newHandle.Length; i++)
                handle[i] = newHandle[i];
        }

        /// <param name="handle">Handles to the texture you want to retrieve</param>
        /// <returns>Returns the handles if the texture is accessible and a null handle otherwise.</returns>
        protected TextureHandle[] CheckAndGetTextureHandle(ref TextureHandle[] handle)
        {
            if (!CheckAndWarnAboutAccessibility())
                return new []{TextureHandle.nullHandle};

            return handle;
        }

        /// <returns>Returns true if the texture is accessible and false otherwise.</returns>
        protected bool CheckAndWarnAboutAccessibility()
        {
            if (!isAccessible)
                Debug.LogError("Trying to access Universal Resources outside of the current frame setup.");

            return isAccessible;
        }
    }
}
