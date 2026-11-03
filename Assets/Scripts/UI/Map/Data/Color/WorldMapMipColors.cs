#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.Map;

internal static class WorldMapMipColors
{

    internal static Color32 CreateAverage(double red, double green, double blue, double alpha, double count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return new Color32(
            WorldMapMipColors.EncodeSrgbChannel(red / count),
            WorldMapMipColors.EncodeSrgbChannel(green / count),
            WorldMapMipColors.EncodeSrgbChannel(blue / count),
            (byte)(alpha / count));
    }

    internal static double DecodeSrgbChannel(byte channel) =>
        Mathf.GammaToLinearSpace(channel / 255f);

    internal static byte EncodeSrgbChannel(double linearChannel) =>
        (byte)Mathf.Clamp(
            Mathf.RoundToInt(Mathf.LinearToGammaSpace((float)linearChannel) * 255f),
            0,
            255);

}
