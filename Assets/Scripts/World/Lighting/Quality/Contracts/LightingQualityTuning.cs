#nullable enable

namespace Kern.World.Lighting;

// FieldPixelsPerCell — сетка переноса: material/albedo/emission, DDA, пробы.
// LightPixelsPerCell — сетка приёмников: static/dynamic direct, поверхность и
// итоговая карта света; не больше FieldPixelsPerCell. Мир и контактное AO
// всегда остаются 32×32.
public readonly record struct LightingQualityTuning(
    int FieldPixelsPerCell,
    int LightPixelsPerCell,
    int CascadeProbePixelsPerCell,
    int MaximumStaticCascadeDirections,
    float DynamicNearCells,
    int DynamicAngularSampleCount,
    int DynamicEmitterPointsPerAxis,
    int DynamicPolarDirectionCount);
