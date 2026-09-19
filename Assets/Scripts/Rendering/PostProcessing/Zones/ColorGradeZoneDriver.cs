#nullable enable

using UnityEngine;

namespace Kern.Rendering.PostProcessing;
public static class ColorGradeZoneDriver
{
    private static ColorGradeZones.Resolution? _look;

    public static ColorGradeZones.Resolution Push(ColorGradeZones? zones, Camera? camera)
    {
        // Грейд по умолчанию собирается один раз. Сборка на каждый кадр давала
        // новый снимок с новыми кривыми, и SetColorGrade ни разу не узнавал в нём
        // прошлый: копировал всё заново и сбрасывал историю постпроцесса, пока
        // на экране ничего не менялось.
        ColorGradeZones.Resolution resolution = _look ??= ColorGradeZones.Resolution.FromLook();
        if (zones != null && zones.Enabled && zones.Count > 0 && camera != null)
        {
            Vector3 camPos = camera.transform.position;
            resolution = zones.Resolve(resolution, camPos.x, camPos.y);
        }

        // Пустой набор, выключенный мастер-тумблер и потерянная камера —
        // тоже состояния, которые надо протолкнуть. Ранний return здесь
        // оставлял в проходе последнюю активную зону навсегда.
        PostProcessRuntimeState.SetColorGrade(resolution.Grade);
        return resolution;
    }
}
