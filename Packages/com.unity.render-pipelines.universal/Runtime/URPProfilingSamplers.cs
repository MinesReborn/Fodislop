using Unity.Profiling.LowLevel;

namespace UnityEngine.Rendering.Universal
{
    internal static class URPProfilingSamplers
    {
        // CPU

        public static readonly ProfilingSampler UniversalRenderTotal = ProfilingSampler.Create(nameof(UniversalRenderTotal), MarkerFlags.Default);

        public static readonly ProfilingSampler UpdateVolumeFramework = ProfilingSampler.Create(nameof(UpdateVolumeFramework), MarkerFlags.Default);

        public static readonly ProfilingSampler RenderCameraStack = ProfilingSampler.Create(nameof(RenderCameraStack), MarkerFlags.Default);

        // GPU

        public static readonly ProfilingSampler AdditionalLightsShadow = ProfilingSampler.Create(nameof(AdditionalLightsShadow), MarkerFlags.Default);

        public static readonly ProfilingSampler ColorGradingLUT = ProfilingSampler.Create(nameof(ColorGradingLUT), MarkerFlags.Default);

        public static readonly ProfilingSampler CopyColor = ProfilingSampler.Create(nameof(CopyColor), MarkerFlags.Default);

        public static readonly ProfilingSampler CopyDepth = ProfilingSampler.Create(nameof(CopyDepth), MarkerFlags.Default);

        public static readonly ProfilingSampler DrawDepthNormalPrepass = ProfilingSampler.Create(nameof(DrawDepthNormalPrepass), MarkerFlags.Default);

        public static readonly ProfilingSampler DepthPrepass = ProfilingSampler.Create(nameof(DepthPrepass), MarkerFlags.Default);

        public static readonly ProfilingSampler UpdateReflectionProbeAtlas = ProfilingSampler.Create(nameof(UpdateReflectionProbeAtlas), MarkerFlags.Default);

        // DrawObjectsPass

        public static readonly ProfilingSampler DrawOpaqueObjects = ProfilingSampler.Create(nameof(DrawOpaqueObjects), MarkerFlags.Default);

        public static readonly ProfilingSampler DrawTransparentObjects = ProfilingSampler.Create(nameof(DrawTransparentObjects), MarkerFlags.Default);

        public static readonly ProfilingSampler DrawScreenSpaceUI = ProfilingSampler.Create(nameof(DrawScreenSpaceUI), MarkerFlags.Default);

        // Full Record Render Graph

        public static readonly ProfilingSampler RecordRenderGraph = ProfilingSampler.Create(nameof(RecordRenderGraph), MarkerFlags.Default);

        public static readonly ProfilingSampler LightCookies = ProfilingSampler.Create(nameof(LightCookies), MarkerFlags.Default);

        public static readonly ProfilingSampler MainLightShadow = ProfilingSampler.Create(nameof(MainLightShadow), MarkerFlags.Default);

        public static readonly ProfilingSampler SSAO = ProfilingSampler.Create(nameof(SSAO), MarkerFlags.Default);

        public static readonly ProfilingSampler SSR = ProfilingSampler.Create(nameof(SSR), MarkerFlags.Default);

        // PostProcessPass

        public static readonly ProfilingSampler DrawMotionVectors = ProfilingSampler.Create(nameof(DrawMotionVectors), MarkerFlags.Default);

        public static readonly ProfilingSampler BlitFinalToBackBuffer = ProfilingSampler.Create(nameof(BlitFinalToBackBuffer), MarkerFlags.Default);

        public static readonly ProfilingSampler DrawSkybox = ProfilingSampler.Create(nameof(DrawSkybox), MarkerFlags.Default);

        // PostProcessPass — top-level pass markers

        public static readonly ProfilingSampler StopNaNs = ProfilingSampler.Create(nameof(StopNaNs), MarkerFlags.Default);

        public static readonly ProfilingSampler SMAAEdgeDetection = ProfilingSampler.Create(nameof(SMAAEdgeDetection), MarkerFlags.Default);

        public static readonly ProfilingSampler SMAABlendWeight = ProfilingSampler.Create(nameof(SMAABlendWeight), MarkerFlags.Default);

        public static readonly ProfilingSampler SMAANeighborhoodBlend = ProfilingSampler.Create(nameof(SMAANeighborhoodBlend), MarkerFlags.Default);

        public static readonly ProfilingSampler GaussianDepthOfField = ProfilingSampler.Create(nameof(GaussianDepthOfField), MarkerFlags.Default);

        public static readonly ProfilingSampler BokehDepthOfField = ProfilingSampler.Create(nameof(BokehDepthOfField), MarkerFlags.Default);

        public static readonly ProfilingSampler MotionBlur = ProfilingSampler.Create(nameof(MotionBlur), MarkerFlags.Default);

        public static readonly ProfilingSampler PaniniProjection = ProfilingSampler.Create(nameof(PaniniProjection), MarkerFlags.Default);

        public static readonly ProfilingSampler UberPostProcess = ProfilingSampler.Create(nameof(UberPostProcess), MarkerFlags.Default);

        public static readonly ProfilingSampler Bloom = ProfilingSampler.Create(nameof(Bloom), MarkerFlags.Default);

        public static readonly ProfilingSampler LensFlareDataDrivenComputeOcclusion = ProfilingSampler.Create(nameof(LensFlareDataDrivenComputeOcclusion), MarkerFlags.Default);

        public static readonly ProfilingSampler LensFlareDataDriven = ProfilingSampler.Create(nameof(LensFlareDataDriven), MarkerFlags.Default);

        public static readonly ProfilingSampler LensFlareScreenSpace = ProfilingSampler.Create(nameof(LensFlareScreenSpace), MarkerFlags.Default);

        // PostProcessPass RenderGraph — hidden from Rendering Debugger Detailed Stats

        [HideInDebugUI] public static readonly ProfilingSampler SMAAMaterialSetup = ProfilingSampler.Create(nameof(SMAAMaterialSetup), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler SetupDoF = ProfilingSampler.Create(nameof(SetupDoF), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFComputeCOC = ProfilingSampler.Create(nameof(DOFComputeCOC), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFDownscalePrefilter = ProfilingSampler.Create(nameof(DOFDownscalePrefilter), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFBlurH = ProfilingSampler.Create(nameof(DOFBlurH), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFBlurV = ProfilingSampler.Create(nameof(DOFBlurV), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFBlurBokeh = ProfilingSampler.Create(nameof(DOFBlurBokeh), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFPostFilter = ProfilingSampler.Create(nameof(DOFPostFilter), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler DOFComposite = ProfilingSampler.Create(nameof(DOFComposite), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler TAA = ProfilingSampler.Create(nameof(TAA), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler TAACopyHistory = ProfilingSampler.Create(nameof(TAACopyHistory), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler BloomSetup = ProfilingSampler.Create(nameof(BloomSetup), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler BloomPrefilter = ProfilingSampler.Create(nameof(BloomPrefilter), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler BloomDownsample = ProfilingSampler.Create(nameof(BloomDownsample), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler BloomUpsample = ProfilingSampler.Create(nameof(BloomUpsample), MarkerFlags.VerbosityAdvanced);

        [HideInDebugUI] public static readonly ProfilingSampler UberPostSetupBloomPass = ProfilingSampler.Create(nameof(UberPostSetupBloomPass), MarkerFlags.VerbosityAdvanced);
    }
}
