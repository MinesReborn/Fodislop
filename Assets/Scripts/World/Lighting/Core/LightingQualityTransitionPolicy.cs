#nullable enable

using Kern.World.Lighting.Quality;

namespace Kern.World.Lighting
{
    /// <summary>Decides when a quality transition invalidates the whole field and radiance solution.</summary>
    internal static class LightingQualityTransitionPolicy
    {
        public static bool RequiresFullSolve(
            bool technicalSettingsChanged,
            LightingQualityMode previousQuality,
            LightingQualityMode nextQuality) =>
            technicalSettingsChanged || previousQuality != nextQuality;
    }
}
