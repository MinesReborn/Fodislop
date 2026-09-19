#nullable enable

using Kern.Rendering;

namespace Kern.World.Lighting.Quality;
public static class LightingQualityResolver
{
    public static LightingQualityMode Resolve(
        GraphicsPreset preset,
        LightingQualityMode requested)
    {
        // Off is the player switching the subsystem off, and no preset
        // outranks that. Ultra used to, and the result was a trap: the
        // radiance-cascade solve is far and away the most expensive thing
        // in the frame - measured on this project at 46.9M ray-steps and
        // 69.8M atlas taps per solve - and on Ultra the one control that
        // turns it off silently did nothing. Somebody trying to make the
        // game playable would set lighting to Off, see no change, and have
        // no way to find out why.
        //
        // The lock's real purpose is to keep Ultra from quietly running the
        // cheaper per-block path, which is the enum's zero value and so the
        // one any older serialized settings deserialize to. Ultra is authored
        // with diffuse bounce ("ULTRA (4 cascades, 1 diffuse bounce)").
        if (requested == LightingQualityMode.Off)
        {
            return LightingQualityMode.Off;
        }

        if (preset == GraphicsPreset.Ultra)
        {
            return requested is LightingQualityMode.PerBlock or LightingQualityMode.PerPixel
                ? LightingQualityMode.PerPixelBilinearFixBounce
                : requested;
        }

        return requested;
    }
}
