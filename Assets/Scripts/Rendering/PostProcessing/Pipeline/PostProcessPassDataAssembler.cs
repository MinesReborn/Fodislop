#nullable enable

using Kern.Core.Interfaces.WorldLighting;
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

        internal static Vector4 ComputeScreenToEmission(Camera camera, bool bloomActive)
        {
            if (!bloomActive)
            {
                return Vector4.zero;
            }

            Vector4 worldLightRect = Shader.GetGlobalVector(PostProcessShaderConstants.WorldLightRectID);
            if (worldLightRect.z <= 0.001f || worldLightRect.w <= 0.001f)
            {
                return Vector4.zero;
            }

            float camHeight = camera.orthographicSize * 2f;
            float camWidth = camHeight * camera.aspect;
            Vector3 camPos = camera.transform.position;
            float camMinX = camPos.x - camWidth * 0.5f;
            float camMinY = camPos.y - camHeight * 0.5f;

            float scaleX = camWidth / worldLightRect.z;
            float scaleY = camHeight / worldLightRect.w;
            float offsetX = (camMinX - worldLightRect.x) / worldLightRect.z;
            float offsetY = (camMinY - worldLightRect.y) / worldLightRect.w;

            if (LightingFieldOrientation.RowsTopDown)
            {
                scaleY = -scaleY;
                offsetY = 1f - offsetY;
            }

            return new Vector4(scaleX, scaleY, offsetX, offsetY);
        }

        internal static void FillPassComponents(
            PostProcessPassData passData,
            BloomComponent bloom,
            VignetteComponent vignette,
            EigengrauComponent eigengrau,
            bool bloomActive,
            int bloomLevels,
            bool vignetteActive,
            bool eigengrauActive,
            bool displayPass,
            bool hdrOutput,
            ColorGamut hdrGamut,
            float paperWhite,
            float peakNits)
        {
            passData.BloomActive = bloomActive;
            passData.BloomLevels = bloomLevels;
            passData.BloomThreshold = bloom.threshold.value;
            passData.BloomSoftKnee = bloom.softKnee.value;
            passData.BloomRadius = bloom.radius.value;
            passData.BloomScatter = bloom.scatter.value;
            passData.BloomTint = bloom.tint.value;
            passData.BloomIntensity = bloom.intensity.value;

            passData.VignetteActive = vignetteActive;
            passData.VignetteIntensity = vignette.intensity.value;
            passData.VignetteColor = vignette.color.value;
            passData.VignetteSmoothness = vignette.smoothness.value;
            passData.VignetteCenter = vignette.center.value;

            passData.DisplayPaperWhiteNits = paperWhite;
            passData.DisplayPeakRelative = hdrOutput ? peakNits / paperWhite : 0f;
            passData.HDROutput = displayPass && hdrOutput;
            passData.HDRGamut = hdrGamut;

            passData.EigengrauActive = eigengrauActive;
            passData.EigengrauIntensity = eigengrau.intensity.value;
            passData.EigengrauColor = eigengrau.color.value;
            passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
            passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
            // Амплитуда — не параметр эффекта, а авторская величина: читается прямо из PostProcessLook.
            passData.EigengrauNoiseAmplitude = PostProcessLook.FilmGrain.EigengrauNoiseAmplitude;
        }

        internal static void FillLut(PostProcessPassData passData, bool lutActive)
        {
            ColorGradeCubeLut? lut = lutActive ? PostProcessRuntimeState.Lut : null;
            passData.Lut3D = lut?.Texture3D;
            passData.LutIntensity = lut == null ? 0f : PostProcessRuntimeState.LutIntensity;
            passData.LutColorSpace = (int)PostProcessRuntimeState.LutColorSpace;
            passData.LutDomainMin = lut?.DomainMin ?? Vector3.zero;
            passData.LutDomainMax = lut?.DomainMax ?? Vector3.one;
        }
    }
}
