#nullable enable

namespace Kern.Rendering.PostProcessing;

public enum PostProcessDebugView
{
    None = 0,

    FalseColor = 1,

    Clipping = 2,

    GamutWarning = 3,

    LumaOnly = 4,

    SaturationOnly = 5,

    QualifierMatte = 6,

    RgbParade = 7,

    HighlightClipping = 8,

    ShadowClipping = 9,
}
