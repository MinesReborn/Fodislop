#ifndef KERN_TERRAIN_PRISMATIC_CRYSTAL_INCLUDED
#define KERN_TERRAIN_PRISMATIC_CRYSTAL_INCLUDED

// OpenMines CellRender: animType 5, cells 71..75. These are animation
// colors, intentionally different from the crystals' minimap colors.
float3 PrismaticCrystalTint(float paletteIndex)
{
    if (paletteIndex == 1.0) { return _PrismaticTintA.rgb; }
    if (paletteIndex == 2.0) { return _PrismaticTintB.rgb; }
    if (paletteIndex == 3.0) { return _PrismaticTintC.rgb; }
    if (paletteIndex == 4.0) { return _PrismaticTintD.rgb; }
    if (paletteIndex == 5.0) { return _PrismaticTintE.rgb; }
    return float3(0.0, 0.0, 0.0);
}

float2 PrismaticCrystalFlowUV(float2 serverCell, float2 localPosition)
{
    // Extracted OpenMines phase sheet (25,23), size 10x8 atlas units.
    // Original offsets 100 and 128000 divide evenly by the sheet dimensions.
    // Stored bottom-up; server Y is down, cell-local Y is up.
    float2 surfacePosition = float2(serverCell.x + localPosition.x, serverCell.y - localPosition.y);
    // Quantize the lookup position, not the sampled hue or animation time.
    // Every fragment in one terrain pixel gets the same phase in both passes.
    float2 pixelCenter = QuantizeTerrainPixelCenter(surfacePosition);
    return pixelCenter / float2(10.0, 8.0);
}

// Equivalent to Unlit_TerrainShader.shader, animType == 5. Inputs/output
// are in the original GAMMA working space; caller bridges the linear atlas.
float3 EvaluatePrismaticCrystal(float3 baseColor, float3 flowSample, float paletteIndex, float phase)
{
    float maximum = max(flowSample.r, max(flowSample.g, flowSample.b));
    float minimum = min(flowSample.r, min(flowSample.g, flowSample.b));
    float chroma = maximum - minimum;
    float hue = 0.0;
    if (chroma > 0.0)
    {
        if (maximum == flowSample.r)
        {
            hue = (flowSample.g - flowSample.b) / chroma;
        }
        else if (maximum == flowSample.g)
        {
            hue = 2.0 + (flowSample.b - flowSample.r) / chroma;
        }
        else
        {
            hue = 4.0 + (flowSample.r - flowSample.g) / chroma;
        }
        hue = frac(hue / 6.0);
    }

    float wave = (sin(-(hue * 6.283185 + phase)) + 1.0) * 0.5;
    float body = wave * wave * wave;
    float inverseLuma = 1.0 - dot(baseColor, float3(0.3, 0.59, 0.11));
    return baseColor * (1.0 - body) + PrismaticCrystalTint(paletteIndex)
        * chroma * body * inverseLuma * inverseLuma * inverseLuma;
}

#endif
