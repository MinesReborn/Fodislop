#nullable enable

namespace Kern.World.Terrain;

using System;

[Flags]
internal enum TerrainLightingFlags : byte
{
    None = 0,
    SolidTop = 1 << 0,
    SolidLeft = 1 << 1,
    SolidBottom = 1 << 2,
    SolidRight = 1 << 3,
    Emissive = 1 << 4,
    PhysicalMass = 1 << 5,
}

// Wire format written to TerrainVertex.UV6 and decoded by
// Assets/Shaders/Terrain/TerrainLightingData.hlsl.
internal readonly record struct TerrainLightingData(
    float PackedFlags,
    float PackedContour)
{
    public const byte SolidBoundaryMask = 0x0F;

    private const float EmissionFractionScale = 0.25f;
    private const int SolidDiagonalShift = 1;
    private const int ReliefCodeShift = 5;
    private const int ReliefCodeRange = 1 << ReliefCodeShift;

    // Бит 0 — roundable contour, биты 1-4 — диагональные соседи,
    // биты 5-9 — код рельефа. Код, а не маска:
    // ноль означает «клетка без рельефа, каймы нет», а маска рельефа
    // хранится как mask + 1. Иначе клетка без рельефа и клетка, у которой
    // все четыре соседа чужие, выглядели бы одинаково.
    public const int NoRelief = 0;
    private const int RoundableContourFlag = 1 << 0;

    public TerrainLightingFlags Flags =>
        (TerrainLightingFlags)(byte)MathF.Floor(PackedFlags + 0.0001f);

    public int SolidBoundary => (int)Flags & SolidBoundaryMask;

    public int SolidDiagonal =>
        ((int)MathF.Round(PackedContour) >> SolidDiagonalShift) & SolidBoundaryMask;

    public int ReliefCode => ((int)MathF.Round(PackedContour) >> ReliefCodeShift) & 0x1F;

    public bool IsEmissive => (Flags & TerrainLightingFlags.Emissive) != 0;

    public bool IsPhysicalMass => (Flags & TerrainLightingFlags.PhysicalMass) != 0;

    public bool ReceivesAmbientOcclusion => !IsPhysicalMass;

    public bool IsRoundable =>
        ((int)MathF.Round(PackedContour) & RoundableContourFlag) != 0;

    public float EmissionStrength => IsEmissive
        ? Math.Clamp((PackedFlags - MathF.Floor(PackedFlags)) / EmissionFractionScale, 0f, 1f)
        : 0f;

    public static TerrainLightingData Pack(
        byte solidConnectivityMask,
        bool isGlowing,
        bool hasRoundedPhysicalContour,
        bool isPhysicalMass,
        float emissionStrength,
        byte reliefMask,
        bool hasRelief)
    {
        var flags = (TerrainLightingFlags)(solidConnectivityMask & SolidBoundaryMask);
        if (isGlowing)
        {
            flags |= TerrainLightingFlags.Emissive;
        }

        if (isPhysicalMass)
        {
            flags |= TerrainLightingFlags.PhysicalMass;
        }

        int contourFlags = hasRoundedPhysicalContour ? RoundableContourFlag : 0;
        int solidDiagonal = solidConnectivityMask >> 4;
        int reliefCode = hasRelief ? (reliefMask & SolidBoundaryMask) + 1 : NoRelief;
        return new TerrainLightingData(
            (byte)flags + (emissionStrength * EmissionFractionScale),
            contourFlags + (solidDiagonal << SolidDiagonalShift) +
                (reliefCode * ReliefCodeRange));
    }
}
