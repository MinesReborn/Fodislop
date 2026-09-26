#nullable enable

namespace Kern.World.Lighting.Quality;
// Режима два: транспорт света выключен или считается попиксельно. Контактное
// AO стандартной ступени не зависит от этого режима. Перблоковой ветки
// больше нет — её никто не выбирал, а тянула она за собой отдельную выборку
// атласа, отдельный путь композита и отдельный глобальный флаг для шейдеров.
public enum LightingQualityMode
{
    [Kern.Core.SettingLabel("settings.lighting.off")]
    Off = 0,
    [Kern.Core.SettingLabel("settings.lighting.per_pixel")]
    PerPixel = 1,
}
