#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing
{
    /// <summary>
    /// Packs volume components and the runtime LUT into the compute-shader
    /// uniforms of <see cref="PostProcessPassData"/>.
    /// </summary>
    internal static class PostProcessPassDataAssembler
    {
        internal static void BuildPassDescriptors(
            TextureDesc activeColorDesc,
            RenderTextureDescriptor cameraTargetDescriptor,
            out int width,
            out int height,
            out TextureDesc intermediateDesc)
        {
            width = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.width
                : cameraTargetDescriptor.width;
            height = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.height
                : cameraTargetDescriptor.height;
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            intermediateDesc = activeColorDesc;
            intermediateDesc.sizeMode = TextureSizeMode.Explicit;
            intermediateDesc.width = width;
            intermediateDesc.height = height;
            intermediateDesc.depthBufferBits = 0;
            intermediateDesc.msaaSamples = MSAASamples.None;
            intermediateDesc.bindTextureMS = false;
            intermediateDesc.enableRandomWrite = true;
            intermediateDesc.useMipMap = false;
            intermediateDesc.autoGenerateMips = false;
            intermediateDesc.clearBuffer = false;
        }

        internal static void FillPassComponents(
            PostProcessPassData passData,
            VignetteComponent vignette,
            EigengrauComponent eigengrau,
            bool vignetteActive,
            bool eigengrauActive,
            bool HDROutput,
            ColorGamut HDRGamut,
            float paperWhite,
            float peakNits)
        {
            passData.VignetteActive = vignetteActive;
            passData.VignetteIntensity = vignette.intensity.value;
            passData.VignetteColor = vignette.color.value;
            passData.VignetteSmoothness = vignette.smoothness.value;
            passData.VignetteCenter = vignette.center.value;

            passData.DisplayPaperWhiteNits = paperWhite;
            passData.DisplayPeakRelative = HDROutput ? peakNits / paperWhite : 0f;
            passData.HDROutput = HDROutput;
            passData.HDRGamut = HDRGamut;

            passData.EigengrauActive = eigengrauActive;
            passData.EigengrauIntensity = eigengrau.intensity.value;
            passData.EigengrauColor = eigengrau.color.value;
            passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
            passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
            // Амплитуда — не параметр эффекта, а авторская величина: читается прямо из PostProcessLook.
            passData.EigengrauNoiseAmplitude = PostProcessLook.FilmGrain.EigengrauNoiseAmplitude;
        }

        internal static void FillLUT(PostProcessPassData passData, bool lutActive)
        {
            ColorGradeCubeLUT? lut = lutActive ? PostProcessRuntimeState.LUT : null;
            passData.LUT3D = lut?.Texture3D;
            passData.LUTIntensity = lut == null ? 0f : PostProcessRuntimeState.LUTIntensity;
            passData.LUTColorSpace = (int)PostProcessRuntimeState.LUTColorSpace;
            passData.LUTDomainMin = lut?.DomainMin ?? Vector3.zero;
            passData.LUTDomainMax = lut?.DomainMax ?? Vector3.one;
        }
    }
}
