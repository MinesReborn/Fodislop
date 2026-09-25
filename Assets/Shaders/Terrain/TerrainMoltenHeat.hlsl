#ifndef KERN_TERRAIN_MOLTEN_HEAT_INCLUDED
#define KERN_TERRAIN_MOLTEN_HEAT_INCLUDED

float3 EvaluateMoltenHeat(float3 baseColor, float2 surfacePosition, float phase)
{
    // One world-anchored 32x32 sampling grid, shared by all neighboring cells.
    // Heat is independent of server sprite-animation flags and texture green.
    float2 pixelPosition = (floor(surfacePosition * 32.0) + 0.5) / 32.0;
    float broadFlow = sin(dot(pixelPosition, _MoltenFlowDirectionA.xy) + phase);
    float crossFlow = sin(dot(pixelPosition, _MoltenFlowDirectionB.xy) - phase * _MoltenFlowPhase);
    float heat = saturate(
        _MoltenFlowWeightC + broadFlow * _MoltenFlowWeightA + crossFlow * _MoltenFlowWeightB);
    float hot = heat * heat;
    float material = max(baseColor.r, max(baseColor.g, baseColor.b));
    return baseColor * (_MoltenHeatBase + _MoltenHeatScale * heat)
        + _MoltenHotColor.rgb * (hot * material);
}

#endif
