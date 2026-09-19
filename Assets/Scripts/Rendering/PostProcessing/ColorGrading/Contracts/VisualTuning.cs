#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing
{
    public static class PostProcessLook
    {
        public static class Bloom
        {
            public const float Intensity = 0.7f;

            // Во сколько раз пиксель обязан превзойти свой локальный фон.
            // Абсолютного порога здесь больше нет: освещённость не ограничена
            // сверху (CompositeLighting пишет `ambient + directAndBounce` без
            // потолка), и любое абсолютное число резало кадр по линии равной
            // освещённости — светилась «половина блоков», а порог ниже 1.0
            // зажигал весь освещённый кадр.
            public const float Threshold = 1.6f;
            public const float SoftKnee = 0.5f;
            public const float Radius = 3f;
            // Шире прежнего: пирамида стала глубже, и рассеяние теперь
            // распределяет свет по пяти уровням, а не по двум.
            public const float Scatter = 0.7f;

            public static Color Tint => Color.white;
        }

        public static class Vignette
        {
            public const float Intensity = 0.28f;
            public const float Smoothness = 0.6f;

            public static Color Color => new(0f, 0f, 0f, 1f);

            public static Vector2 Center => new(0.5f, 0.5f);
        }

        public static class ColorGrading
        {
            public const float Exposure = 0f;
            public const float Contrast = 0f;
            public const float Saturation = 1f;

            public static Color Filter => Color.white;
        }

        public static class DisplayCalibration
        {
            public const float PaperWhiteNits = 350f;
            public const float PeakBrightnessNits = 1300f;
        }

        public static class SurfaceLook
        {
            public static Vector2 FlowScale => new(12f, 10f);
            public const float ShimmerSpeedScale = 0.05f;
            public const float PulseSpeedScale = 0.5f;

            public static Color ShimmerColor => Color.white;

            public static Color TransitEmissionColor => Color.white;
            public const float TransitEmissionStrength = 0.35f;

            public static Color PerspectiveEmissionColor => Color.white;
            public const float PerspectiveEmissionStrength = 0.12f;

            public const float SurfaceOccupancy = 1f;
        }

        public static class Effects
        {
            public const bool Bloom = false;
            public const bool Vignette = false;
            public const bool Eigengrau = false;
            public const bool MotionBlur = false;
        }

        public static class Grade
        {
            public const DisplayTransform Transform = DisplayTransform.None;
            public const float WhitePoint = 1f;
            public const float Temperature = 0f;
            public const float Tint = 0f;

            public static Vector3 Slope => Vector3.one;

            public static Vector3 Offset => Vector3.zero;

            public static Vector3 Power => Vector3.one;

            public const float GreyOut = 0.18f;
            public const float ShoulderPower = 4f;
            public const float ToePower = 1.6f;
            public const float ToeStops = 12f;

            // Сжатие гамута на выводе (ACES RGC). По умолчанию выключено: оно
            // единственное держало проход дисплея в кадре при нулевых эффектах,
            // а полноэкранный проход на 3420×1890 стоит десятки fps.
            public const bool GamutCompressionEnabled = false;
            public const float GamutCompressionStrength = 1f;
        }

        public static class FilmGrain
        {
            public const float Intensity = 0.3f;

            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            public const float AnimationSpeed = 60f;

            public static Color Color => new(0.02f, 0.02f, 0.02f, 1f);
        }

        public static class MotionBlur
        {
            public const float Intensity = 0.25f;
        }

    }
}

namespace Kern.World.Lighting
{
    [System.Flags]
    public enum LightingFeatureFlags
    {
        None = 0,
        StaticRC = 1 << 0,
        DynamicLights = 1 << 1,
        DiffuseBounce = 1 << 2,
        VisibilityAwareMerge = 1 << 3,
        WallAwareUpsample = 1 << 4,
        All = StaticRC | DynamicLights | DiffuseBounce | VisibilityAwareMerge | WallAwareUpsample,
    }

    public static class LightingConfigHolder
    {
        public static LightingFeatureFlags EnabledFeatures { get; set; } =
            LightingFeatureFlags.StaticRC |
            LightingFeatureFlags.DynamicLights |
            LightingFeatureFlags.DiffuseBounce |
            LightingFeatureFlags.VisibilityAwareMerge |
            LightingFeatureFlags.WallAwareUpsample;

        public const float AmbientIntensity = 0.0f;
        public const float EmissionScale = 6.0f;
        public static readonly Color AmbientColor = Color.white;
        // Per RGB channel: sigma = ExtinctionRGB * ExtinctionMultiplier.
        // Transmission after d cells = exp(-sigma * d); multiply incoming light by it.
        // sigma: 0 = transparent; 0.2 = 81.87% per cell; 4.60517 = 1% per cell.
        // Solid affects transmission through the wall, not illumination of its front surface.
        public static readonly Color EmptyExtinctionRGB = Color.white;
        public static readonly Color SolidExtinctionRGB = Color.white;
        public const float EmptyExtinctionMultiplier = 0.25f;
        public const float SolidExtinctionMultiplier = 1.0f;
        // false выключает отскок целиком: проход не считается, в свет не входит.
        public static bool BounceEnabled => (EnabledFeatures & LightingFeatureFlags.DiffuseBounce) != 0;
        public const float BounceStrength = 1.0f;

        // Стеля exposure-зебры (вид 9): всё выше — згорить і після тонмаппа.
        // Шкала в стопах від білого: 8.0 = +3 стопи. Контент HDR by design
        // (емісія до EmissionScale), тому стеля 1.0 фарбувала червоним весь
        // робочий HDR-запас.
        public const float MaximumLightMultiplier = 8.0f;

        public static bool DynamicLightEnabled => (EnabledFeatures & LightingFeatureFlags.DynamicLights) != 0;
        public const float DynamicLightIntensity = 1.0f;
        public static readonly Color DynamicLightColor = Color.white;
    }
}

namespace Kern.World.Terrain
{
    public static class TerrainLook
    {
        public const float AmbientOcclusionMip = 1.5f;

        public const float AmbientOcclusionStrength = 1f;

        private static readonly int _AmbientOcclusionMipID =
            Shader.PropertyToID("_TerrainAmbientOcclusionMip");
        private static readonly int _AmbientOcclusionStrengthID =
            Shader.PropertyToID("_TerrainAmbientOcclusionStrength");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplyShaderGlobals()
        {
            Shader.SetGlobalFloat(_AmbientOcclusionMipID, AmbientOcclusionMip);
            Shader.SetGlobalFloat(_AmbientOcclusionStrengthID, AmbientOcclusionStrength);
        }
    }
}
