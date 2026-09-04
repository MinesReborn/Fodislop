#nullable enable

namespace Fodinae.World.Lighting.Quality
{
    /// <summary>
    /// How the radiance-cascade solve is sampled into the final lightmap.
    /// </summary>
    /// <remarks>
    /// <see cref="PerBlock"/> is the enum's zero value on purpose: existing
    /// serialized <c>GraphicsQualitySettings</c> data predates this field and
    /// will deserialize with the default value, and that default must be a
    /// working mid-quality tier, not <see cref="Off"/> (which would silently
    /// go dark) or <see cref="PerPixel"/> (which would silently regress
    /// everyone's frame time).
    /// </remarks>
    public enum LightingQualityMode
    {
        [Fodinae.Core.SettingLabel("settings.lighting.per_block")]
        PerBlock = 0,
        [Fodinae.Core.SettingLabel("settings.lighting.off")]
        Off = 1,
        [Fodinae.Core.SettingLabel("settings.lighting.per_pixel")]
        PerPixel = 2,

        /// <summary>
        /// Per-pixel resolve with the full Radiance Cascades bilinear fix:
        /// each spatial/angular child sample receives its own near interval,
        /// intervals are merged independently, then bilinearly accumulated.
        /// </summary>
        [Fodinae.Core.SettingLabel("settings.lighting.per_pixel_bilinear")]
        PerPixelBilinearFix = 3,
    }
}
