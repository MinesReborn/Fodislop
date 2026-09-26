#ifndef KERN_TERRAIN_ANIMATION_SAMPLING_INCLUDED
#define KERN_TERRAIN_ANIMATION_SAMPLING_INCLUDED

// Requires TerrainAnimationProfile.hlsl, TerrainPrismaticCrystal.hlsl, and
// caller-owned flow texture/sampler declarations.
float3 TerrainResolveFlowSample(
    int animationProfile,
    int animationType,
    float4 worldPos,
    float4 packedData,
    float4 flowScale)
{
    float3 result = 0.0;

    if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL)
    {
        result = SAMPLE_TEXTURE2D(
            _PrismaticFlowMap,
            sampler_PrismaticFlowMap,
            PrismaticCrystalFlowUV(worldPos.xy, packedData.yz)).rgb;
    }
    else if (TerrainAnimationUsesFlowMap(animationType, animationProfile))
    {
        float2 flowPosition = worldPos.xy + packedData.yz * float2(1.0, -1.0);
        result = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, flowPosition / flowScale.xy).rgb;
    }

    return result;
}

#endif
