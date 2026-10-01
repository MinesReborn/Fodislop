#ifndef SCREEN_SPACE_REFLECTION_COMMON_INCLUDED
#define SCREEN_SPACE_REFLECTION_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

//#define SSR_USE_DISTANCE_BASED_BLUR

static const float k_SSRBlurReferenceDistance = 1.0;

// x : Blurriness - Exposed as a volume parameter; see ScreenSpaceReflectionVolumeSettings.blurriness.
// y : Delta between the screen Space Reflection texture last mip index and a reference last mip index.
// z : Screen Space Reflection texture last valid mip index
// w : Unused
float4 _ScreenSpaceReflectionParam2;

float GetSSRBlurriness()
{
    return _ScreenSpaceReflectionParam2.x;
}

float GetSSRTextureMipOffset()
{
    return _ScreenSpaceReflectionParam2.y;
}

float GetSSRTextureLastValidMipIndex()
{
    return _ScreenSpaceReflectionParam2.z;
}

float GetSSRBlurConeHalfAngle(float perceptualRoughness)
{
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);

    // Ref: "Moving Frostbite to PBR", p. 72. But using a different E value to match the reflection probe
    // roughness curve.
    //
    // const float e = 0.85;
    // return atan(e*roughness/(1.0 - e));

    // (Hopefully) Faster polynomial approximation of above
    float shininess = 1.0 - roughness;
    float shininess2 = shininess * shininess;
    return 0.2094 * (roughness + 5.6667 * (1.0 - shininess2 * shininess2));
}

float GetSSRMipLevelFromPerceptualRoughness(float3 positionWS, float perceptualRoughness)
{
    // Map perceptual roughness to a blur cone radius
    float blurConeAngle = GetSSRBlurConeHalfAngle(perceptualRoughness);
    float blurRadius = GetSSRBlurriness() * tan(blurConeAngle);

    #ifdef SSR_USE_DISTANCE_BASED_BLUR
    // Adjust based on camera distance
    if (IsPerspectiveProjection())
    {
        float fragZ = TransformWorldToView(positionWS).z;
        // Assume we have the blur radius at a distance BLUR_REFERENCE_DISTANCE to the camera and divide this radius
        // by 2 if the ratio between that reference distance and the fragment Z doubles.
        const float k_MinimumZ = 0.01;
        float scalingRatio = k_SSRBlurReferenceDistance / max(k_MinimumZ, abs(fragZ));
        blurRadius *= scalingRatio;
    }
    else
    {
        // TODO : handle orthographic projection. see https://jira.unity3d.com/browse/GFXLIGHT-1849
    }
    #endif

    // Map this blur radius back to a mip level, but assuming the reference resolution.
    const float k_MinimumRadius = 0.001;
    float mipLevel = log2(max(k_MinimumRadius, blurRadius));

    // Adjust for resolution: shift that mip by the difference between the actual SSR mip count and the reference one,
    // so the same material roughness produces approximately the same screen-space blur in pixels regardless of
    // reflection buffer resolution. The minimum-radius clamp keeps roughness 0 at mip 0 up to resolutions of
    // k_SSRBlurReferenceResolution * 2^-log2(k_MinimumRadius) ~= 1024 * 1024 = 1M pixels wide. The subtraction below
    // keeps roughness 1 at the highest mip level.
    mipLevel += GetSSRTextureMipOffset();
    return clamp(mipLevel, 0, GetSSRTextureLastValidMipIndex());
}

#endif
