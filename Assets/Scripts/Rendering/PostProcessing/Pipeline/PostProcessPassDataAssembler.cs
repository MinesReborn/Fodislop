#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Kern.Rendering.PostProcessing
{
    /// <summary>
    /// Packs the authored <see cref="ColorGradeSnapshot"/> into the compute-shader
    /// uniforms of <see cref="PostProcessPassData"/>: display grade, curve points,
    /// qualifier vectors and LUT binding. Scratch buffers live on the pass to
    /// avoid per-frame allocations.
    /// </summary>
    internal static class PostProcessPassDataAssembler
    {
        internal static void BuildPassDescriptors(
            TextureDesc activeColorDesc,
            RenderTextureDescriptor cameraTargetDescriptor,
            out int width,
            out int height,
            out TextureDesc intermediateDesc,
            out RenderTextureDescriptor historyDesc)
        {
            historyDesc = cameraTargetDescriptor;
            width = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.width
                : historyDesc.width;
            height = activeColorDesc.sizeMode == TextureSizeMode.Explicit
                ? activeColorDesc.height
                : historyDesc.height;
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

            historyDesc.width = width;
            historyDesc.height = height;
            historyDesc.graphicsFormat = activeColorDesc.colorFormat;
            historyDesc.depthBufferBits = 0;
            historyDesc.msaaSamples = 1;
            historyDesc.bindMS = false;
            historyDesc.enableRandomWrite = true;
        }
        internal readonly struct GradeScratch
        {
            internal readonly Vector4[] MasterCurvePoints;
            internal readonly Vector4[] RedCurvePoints;
            internal readonly Vector4[] GreenCurvePoints;
            internal readonly Vector4[] BlueCurvePoints;
            internal readonly Vector4[] HueVsHueCurvePoints;
            internal readonly Vector4[] HueVsSaturationCurvePoints;
            internal readonly Vector4[] HueVsLuminanceCurvePoints;
            internal readonly Vector4[] LuminanceVsSaturationCurvePoints;
            internal readonly Vector4[] SaturationVsSaturationCurvePoints;
            internal readonly Vector4[] QualifierHueSamples;

            internal GradeScratch(
                Vector4[] masterCurvePoints,
                Vector4[] redCurvePoints,
                Vector4[] greenCurvePoints,
                Vector4[] blueCurvePoints,
                Vector4[] hueVsHueCurvePoints,
                Vector4[] hueVsSaturationCurvePoints,
                Vector4[] hueVsLuminanceCurvePoints,
                Vector4[] luminanceVsSaturationCurvePoints,
                Vector4[] saturationVsSaturationCurvePoints,
                Vector4[] qualifierHueSamples)
            {
                MasterCurvePoints = masterCurvePoints;
                RedCurvePoints = redCurvePoints;
                GreenCurvePoints = greenCurvePoints;
                BlueCurvePoints = blueCurvePoints;
                HueVsHueCurvePoints = hueVsHueCurvePoints;
                HueVsSaturationCurvePoints = hueVsSaturationCurvePoints;
                HueVsLuminanceCurvePoints = hueVsLuminanceCurvePoints;
                LuminanceVsSaturationCurvePoints = luminanceVsSaturationCurvePoints;
                SaturationVsSaturationCurvePoints = saturationVsSaturationCurvePoints;
                QualifierHueSamples = qualifierHueSamples;
            }
        }

        internal static void FillPassComponents(
            PostProcessPassData passData,
            BloomComponent bloom,
            VignetteComponent vignette,
            ColorGradingComponent cg,
            EigengrauComponent eigengrau,
            MotionBlurComponent mb,
            bool bloomActive,
            int bloomLevels,
            bool vignetteActive,
            bool cgActive,
            bool eigengrauActive,
            bool temporalActive,
            bool mbActive,
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

            passData.CgActive = cgActive;
            passData.Exposure = cg.exposure.value;
            passData.ColorFilter = cg.colorFilter.value;
            passData.Contrast = cg.contrast.value;
            passData.Saturation = cg.saturation.value;

            passData.DisplayPaperWhiteNits = paperWhite;
            passData.DisplayPeakRelative = hdrOutput ? peakNits / paperWhite : 0f;
            passData.HDROutput = displayPass && hdrOutput;
            passData.HDRGamut = hdrGamut;

            passData.EigengrauActive = eigengrauActive;
            passData.EigengrauIntensity = eigengrau.intensity.value;
            passData.EigengrauColor = eigengrau.color.value;
            passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
            passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
            passData.EigengrauAnimationSpeed = eigengrau.animationSpeed.value;

            passData.MotionBlurHistory = passData.HistoryValid && displayPass && mbActive
                ? mb.intensity.value
                : 0f;
            passData.TemporalActive = temporalActive;
        }

        internal static void FillGradeTransport(
            PostProcessPassData passData,
            ColorGradeSnapshot grade,
            GradeScratch scratch)
        {
            passData.CdlSaturation = grade.CdlSaturation;
            passData.WhiteBalance = new Vector2(grade.Temperature, grade.Tint);
            passData.CdlSlope = grade.Slope;
            passData.CdlOffset = grade.Offset;
            passData.CdlPower = grade.Power;
            passData.CdlMaster = grade.CdlMaster;
            passData.PrimaryLift = grade.PrimaryLift;
            passData.PrimaryGamma = grade.PrimaryGamma;
            passData.PrimaryGain = grade.PrimaryGain;
            passData.PrimaryOffset = grade.PrimaryOffset;
            passData.PrimaryMaster = grade.PrimaryMaster;
            passData.Vibrance = grade.Vibrance;
            passData.Hue = grade.Hue;
            passData.ContrastControls = new Vector4(
                grade.Pivot,
                grade.Shadows,
                grade.Highlights,
                grade.Blacks);
            passData.ContrastControls2 = new Vector3(
                grade.Whites,
                grade.Toe,
                grade.Shoulder);

            // Тонмаппинг — за URP (Neutral SDR / Neutral BT2390 HDR через
            // HDROutputReconciler). Кастомний трансформ поверх нього дає
            // подвійне маплення, тому рантайм його не форсує: діє тільки
            // авторський вибір, дефолт — None (bypass).
            DisplayTransform displayTransform = grade.Transform;

            passData.DisplayGrade0 = new Vector4(
                grade.WhitePoint,
                grade.GreyOut,
                0f,
                (int)displayTransform);
            passData.DisplayGrade1 = new Vector4(
                grade.ShoulderPower,
                grade.ToePower,
                grade.ToeStops,
                0f);
            passData.GamutCompression = grade.GamutCompressionEnabled
                ? Mathf.Clamp01(grade.GamutCompressionStrength)
                : 0f;
            grade.MasterCurve.WriteShaderPoints(scratch.MasterCurvePoints);
            passData.MasterCurvePoints = scratch.MasterCurvePoints;
            grade.RedCurve.WriteShaderPoints(scratch.RedCurvePoints);
            passData.RedCurvePoints = scratch.RedCurvePoints;
            grade.GreenCurve.WriteShaderPoints(scratch.GreenCurvePoints);
            passData.GreenCurvePoints = scratch.GreenCurvePoints;
            grade.BlueCurve.WriteShaderPoints(scratch.BlueCurvePoints);
            passData.BlueCurvePoints = scratch.BlueCurvePoints;
            grade.HueVsHueCurve.WriteShaderPoints(scratch.HueVsHueCurvePoints);
            passData.HueVsHueCurvePoints = scratch.HueVsHueCurvePoints;
            grade.HueVsSaturationCurve.WriteShaderPoints(scratch.HueVsSaturationCurvePoints);
            passData.HueVsSaturationCurvePoints = scratch.HueVsSaturationCurvePoints;
            grade.HueVsLuminanceCurve.WriteShaderPoints(scratch.HueVsLuminanceCurvePoints);
            passData.HueVsLuminanceCurvePoints = scratch.HueVsLuminanceCurvePoints;
            grade.LuminanceVsSaturationCurve.WriteShaderPoints(scratch.LuminanceVsSaturationCurvePoints);
            passData.LuminanceVsSaturationCurvePoints = scratch.LuminanceVsSaturationCurvePoints;
            grade.SaturationVsSaturationCurve.WriteShaderPoints(scratch.SaturationVsSaturationCurvePoints);
            passData.SaturationVsSaturationCurvePoints = scratch.SaturationVsSaturationCurvePoints;
            passData.MasterCurvePointCount = grade.MasterCurve.PointCount;
            passData.RedCurvePointCount = grade.RedCurve.PointCount;
            passData.GreenCurvePointCount = grade.GreenCurve.PointCount;
            passData.BlueCurvePointCount = grade.BlueCurve.PointCount;
            passData.HueVsHueCurvePointCount = grade.HueVsHueCurve.PointCount;
            passData.HueVsSaturationCurvePointCount = grade.HueVsSaturationCurve.PointCount;
            passData.HueVsLuminanceCurvePointCount = grade.HueVsLuminanceCurve.PointCount;
            passData.LuminanceVsSaturationCurvePointCount = grade.LuminanceVsSaturationCurve.PointCount;
            passData.SaturationVsSaturationCurvePointCount = grade.SaturationVsSaturationCurve.PointCount;
            passData.CurveInterpolation = (int)grade.MasterCurve.Interpolation;
            ColorGradeQualifier qualifier = grade.Qualifier;
            passData.Qualifier0 = new Vector4(
                qualifier.HueCenter,
                qualifier.HueWidth,
                qualifier.HueSoftness,
                qualifier.Enabled ? (qualifier.Invert ? -1f : 1f) : 0f);
            passData.Qualifier1 = new Vector4(
                qualifier.SaturationCenter,
                qualifier.SaturationWidth,
                qualifier.SaturationSoftness,
                qualifier.LuminanceCenter);
            passData.Qualifier2 = new Vector4(
                qualifier.LuminanceWidth,
                qualifier.LuminanceSoftness,
                qualifier.HueShift,
                qualifier.Saturation);
            passData.Qualifier3 = new Vector4(
                qualifier.Exposure,
                qualifier.Temperature,
                qualifier.Tint,
                0f);
            passData.Qualifier4 = new Vector4(
                qualifier.Lift.x,
                qualifier.Lift.y,
                qualifier.Lift.z,
                0f);
            passData.Qualifier5 = new Vector4(
                qualifier.Gamma.x,
                qualifier.Gamma.y,
                qualifier.Gamma.z,
                0f);
            passData.Qualifier6 = new Vector4(
                qualifier.Gain.x,
                qualifier.Gain.y,
                qualifier.Gain.z,
                0f);
            System.Array.Clear(scratch.QualifierHueSamples, 0, scratch.QualifierHueSamples.Length);
            passData.QualifierHueSamples = scratch.QualifierHueSamples;
            passData.QualifierHueSampleCount = Mathf.Min(
                qualifier.HueSamples.Count,
                ColorGradeQualifier.MaxHueSamples);
            for (int sampleIndex = 0; sampleIndex < passData.QualifierHueSampleCount; sampleIndex++)
            {
                passData.QualifierHueSamples[sampleIndex] = new Vector4(
                    qualifier.HueSamples[sampleIndex],
                    qualifier.HueWidth,
                    qualifier.HueSoftness,
                    0f);
            }

            passData.Lut1D = grade.Lut?.Texture1D;
            passData.Lut3D = grade.Lut?.Texture3D;
            passData.LutType = grade.Lut == null ? 0 : (int)grade.Lut.Type;
            passData.LutIntensity = grade.Lut == null ? 0f : grade.LutIntensity;
            passData.LutColorSpace = (int)grade.LutColorSpace;
            passData.LutDomainMin = grade.Lut?.DomainMin ?? Vector3.zero;
            passData.LutDomainMax = grade.Lut?.DomainMax ?? Vector3.one;
        }
    }
}
