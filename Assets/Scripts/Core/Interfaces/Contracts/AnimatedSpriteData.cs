#nullable enable

using UnityEngine;

namespace Fodinae
{
    /// <summary>
    /// Result of decoding an animated sprite file (GIF/WebP).
    /// Carries both the individual frame sprites and the animation metadata.
    /// </summary>
    /// <remarks>
    /// Живёт в Fodinae.Contracts, а не рядом с декодерами: тип стоит в
    /// сигнатуре IAssetLoader, а Fodinae.AssetPipeline ссылается на Contracts,
    /// не наоборот. Держать его у декодеров значило бы завернуть зависимость
    /// в кольцо — Contracts перестал бы собираться, что и произошло.
    /// Это значение без поведения, и правило слоя (в Contracts только
    /// интерфейсы, DTO и типы-значения) как раз про такие.
    /// </remarks>
    public readonly struct AnimatedSpriteData
    {
        public AnimatedSpriteData(Sprite[] frames, float fps, int frameHeight)
        {
            Frames = frames;
            FPS = fps;
            FrameHeight = frameHeight;
        }

        public Sprite[] Frames { get; }
        public float FPS { get; }
        public int FrameHeight { get; }

        /// <summary>Duration of a single frame in seconds.</summary>
        public float FrameDuration => 1f / Mathf.Max(1f, FPS);
    }
}
