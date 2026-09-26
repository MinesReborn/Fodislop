#ifndef KERN_WORLD_LIGHT_SAMPLING_INCLUDED
#define KERN_WORLD_LIGHT_SAMPLING_INCLUDED

Texture2D<float4> _WorldLightTexture;
SamplerState sampler_WorldLightTexture;
float4 _WorldLightRect;
float4 _WorldLightTextureSize;
int _WorldLightDebugView;

float2 GetWorldLightUvUnclamped(float2 worldPos)
{
    float2 rectSize = max(_WorldLightRect.zw, float2(0.0001, 0.0001));
    return (worldPos - _WorldLightRect.xy) / rectSize;
}

float2 GetWorldLightUv(float2 worldPos)
{
    return saturate(GetWorldLightUvUnclamped(worldPos));
}

float4 SampleWorldLightColorAtUv(float2 lightUV)
{
    if (_WorldLightDebugView >= 1 && _WorldLightDebugView <= 3)
    {
        int2 debugPixel = clamp(
            int2(lightUV * _WorldLightTextureSize.xy),
            int2(0, 0),
            int2(_WorldLightTextureSize.xy) - 1);
        return _WorldLightTexture.Load(int3(debugPixel.x, debugPixel.y, 0));
    }

    return _WorldLightTexture.Sample(
        sampler_WorldLightTexture,
        lightUV);
}

float4 SampleWorldLightColor(float2 worldPos)
{
    return SampleWorldLightColorAtUv(GetWorldLightUv(worldPos));
}

float4 SampleWorldLightColorUnclamped(float2 worldPos)
{
    return SampleWorldLightColorAtUv(GetWorldLightUvUnclamped(worldPos));
}

float4 GetWorldLightColor(float2 worldPos)
{
#if !defined(KERN_WORLD_LIGHTING)
    return 1.0;
#else
    return SampleWorldLightColor(worldPos);
#endif
}

#endif
