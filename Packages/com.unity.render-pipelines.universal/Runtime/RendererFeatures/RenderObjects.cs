using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Rendering.Universal
{
    [MovedFrom(true, "UnityEngine.Experimental.Rendering.Universal")]
    public enum RenderQueueType
    {
        Opaque,

        Transparent,
    }

    [ExcludeFromPreset]
    [MovedFrom(true, "UnityEngine.Experimental.Rendering.Universal")]
    [Tooltip("Render Objects simplifies the injection of additional render passes by exposing a selection of commonly used settings.")]
    [URPHelpURL("urp/renderer-features/renderer-feature-render-objects")]
    public class RenderObjects : ScriptableRendererFeature
    {
        [System.Serializable]
        public class RenderObjectsSettings
        {
            public string passTag = "RenderObjectsFeature";

            public RenderPassEvent Event = RenderPassEvent.AfterRenderingOpaques;

            public FilterSettings filterSettings = new FilterSettings();

            public Material overrideMaterial = null;

            public int overrideMaterialPassIndex = 0;

            public Shader overrideShader = null;

            public int overrideShaderPassIndex = 0;

            public enum OverrideMaterialMode
            {
                None,

                Material,

                Shader
            };

            public OverrideMaterialMode overrideMode = OverrideMaterialMode.Material; //default to Material as this was previously the only option

            public bool overrideDepthState = false;

            public CompareFunction depthCompareFunction = CompareFunction.LessEqual;

            public bool enableWrite = true;

            public StencilStateData stencilSettings = new StencilStateData();

            public CustomCameraSettings cameraSettings = new CustomCameraSettings();

            public bool depthInputAttachment = false;
        }

        [System.Serializable]
        public class FilterSettings
        {
            // TODO: expose opaque, transparent, all ranges as drop down

            public RenderQueueType RenderQueueType;

            public LayerMask LayerMask;

            public string[] PassNames;

            public FilterSettings()
            {
                RenderQueueType = RenderQueueType.Opaque;
                LayerMask = 0;
            }
        }

        [System.Serializable]
        public class CustomCameraSettings
        {
            public bool overrideCamera = false;

            public bool restoreCamera = true;

            public Vector4 offset;

            public float cameraFieldOfView = 60.0f;
        }

        public RenderObjectsSettings settings = new RenderObjectsSettings();

        RenderObjectsPass renderObjectsPass;

        /// <inheritdoc/>
        public override void Create()
        {
            FilterSettings filter = settings.filterSettings;

            // Render Objects pass doesn't support events before rendering prepasses.
            // The camera is not setup before this point and all rendering is monoscopic.
            // Events before BeforeRenderingPrepasses should be used for input texture passes (shadow map, LUT, etc) that doesn't depend on the camera.
            // These events are filtering in the UI, but we still should prevent users from changing it from code or
            // by changing the serialized data.
            if (settings.Event < RenderPassEvent.BeforeRenderingPrePasses)
                settings.Event = RenderPassEvent.BeforeRenderingPrePasses;

            renderObjectsPass = new RenderObjectsPass(settings.passTag, settings.Event, filter.PassNames,
                filter.RenderQueueType, filter.LayerMask, settings.cameraSettings);

            switch (settings.overrideMode)
            {
                case RenderObjectsSettings.OverrideMaterialMode.None:
                    renderObjectsPass.overrideMaterial = null;
                    renderObjectsPass.overrideShader = null;
                    break;
                case RenderObjectsSettings.OverrideMaterialMode.Material:
                    renderObjectsPass.overrideMaterial = settings.overrideMaterial;
                    renderObjectsPass.overrideMaterialPassIndex = settings.overrideMaterialPassIndex;
                    renderObjectsPass.overrideShader = null;
                    break;
                case RenderObjectsSettings.OverrideMaterialMode.Shader:
                    renderObjectsPass.overrideMaterial = null;
                    renderObjectsPass.overrideShader = settings.overrideShader;
                    renderObjectsPass.overrideShaderPassIndex = settings.overrideShaderPassIndex;
                    break;
            }

            if (settings.overrideDepthState)
                renderObjectsPass.SetDepthState(settings.enableWrite, settings.depthCompareFunction);

            if (settings.stencilSettings.overrideStencilState)
                renderObjectsPass.SetStencilState(settings.stencilSettings.stencilReference,
                    settings.stencilSettings.stencilCompareFunction, settings.stencilSettings.passOperation,
                    settings.stencilSettings.failOperation, settings.stencilSettings.zFailOperation);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview
                || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            bool enableDepthInputAttachment = settings.depthInputAttachment;
            renderObjectsPass.SetDepthInputAttachment(enableDepthInputAttachment);

            renderer.EnqueuePass(renderObjectsPass);
        }
    }
}
