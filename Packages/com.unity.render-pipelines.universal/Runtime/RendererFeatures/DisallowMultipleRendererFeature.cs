using System;

namespace UnityEngine.Rendering.Universal
{
    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleRendererFeature : Attribute
    {
        public string customTitle { private set; get; }

        /// <param name="customTitle">Sets the custom title for renderer feature.</param>
        public DisallowMultipleRendererFeature(string customTitle = null)
        {
            this.customTitle = customTitle;
        }
    }
}
