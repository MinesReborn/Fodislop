#nullable enable

using System;

namespace Kern.Rendering;

/// <summary>
/// Finest brightness step the active display output can show at black, in
/// scene-linear units relative to paper white (1.0 = paper white), before
/// exposure. The display post-process pass publishes the output it actually
/// encodes; lighting derives what it may stop tracing from it.
/// </summary>
public static class DisplayOutputPrecision
{
    // SDR: 8-bit sRGB, first code above black lies on the linear segment.
    private const double SdrFirstStep = 1.0 / 255.0 / 12.92;

    // HDR10: 10-bit full-range PQ (SMPTE ST 2084) code 1, in nits.
    private static readonly double s_pqFirstStepNits = PqToNits(1.0 / 1023.0);

    public static bool HDROutput { get; private set; }

    public static float PaperWhiteNits { get; private set; } = 1f;

    // Changes whenever the published output changes what is visible.
    public static int Revision { get; private set; }

    /// <summary>Half of the first display code above black, relative to paper white.</summary>
    public static float HalfStepAtBlack => (float)(0.5 * (HDROutput
        ? s_pqFirstStepNits / Math.Max(PaperWhiteNits, 1f)
        : SdrFirstStep));

    public static void Publish(bool HDR, float paperWhiteNits)
    {
        float paperWhite = HDR ? Math.Max(paperWhiteNits, 1f) : 1f;
        if (HDR == HDROutput && paperWhite.Equals(PaperWhiteNits))
        {
            return;
        }

        HDROutput = HDR;
        PaperWhiteNits = paperWhite;
        Revision++;
    }

    private static double PqToNits(double code)
    {
        const double m1 = 2610.0 / 16384.0;
        const double m2 = 2523.0 / 4096.0 * 128.0;
        const double c1 = 3424.0 / 4096.0;
        const double c2 = 2413.0 / 4096.0 * 32.0;
        const double c3 = 2392.0 / 4096.0 * 32.0;
        double power = Math.Pow(code, 1.0 / m2);
        return 10000.0 * Math.Pow(Math.Max(power - c1, 0.0) / (c2 - c3 * power), 1.0 / m1);
    }
}
