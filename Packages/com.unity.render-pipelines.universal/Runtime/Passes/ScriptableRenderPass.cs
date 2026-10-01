using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="ConfigureInput"/>
    [Flags]
    public enum ScriptableRenderPassInput
    {
        None = 0,

        Depth = 1 << 0,

        Normal = 1 << 1,

        Color = 1 << 2,

        Motion = 1 << 3,
    }

    // Note: Spaced built-in events so we can add events in between them
    // We need to leave room as we sort render passes based on event.
    // Users can also inject render pass events in a specific point by doing RenderPassEvent + offset

    public enum RenderPassEvent
    {
        BeforeRendering = 0,

        BeforeRenderingShadows = 50,

        AfterRenderingShadows = 100,

        BeforeRenderingPrePasses = 150,

        AfterRenderingPrePasses = 200,

        BeforeRenderingGbuffer = 210,

        AfterRenderingGbuffer = 220,

        BeforeRenderingDeferredLights = 230,

        AfterRenderingDeferredLights = 240,

        BeforeRenderingOpaques = 250,

        AfterRenderingOpaques = 300,

        BeforeRenderingSkybox = 350,

        AfterRenderingSkybox = 400,

        BeforeRenderingTransparents = 450,

        AfterRenderingTransparents = 500,

        BeforeRenderingPostProcessing = 550,

        AfterRenderingPostProcessing = 600,

        AfterRendering = 1000,
    }

    internal enum FramebufferFetchEvent
    {
        None = 0,
        FetchGbufferInDeferred = 1
    }

    internal static class RenderPassEventsEnumValues
    {
        // we cache the values in this array at construction time to avoid runtime allocations, which we would cause if we accessed valuesInternal directly
        [NoAutoStaticsCleanup] // Clearing the array is not necessary when entering play mode because it's reflection data.
        public static readonly int[] values;

        static RenderPassEventsEnumValues()
        {
            System.Array valuesInternal = Enum.GetValues(typeof(RenderPassEvent));

            values = new int[valuesInternal.Length];

            int index = 0;
            foreach (int value in valuesInternal)
            {
                values[index] = value;
                index++;
            }
        }
    }

    public abstract partial class ScriptableRenderPass : IRenderGraphRecorder
    {
        public RenderPassEvent renderPassEvent { get; set; }

        /// <seealso cref="ConfigureInput"/>
        public ScriptableRenderPassInput input => m_Input;

        public bool requiresIntermediateTexture { get; set; }

        private ProfilingSampler m_ProfingSampler;
        private string m_PassName;
        
        protected internal ProfilingSampler profilingSampler
        {
            get
            {

#if UNITY_INCLUDE_INSTRUMENTATION
                return m_ProfingSampler;
#else
                return null;
#endif
            }
            set
            {
                m_ProfingSampler = value;
                m_PassName = (value != null) ? value.name : this.GetType().Name;
            }
        }

        protected internal string passName{ get { return m_PassName; } }

        internal bool isBlitRenderPass { get; set; }
        
        // index to track the position in the current frame
        internal int renderPassQueueIndex { get; set; }

        internal NativeArray<int> m_ColorAttachmentIndices;
        internal NativeArray<int> m_InputAttachmentIndices;

        internal GraphicsFormat[] renderTargetFormat { get; set; }

        ScriptableRenderPassInput m_Input = ScriptableRenderPassInput.None;

        static internal DebugHandler GetActiveDebugHandler(UniversalCameraData cameraData)
        {
            var debugHandler = cameraData.renderer.DebugHandler;
            if ((debugHandler != null) && debugHandler.IsActiveForCamera(cameraData.isPreviewCamera))
                return debugHandler;
            return null;
        }

        public ScriptableRenderPass()            
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            profilingSampler = new ProfilingSampler(this.GetType().Name);
        }

        /// <param name="passInput">ScriptableRenderPassInput containing information about what requirements the pass needs.</param>
        /// <seealso cref="ScriptableRendererFeature.AddRenderPasses"/>
        public void ConfigureInput(ScriptableRenderPassInput passInput)
        {
            m_Input = passInput;
        }

        /// <param name="cmd">Use this CommandBuffer to cleanup any generated data</param>
        public virtual void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        /// <inheritdoc cref="IRenderGraphRecorder.RecordRenderGraph"/>
        public virtual void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Debug.LogWarning("The render pass " + this.ToString() + " does not have an implementation of the RecordRenderGraph method. Please implement this method, or consider turning on Compatibility Mode (RenderGraph disabled) in the menu Edit > Project Settings > Graphics > URP. Otherwise the render pass will have no effect. For more information, refer to https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest/index.html?subfolder=/manual/customizing-urp.html.");
        }
        
        /// <param name="shaderTagId">Shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns>Returns the draw settings created.</returns>
        /// <seealso cref="DrawingSettings"/>
        public DrawingSettings CreateDrawingSettings(ShaderTagId shaderTagId, ref RenderingData renderingData, SortingCriteria sortingCriteria)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            return RenderingUtils.CreateDrawingSettings(shaderTagId, universalRenderingData, cameraData, lightData, sortingCriteria);
        }

        /// <param name="shaderTagId">Shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="cameraData">Current camera state.</param>
        /// <param name="lightData">Current light state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns>Returns the draw settings created.</returns>
        /// <seealso cref="DrawingSettings"/>
        public DrawingSettings CreateDrawingSettings(ShaderTagId shaderTagId, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData, SortingCriteria sortingCriteria)
        {
            return RenderingUtils.CreateDrawingSettings(shaderTagId, renderingData, cameraData, lightData, sortingCriteria);
        }

        /// <param name="shaderTagIdList">List of shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns>Returns the draw settings created.</returns>
        /// <seealso cref="DrawingSettings"/>
        public DrawingSettings CreateDrawingSettings(List<ShaderTagId> shaderTagIdList,
            ref RenderingData renderingData, SortingCriteria sortingCriteria)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            return RenderingUtils.CreateDrawingSettings(shaderTagIdList, universalRenderingData, cameraData, lightData, sortingCriteria);
        }

        /// <param name="shaderTagIdList">List of shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="cameraData">Current camera state.</param>
        /// <param name="lightData">Current light state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns>Returns the draw settings created.</returns>
        /// <seealso cref="DrawingSettings"/>
        public DrawingSettings CreateDrawingSettings(List<ShaderTagId> shaderTagIdList,
            UniversalRenderingData renderingData, UniversalCameraData cameraData,
            UniversalLightData lightData, SortingCriteria sortingCriteria)
        {
            return RenderingUtils.CreateDrawingSettings(shaderTagIdList, renderingData, cameraData, lightData, sortingCriteria);
        }

        /// <param name="lhs"></param>
        /// <param name="rhs"></param>
        /// <returns></returns>
        public static bool operator <(ScriptableRenderPass lhs, ScriptableRenderPass rhs)
        {
            return lhs.renderPassEvent < rhs.renderPassEvent;
        }

        /// <param name="lhs"></param>
        /// <param name="rhs"></param>
        /// <returns></returns>
        public static bool operator >(ScriptableRenderPass lhs, ScriptableRenderPass rhs)
        {
            return lhs.renderPassEvent > rhs.renderPassEvent;
        }

        internal static int GetRenderPassEventRange(RenderPassEvent renderPassEvent)
        {
            int numEvents = RenderPassEventsEnumValues.values.Length;
            int currentIndex = 0;

            // find the index of the renderPassEvent in the values array
            for(int i = 0; i < numEvents; ++i)
            {
                if (RenderPassEventsEnumValues.values[currentIndex] == (int)renderPassEvent)
                    break;

                currentIndex++;
            }

            if (currentIndex >= numEvents)
            {
                Debug.LogError("GetRenderPassEventRange: invalid renderPassEvent value cannot be found in the RenderPassEvent enumeration");
                return 0;
            }

            if (currentIndex + 1 >= numEvents)
                return 50; // if this was the last event in the enum, then add 50 as the range

            int nextValue = RenderPassEventsEnumValues.values[currentIndex + 1];

            return nextValue - (int) renderPassEvent;
        }
    }
}
