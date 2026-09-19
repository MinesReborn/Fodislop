#nullable enable

namespace Kern.Core;

public enum PixelSamplingMode
{
    SmoothFiltered = 0,

    PixelPerfect = 1,

    Raw = 2,
}

public static class PixelSamplingRules
{
    public static bool SnapsCameraPosition(PixelSamplingMode mode) =>
        mode == PixelSamplingMode.PixelPerfect;

    public static bool QuantizesZoom(PixelSamplingMode mode) =>
        mode == PixelSamplingMode.PixelPerfect;

    public static bool FiltersTexelEdges(PixelSamplingMode mode) =>
        mode == PixelSamplingMode.SmoothFiltered;
}
