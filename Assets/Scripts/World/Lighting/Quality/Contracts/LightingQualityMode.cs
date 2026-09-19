#nullable enable

namespace Kern.World.Lighting.Quality;
public enum LightingQualityMode
{
    [Kern.Core.SettingLabel("settings.lighting.per_block")]
    PerBlock = 0,
    [Kern.Core.SettingLabel("settings.lighting.off")]
    Off = 1,
    [Kern.Core.SettingLabel("settings.lighting.per_pixel")]
    PerPixel = 2,

    [Kern.Core.SettingLabel("settings.lighting.per_pixel_bilinear")]
    PerPixelBilinearFix = 3,

    [Kern.Core.SettingLabel("settings.lighting.per_pixel_bilinear_bounce")]
    PerPixelBilinearFixBounce = 4,
}
