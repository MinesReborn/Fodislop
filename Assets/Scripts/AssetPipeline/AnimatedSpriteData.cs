using UnityEngine;

namespace Fodinae.Scripts
{
    /// <summary>
    /// Result of decoding an animated sprite file (GIF/WebP).
    /// Carries both the individual frame sprites and the animation metadata.
    /// </summary>
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
        public float FrameDuration => 1f / UnityEngine.Mathf.Max(1f, FPS);
    }
}
