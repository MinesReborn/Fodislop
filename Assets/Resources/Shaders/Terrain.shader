Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
        _FlowMapRect ("Flow Map Rect", Vector) = (0,0,0,0)
        _ShimmerColor ("Shimmer Color", Color) = (1,1,1,1)
        _FallbackColor ("Fallback Color", Color) = (1, 1, 0, 1)
        _DebugColor ("Debug Color", Color) = (1, 0, 1, 1)
        [ToggleUI] _DebugMode ("Debug Mode", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float2 shadowRelief : TEXCOORD5;
                float2 localUV      : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float2 shadowRelief : TEXCOORD5;
                float2 localUV      : TEXCOORD6;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
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

            float3 SampleFlowMap(float2 worldPos)
            {
                float2 size = float2(12.0, 10.0);
                float2 uv = worldPos / size;

                // Manual bilinear filtering for Point-filtered atlas
                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

                // Wrap UVs
                uv00 = frac(uv00);
                uv10 = frac(uv10);
                uv01 = frac(uv01);
                uv11 = frac(uv11);

                float3 s00 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv00 * _FlowMapRect.zw).rgb;
                float3 s10 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv10 * _FlowMapRect.zw).rgb;
                float3 s01 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv01 * _FlowMapRect.zw).rgb;
                float3 s11 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv11 * _FlowMapRect.zw).rgb;

                return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.shadowRelief = input.shadowRelief;
                output.localUV = input.localUV;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (input.animData.w > 0.5)
                {
                    if (input.color.a < 0.05) discard;
                    return input.color;
                }

                if (_DebugMode > 0.5)
                {
                    return half4(_DebugColor.rgb, 1.0);
                }

                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV.xy;

                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                    return half4(1, 0, 1, 1);

                // Use ceil to handle partial variations (e.g. 10.5 tiles -> 11 variations)
                // Subtracting a small epsilon to avoid rounding up exact integers due to precision
                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                float2 gPos = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = input.worldPos.w > 0.5;

                const float EPS = 0.0001;

                // Y-variant calculation
                // abs() handles negative world coordinates correctly
                float variantY = fmod(abs(gPos.y), tilesCount.y);
                // Invert Y because server Y increases downwards but texture V increases upwards
                wrapped.y = floor(tilesCount.y - EPS - variantY);

                if (isTiling)
                {
                    wrapped.x = floor(input.worldPos.z + EPS); // Base Tile Index
                }
                else
                {
                    wrapped.x = floor(fmod(abs(gPos.x), tilesCount.x) + EPS);
                }

                // Clamp wrapped coordinates to ensure they are within valid range [0, tilesCount-1]
                wrapped = clamp(wrapped, 0.0, tilesCount - 1.0);

                float2 tileOffsetUV = wrapped * tileSizeUV;

                // Calculate available space for this tile (relevant for partial tiles at the edge of sub-atlas)
                float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);

                // Inset the sampling slightly to avoid bleeding into neighboring tiles/padding
                // and clamp UVs within the quad boundaries [EPS, 1.0-EPS]
                float2 quadUV = clamp(input.uv, EPS, 1.0 - EPS);

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                if (texColor.a < 0.05)
                {
                    discard;
                }

                float3 finalRgb = texColor.rgb;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                // Shadow / Relief logic
                float textureType = input.shadowRelief.x;
                float reliefMaskVal = input.shadowRelief.y;

                if (textureType > 0.5) // Relief (Type 1)
                {
                    float val = 15.0 - reliefMaskVal;
                    // Bits: 1(Top), 2(Left), 4(Bottom), 8(Right)
                    // Extraction: frac(val * 0.25) extracts bit 1 (Left), 0.125 bit 2 (Bottom), 0.0625 bit 3 (Right)
                    float3 bits = frac(val * float3(0.25, 0.125, 0.0625));
                    bool3 isCliff = bits >= 0.5; // x=Left, y=Bottom, z=Right

                    float u = input.localUV.x;
                    float v = input.localUV.y;
                    float uvMinus = u - v;
                    float uvPlus = u + v;

                    bool isLeft = (uvPlus < 0.0) && (uvMinus < 0.0);
                    bool isBottom = (uvMinus > 0.0) && (uvPlus < 0.0);
                    bool isRight = (uvPlus > 0.0) && (uvMinus > 0.0);

                    bool activeCliff = (isLeft && isCliff.x) || (isBottom && isCliff.y) || (isRight && isCliff.z);

                    if (activeCliff)
                    {
                        float maxUV2 = max(u * u, v * v);
                        float grad = 1.0 - maxUV2;
                        finalRgb *= (grad * grad * grad); // Cubed gradient
                    }
                }
                else // Shadow (Type 0)
                {
                    float shadowVal = input.shadowRelief.y;
                    finalRgb *= (1.0 - shadowVal * shadowVal); // Quadratic falloff
                }

                if (animType == 1) // Blinking
                {
                    float pulse = 0.5 + 0.5 * sin(_Time.y * speed * 0.5 + offset);
                    finalRgb *= pulse;
                }
                else if (animType == 2) // Shimmer
                {
                    float2 pixelWorldPos = input.worldPos.xy + input.uv;
                    float3 flowSample = SampleFlowMap(pixelWorldPos);

                    float3 flowHsv = RgbToHsv(flowSample);
                    float hueAngle = flowHsv.x * 6.28318548;
                    float chroma = max(flowSample.r, max(flowSample.g, flowSample.b)) - min(flowSample.r, min(flowSample.g, flowSample.b));

                    float wave = sin(-(hueAngle + _Time.y * speed * 0.05));
                    wave = (wave + 1.0) * 0.5;
                    float waveCubed = wave * wave * wave;

                    float luminance = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
                    float invLum = 1.0 - luminance;
                    float lumMask = 1.0 - invLum * invLum * invLum;

                    float factor = waveCubed * lumMask * chroma;

                    finalRgb = lerp(finalRgb, _ShimmerColor.rgb, factor);
                }
                else if (animType == 3) // Rainbow
                {
                    float3 hsv = RgbToHsv(finalRgb);
                    hsv.x = frac(hsv.x + _Time.y * (speed / 255.0));
                    finalRgb = HsvToRgb(hsv);
                }

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend Off
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float2 shadowRelief : TEXCOORD5;
                float2 localUV      : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float2 shadowRelief : TEXCOORD5;
                float2 localUV      : TEXCOORD6;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
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

            float3 SampleFlowMap(float2 worldPos)
            {
                float2 size = float2(12.0, 10.0);
                float2 uv = worldPos / size;

                // Manual bilinear filtering for Point-filtered atlas
                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

                // Wrap UVs
                uv00 = frac(uv00);
                uv10 = frac(uv10);
                uv01 = frac(uv01);
                uv11 = frac(uv11);

                float3 s00 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv00 * _FlowMapRect.zw).rgb;
                float3 s10 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv10 * _FlowMapRect.zw).rgb;
                float3 s01 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv01 * _FlowMapRect.zw).rgb;
                float3 s11 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv11 * _FlowMapRect.zw).rgb;

                return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.shadowRelief = input.shadowRelief;
                output.localUV = input.localUV;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (input.animData.w > 0.5)
                {
                    if (input.color.a < 0.05) discard;
                    return input.color;
                }

                if (_DebugMode > 0.5)
                {
                    return half4(_DebugColor.rgb, 1.0);
                }

                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV.xy;

                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                    return half4(1, 0, 1, 1);

                // Use ceil to handle partial variations
                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                float2 gPos = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = input.worldPos.w > 0.5;

                const float EPS = 0.0001;

                // Y-variant calculation
                float variantY = fmod(abs(gPos.y), tilesCount.y);
                wrapped.y = floor(tilesCount.y - EPS - variantY);

                if (isTiling)
                {
                    wrapped.x = floor(input.worldPos.z + EPS); // Base Tile Index
                }
                else
                {
                    wrapped.x = floor(fmod(abs(gPos.x), tilesCount.x) + EPS);
                }

                wrapped = clamp(wrapped, 0.0, tilesCount - 1.0);

                float2 tileOffsetUV = wrapped * tileSizeUV;
                float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                float2 quadUV = clamp(input.uv, EPS, 1.0 - EPS);

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                if (texColor.a < 0.05)
                {
                    discard;
                }

                float3 finalRgb = texColor.rgb;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                // Shadow / Relief logic
                float textureType = input.shadowRelief.x;
                float reliefMaskVal = input.shadowRelief.y;

                if (textureType > 0.5) // Relief (Type 1)
                {
                    float val = 15.0 - reliefMaskVal;
                    // Bits: 1(Top), 2(Left), 4(Bottom), 8(Right)
                    // Extraction: frac(val * 0.25) extracts bit 1 (Left), 0.125 bit 2 (Bottom), 0.0625 bit 3 (Right)
                    float3 bits = frac(val * float3(0.25, 0.125, 0.0625));
                    bool3 isCliff = bits >= 0.5; // x=Left, y=Bottom, z=Right

                    float u = input.localUV.x;
                    float v = input.localUV.y;
                    float uvMinus = u - v;
                    float uvPlus = u + v;

                    bool isLeft = (uvPlus < 0.0) && (uvMinus < 0.0);
                    bool isBottom = (uvMinus > 0.0) && (uvPlus < 0.0);
                    bool isRight = (uvPlus > 0.0) && (uvMinus > 0.0);

                    bool activeCliff = (isLeft && isCliff.x) || (isBottom && isCliff.y) || (isRight && isCliff.z);

                    if (activeCliff)
                    {
                        float maxUV2 = max(u * u, v * v);
                        float grad = 1.0 - maxUV2;
                        finalRgb *= (grad * grad * grad); // Cubed gradient
                    }
                }
                else // Shadow (Type 0)
                {
                    float shadowVal = input.shadowRelief.y;
                    finalRgb *= (1.0 - shadowVal * shadowVal); // Quadratic falloff
                }

                if (animType == 1) // Blinking
                {
                    float pulse = 0.5 + 0.5 * sin(_Time.y * speed * 0.5 + offset);
                    finalRgb *= pulse;
                }
                else if (animType == 2) // Shimmer
                {
                    float2 pixelWorldPos = input.worldPos.xy + input.uv;
                    float3 flowSample = SampleFlowMap(pixelWorldPos);

                    float3 flowHsv = RgbToHsv(flowSample);
                    float hueAngle = flowHsv.x * 6.28318548;
                    float chroma = max(flowSample.r, max(flowSample.g, flowSample.b)) - min(flowSample.r, min(flowSample.g, flowSample.b));

                    float wave = sin(-(hueAngle + _Time.y * speed * 0.05));
                    wave = (wave + 1.0) * 0.5;
                    float waveCubed = wave * wave * wave;

                    float luminance = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
                    float invLum = 1.0 - luminance;
                    float lumMask = 1.0 - invLum * invLum * invLum;

                    float factor = waveCubed * lumMask * chroma;

                    finalRgb = (finalRgb * (1.0 - factor)) + (factor * _ShimmerColor.rgb);
                }
                else if (animType == 3) // Rainbow
                {
                    float3 hsv = RgbToHsv(finalRgb);
                    hsv.x = frac(hsv.x + _Time.y * (speed / 255.0));
                    finalRgb = HsvToRgb(hsv);
                }

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
}
