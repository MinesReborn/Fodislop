#ifndef KERN_EXTINCTION_HLSL
#define KERN_EXTINCTION_HLSL

// Математика экстинкции и пропускания среды.
//
// READS: _EmptyExtinctionRGB, _SolidExtinctionRGB
// WRITES: ничего
// MUST NOT: читать/писать текстуры, знать о геометрии

// Extinction per cell, per RGB channel. Only physical occupancy mixes media.
float3 SegmentExtinction(float solid)
{
    return lerp(max(_EmptyExtinctionRGB.rgb, 0.0), max(_SolidExtinctionRGB.rgb, 0.0), solid);
}

float3 SegmentTransmission(float solid, float physicalLength)
{
    return exp(-SegmentExtinction(solid) * physicalLength);
}

// Stable 1 - exp(-x), including nearly transparent media.
float AbsorbedFraction(float opticalDepth)
{
    float result = 0.0;
    if (opticalDepth < 0.001)
    {
        result = opticalDepth * (1.0 - opticalDepth * 0.5 + opticalDepth * opticalDepth / 6.0);
    }
    else
    {
        result = 1.0 - exp(-opticalDepth);
    }

    return result;
}

// EmissionField is the radiance emitted by one cell. Normalizing by the
// one-cell integral keeps glowing rock bright without bypassing intervening
// rock, and makes splitting a segment leave the answer unchanged.
float CellEmissionWeight(float extinction, float distanceCells)
{
    float result = distanceCells;
    if (extinction > 0.0)
    {
        result = AbsorbedFraction(extinction * distanceCells) / AbsorbedFraction(extinction);
    }

    return result;
}

#endif // KERN_EXTINCTION_HLSL
