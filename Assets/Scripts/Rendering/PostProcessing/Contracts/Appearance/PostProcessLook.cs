#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public static class PostProcessLook
{
    public static class Effects
    {
        public const bool Bloom = false;
        public const bool Vignette = false;
        public const bool Eigengrau = false;
    }

    // 1. Пирамида блум собирается из HDR-входа до финального композита.
    public static class Bloom
    {
        public const float Intensity = 0.7f;
        public const float Threshold = 1f;
        public const float SoftKnee = 0.5f;
        public const float Radius = 1.95f;
        public const float Scatter = 0.58f;

        public static Color Tint => Color.white;
    }

    // 2. Кадр вдвое ярче сцены (+1 стоп) до тонмаппинга URP. Пики сжимает
    // сам URP: в SDR — плавное плечо Neutral, в HDR — BT.2390, который
    // трогает только то, что подходит к пику дисплея.
    public static class Exposure
    {
        public const float Stops = 1f;
    }

    // 3. Nits профиля задают нормализацию входа и предел вывода дисплея.
    public static class DisplayCalibration
    {
        public const float PaperWhiteNits = 350f;
        public const float PeakBrightnessNits = 1300f;
    }

    // 4. LUT от сервера и виньетка ложатся на сигнал дисплея после тонмаппинга.
    public static class Vignette
    {
        public const float Intensity = 0.4f;
        public const float Smoothness = 0.6f;

        public static Color Color => new(0f, 0f, 0f, 1f);

        public static Vector2 Center => new(0.5f, 0.5f);
    }

    // 4b. Хроматическая аберрация объектива: радиальное расхождение каналов.
    // Сила 0..1; центр кадра остаётся чистым, к краям дужка растёт.
    public static class ChromaticAberration
    {
        public const float Intensity = 0.6f;
    }

    // 5. Eigengrau добавляется после виньетки в DisplayFinal.
    public static class FilmGrain
    {
        public const float Intensity = 1f;
        public const float DarknessThreshold = 0.08f;
        public const float NoiseScale = 0.75f;
        public const float EigengrauNoiseAmplitude = 0.85f;
        public static Color Color => new(0.0863f, 0.0863f, 0.1137f, 1f);
    }

}
