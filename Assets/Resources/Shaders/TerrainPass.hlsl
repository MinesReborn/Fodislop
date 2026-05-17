#ifndef TERRAIN_PASS_INCLUDED
#define TERRAIN_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float2 localUV : TEXCOORD6;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 localUV : TEXCOORD6;
    float3 worldPos : TEXCOORD7;
};

TEXTURE2D(_ViewportDataTex);
SAMPLER(sampler_ViewportDataTex);
TEXTURE2D(_CellConfigTex);
SAMPLER(sampler_CellConfigTex);
TEXTURE2D_ARRAY(_Atlases);
SAMPLER(sampler_Atlases);

CBUFFER_START(UnityPerMaterial)
    float4 _WorldParams; // x: camX, y: camY, z: worldW, w: worldH
    float4 _WindowParams; // x: anchorX, y: anchorY, z: winW, w: winH
    float4 _FlowMapRect;
    float4 _ShimmerColor;
CBUFFER_END

float3 RgbToHsv(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 HsvToRgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

float3 SampleFlowMap(float2 worldPos, int atlasIdx)
{
    float2 size = float2(12.0, 10.0);
    float2 uv = worldPos / size;
    float2 texelSize = 1.0 / size;
    float2 pixel = uv * size - 0.5;
    float2 f = frac(pixel);
    pixel = floor(pixel);

    float2 uv00 = frac((pixel + float2(0.5, 0.5)) * texelSize);
    float2 uv10 = frac((pixel + float2(1.5, 0.5)) * texelSize);
    float2 uv01 = frac((pixel + float2(0.5, 1.5)) * texelSize);
    float2 uv11 = frac((pixel + float2(1.5, 1.5)) * texelSize);

    float3 s00 = SAMPLE_TEXTURE2D_ARRAY(_Atlases, sampler_Atlases, _FlowMapRect.xy + uv00 * _FlowMapRect.zw, atlasIdx).rgb;
    float3 s10 = SAMPLE_TEXTURE2D_ARRAY(_Atlases, sampler_Atlases, _FlowMapRect.xy + uv10 * _FlowMapRect.zw, atlasIdx).rgb;
    float3 s01 = SAMPLE_TEXTURE2D_ARRAY(_Atlases, sampler_Atlases, _FlowMapRect.xy + uv01 * _FlowMapRect.zw, atlasIdx).rgb;
    float3 s11 = SAMPLE_TEXTURE2D_ARRAY(_Atlases, sampler_Atlases, _FlowMapRect.xy + uv11 * _FlowMapRect.zw, atlasIdx).rgb;

    return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
}

Varyings vert(Attributes input)
{
    Varyings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = input.uv;
    output.localUV = input.localUV;
    output.worldPos.xy = _WorldParams.xy + input.positionOS.xy;
    output.worldPos.z = input.positionOS.z;
    return output;
}

half4 SampleCell(float2 wPos, int cellId, float2 quadUV, float2 localUV, float tilingDesc, float reliefMaskVal, float shadowVal)
{
    if (cellId == 0) return half4(0, 0, 0, 0);

    float2 configUV = float2((cellId + 0.5) / 256.0, 0.25);
    float4 rect = _CellConfigTex.SampleLevel(sampler_CellConfigTex, configUV, 0);
    float4 meta = _CellConfigTex.SampleLevel(sampler_CellConfigTex, float2(configUV.x, 0.75), 0);

    int animType = (int)(meta.x + 0.5);
    float animSpeed = meta.y;
    float animFrames = meta.z;
    int atlasIdx = (int)(meta.w + 0.5);

    if (atlasIdx < 0) return half4(0, 0, 0, 0);

    const float tileSizeUV = 32.0 / 4096.0;
    float2 subAtlasSizeUV = rect.zw;
    if (subAtlasSizeUV.x < 0.0001) return half4(0, 0, 0, 0);

    float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
    tilesCount = max(tilesCount, 1.0);

    int desc = (int)tilingDesc;
    if ((desc & 0x40) != 0) quadUV.x = 1.0 - quadUV.x;
    if ((desc & 0x20) != 0) quadUV.y = 1.0 - quadUV.y;
    if ((desc & 0x80) != 0) quadUV = float2(quadUV.y, 1.0 - quadUV.x);

    float2 wrapped;
    if (tilingDesc > 0.5) {
        wrapped.x = desc & 0x1F;
    } else {
        wrapped.x = fmod(abs(floor(wPos.x + 0.001)), tilesCount.x);
    }
    wrapped.y = tilesCount.y - 1.0 - fmod(abs(floor(wPos.y + 0.001)), tilesCount.y);

    if (animFrames > 1.5) {
        int frameIdx = (int)(_Time.y * animSpeed) % (int)animFrames;
        wrapped.y += frameIdx * tilesCount.y;
    }

    float2 finalUV = rect.xy + (wrapped + clamp(quadUV, 0.001, 0.999)) * tileSizeUV;
    half4 texColor = SAMPLE_TEXTURE2D_ARRAY(_Atlases, sampler_Atlases, finalUV, atlasIdx);

    if (texColor.a < 0.05) return half4(0,0,0,0);

    float3 finalRgb = texColor.rgb;

    if (reliefMaskVal > 0) {
        float val = 15.0 - reliefMaskVal;
        float3 bits = frac(val * float3(0.25, 0.125, 0.0625));
        bool3 isCliff = bits >= 0.5;
        float u = localUV.x; float v = localUV.y;
        bool isLeft = (u+v < 0) && (u-v < 0);
        bool isBottom = (u-v > 0) && (u+v < 0);
        bool isRight = (u+v > 0) && (u-v > 0);
        if ((isLeft && isCliff.x) || (isBottom && isCliff.y) || (isRight && isCliff.z)) {
            finalRgb *= pow(1.0 - max(u*u, v*v), 3);
        }
    } else {
        finalRgb *= (1.0 - shadowVal * shadowVal);
    }

    if (animType == 1) finalRgb *= (0.5 + 0.5 * sin(_Time.y * animSpeed * 0.5));
    else if (animType == 2) {
        float3 flowSample = SampleFlowMap(wPos + quadUV, atlasIdx);
        float3 flowHsv = RgbToHsv(flowSample);
        float chroma = max(flowSample.r, max(flowSample.g, flowSample.b)) - min(flowSample.r, min(flowSample.g, flowSample.b));
        float wave = (sin(-(flowHsv.x * 6.283 + _Time.y * animSpeed * 0.05)) + 1.0) * 0.5;
        float luminance = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
        float factor = pow(wave, 3) * (1.0 - pow(1.0 - luminance, 3)) * chroma;
        finalRgb = lerp(finalRgb, _ShimmerColor.rgb, factor);
    }
    else if (animType == 3) {
        float3 hsv = RgbToHsv(finalRgb);
        hsv.x = frac(hsv.x + _Time.y * (animSpeed / 255.0));
        finalRgb = HsvToRgb(hsv);
    }

    return half4(finalRgb, 1.0);
}

half4 frag(Varyings input) : SV_Target
{
    float2 wPos = input.worldPos.xy;
    // Window-relative coordinates
    float2 localPos = wPos - _WindowParams.xy;
    float2 dataUV = (floor(localPos) + 0.5) / _WindowParams.zw;

    float4 mapData = _ViewportDataTex.Sample(sampler_ViewportDataTex, dataUV);

    int fgId = (int)(mapData.r * 255.0 + 0.5);
    int bgId = (int)(mapData.a * 255.0 + 0.5);
    float tiling = mapData.g * 255.0;
    float relief = (int)(mapData.b * 255.0 + 0.5) & 0x0F;
    float shadow = ((int)(mapData.b * 255.0 + 0.5) >> 4) / 15.0;

    if (input.worldPos.z > 0.05) {
        half4 bg = SampleCell(wPos, bgId, input.uv, input.localUV, 0, 0, shadow);
        if (bg.a < 0.05) discard;
        return bg;
    } else {
        half4 fg = SampleCell(wPos, fgId, input.uv, input.localUV, tiling, relief, shadow);
        if (fg.a < 0.05) discard;
        return fg;
    }
}

#endif
