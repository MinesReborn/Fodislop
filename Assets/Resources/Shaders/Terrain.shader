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
        [ToggleUI] _UseLight2D ("Use Light2D", Float) = 0
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

            Blend SrcAlpha OneMinusSrcAlpha
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
                float4 packedData   : TEXCOORD5;
                float4 glowAttr     : TEXCOORD6;
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
                float4 packedData   : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 glowData     : TEXCOORD7;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
                float _SimpleGraphics;
                float _DarknessFactor;
                float _UseLight2D;
            CBUFFER_END

            float4 _HeadlightPos;
            float4 _HeadlightDir;
            float _HeadlightAngleCos;
            float _HeadlightRange;
            float _HeadlightIntensity;

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

                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

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
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.glowData = input.glowAttr;
                output.animData = input.animData;
                output.packedData = input.packedData;

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (input.color.a < 0.05) discard;
                if (input.worldPos.w > 1.5) discard;
                if (input.subAtlasRect.z < 0.0001)
                {
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
                {
                    if (input.color.a < 0.05) discard;
                    return input.color;
                }

                float frameCount = input.tileSizeUV.z;
                float frameHeightTiles = input.tileSizeUV.w;
                float animOffsetUV = 0;

                if (frameCount > 1.5)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float frameIndex = floor(fmod(_Time.y * speed, frameCount));
                    animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
                }

                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                float2 gPos = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = fmod(input.worldPos.w, 2.0) > 0.5;

                const float EPS = 0.0001;
                float variantY = fmod(abs(gPos.y), tilesCount.y);
                wrapped.y = floor(tilesCount.y - EPS - variantY);

                if (isTiling)
                {
                    wrapped.x = floor(input.worldPos.z + EPS);
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
                finalUV.y += animOffsetUV;

                int animTypeEarly = (int)(input.animData.x + 0.5);
                if (animTypeEarly == 4)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float scrollUV = fmod(_Time.y * speed * tileSizeUV.y * 0.05, subAtlasSizeUV.y);
                    finalUV.y = baseUV.y + fmod(finalUV.y - baseUV.y + scrollUV + subAtlasSizeUV.y, subAtlasSizeUV.y);
                }

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                if (texColor.a < 0.05)
                {
                    discard;
                }

                float3 finalRgb = texColor.rgb;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                if (_SimpleGraphics < 0.5)
                {
                    float textureType = input.packedData.x;
                    float reliefMaskVal = input.packedData.y;

                    if (textureType > 0.5)
                    {
                        float val = 15.0 - reliefMaskVal;
                        float4 bits = frac(val * float4(0.5, 0.25, 0.125, 0.0625));
                        bool4 isCliff = bits >= 0.5;

                        float u = input.packedData.z;
                        float v = input.packedData.w;
                        float uvMinus = u - v;
                        float uvPlus = u + v;

                        bool isTop    = (uvPlus > 0.0) && (uvMinus < 0.0);
                        bool isLeft   = (uvPlus < 0.0) && (uvMinus < 0.0);
                        bool isBottom = (uvMinus > 0.0) && (uvPlus < 0.0);
                        bool isRight  = (uvPlus > 0.0) && (uvMinus > 0.0);

                        bool activeCliff = (isTop && isCliff.x) || (isLeft && isCliff.y) || (isBottom && isCliff.z) || (isRight && isCliff.w);

                        if (activeCliff)
                        {
                            float maxUV2 = max(u * u, v * v);
                            float grad = 1.0 - maxUV2;
                            finalRgb *= (grad * grad * grad);
                        }
                    }
                    else
                    {
                        float shadowVal = input.packedData.y;
                        finalRgb *= (1.0 - shadowVal * shadowVal);
                    }
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

                float darkness = _DarknessFactor;
                float finalAlpha = 1.0;
                if (_UseLight2D > 0.5)
                {
                    float2 pixelPos = input.worldPosition.xy;
                    float2 toLight = pixelPos - _HeadlightPos.xy;
                    float dist = length(toLight);
                    float2 dir = toLight / (dist + 0.0001);
                    float cosAngle = dot(dir, _HeadlightDir.xy);
                    float angleAtten = smoothstep(_HeadlightAngleCos, 1.0, cosAngle);
                    float distAtten = 1.0 - smoothstep(0.0, _HeadlightRange, dist);
                    float attenuation = angleAtten * distAtten;
                    darkness *= (1.0 - attenuation * _HeadlightIntensity);
                }
                float4 glowFlags = input.glowData;
                int iflags = int(round(glowFlags.z));
                bool hasGlow = (iflags & 1) != 0;
                bool isRoundable = (iflags & 2) != 0;
                if (hasGlow)
                {
                    float2 toMagma = input.worldPosition.xy - glowFlags.xy;
                    float dist = length(toMagma);
                    float glowAtten = 1.0 - smoothstep(0.0, 1.8, dist);
                    darkness *= (1.0 - glowAtten);
                }
                if (isRoundable)
                {
                    int sameMask = int(round(glowFlags.w));
                    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
                    bool4 hasSame = bits >= 0.5;
                    float2 p = input.uv - 0.5;
                    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
                    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
                    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
                    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
                    float dist = length(p);
                    float aa = min(fwidth(dist), 1.0 / 16.0);
                    float alpha = 1.0 - smoothstep(0.51 - aa, 0.51 + aa, dist);
                    if (rTL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rTR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    float cornerDist = abs(abs(p.x) - abs(p.y));
                    float cornerExclude = smoothstep(0.4, 0.5, cornerDist);
                    alpha = lerp(alpha, 1.0, cornerExclude);
                    finalAlpha *= alpha;
                }
                finalRgb *= (1.0 - darkness);

                return half4(finalRgb, finalAlpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
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
                float4 packedData   : TEXCOORD5;
                float4 glowAttr     : TEXCOORD6;
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
                float4 packedData   : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 glowData     : TEXCOORD7;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
                float _SimpleGraphics;
                float _DarknessFactor;
                float _UseLight2D;
            CBUFFER_END

            float4 _HeadlightPos;
            float4 _HeadlightDir;
            float _HeadlightAngleCos;
            float _HeadlightRange;
            float _HeadlightIntensity;

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

                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

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
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.glowData = input.glowAttr;
                output.animData = input.animData;
                output.packedData = input.packedData;

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (input.color.a < 0.05) discard;
                if (input.worldPos.w > 1.5) discard;
                if (input.subAtlasRect.z < 0.0001)
                {
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
                {
                    if (input.color.a < 0.05) discard;
                    return input.color;
                }

                float frameCount = input.tileSizeUV.z;
                float frameHeightTiles = input.tileSizeUV.w;
                float animOffsetUV = 0;

                if (frameCount > 1.5)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float frameIndex = floor(fmod(_Time.y * speed, frameCount));
                    animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
                }

                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                float2 gPos = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = fmod(input.worldPos.w, 2.0) > 0.5;

                const float EPS = 0.0001;
                float variantY = fmod(abs(gPos.y), tilesCount.y);
                wrapped.y = floor(tilesCount.y - EPS - variantY);

                if (isTiling)
                {
                    wrapped.x = floor(input.worldPos.z + EPS);
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
                finalUV.y += animOffsetUV;

                int animTypeEarly = (int)(input.animData.x + 0.5);
                if (animTypeEarly == 4)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float scrollUV = fmod(_Time.y * speed * tileSizeUV.y * 0.05, subAtlasSizeUV.y);
                    finalUV.y = baseUV.y + fmod(finalUV.y - baseUV.y + scrollUV + subAtlasSizeUV.y, subAtlasSizeUV.y);
                }

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                if (texColor.a < 0.05)
                {
                    discard;
                }

                float3 finalRgb = texColor.rgb;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                if (_SimpleGraphics < 0.5)
                {
                    float textureType = input.packedData.x;
                    float reliefMaskVal = input.packedData.y;

                    if (textureType > 0.5)
                    {
                        float val = 15.0 - reliefMaskVal;
                        float4 bits = frac(val * float4(0.5, 0.25, 0.125, 0.0625));
                        bool4 isCliff = bits >= 0.5;

                        float u = input.packedData.z;
                        float v = input.packedData.w;
                        float uvMinus = u - v;
                        float uvPlus = u + v;

                        bool isTop    = (uvPlus > 0.0) && (uvMinus < 0.0);
                        bool isLeft   = (uvPlus < 0.0) && (uvMinus < 0.0);
                        bool isBottom = (uvMinus > 0.0) && (uvPlus < 0.0);
                        bool isRight  = (uvPlus > 0.0) && (uvMinus > 0.0);

                        bool activeCliff = (isTop && isCliff.x) || (isLeft && isCliff.y) || (isBottom && isCliff.z) || (isRight && isCliff.w);

                        if (activeCliff)
                        {
                            float maxUV2 = max(u * u, v * v);
                            float grad = 1.0 - maxUV2;
                            finalRgb *= (grad * grad * grad);
                        }
                    }
                    else
                    {
                        float shadowVal = input.packedData.y;
                        finalRgb *= (1.0 - shadowVal * shadowVal);
                    }
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

                float darkness = _DarknessFactor;
                float finalAlpha = 1.0;
                if (_UseLight2D > 0.5)
                {
                    float2 pixelPos = input.worldPosition.xy;
                    float2 toLight = pixelPos - _HeadlightPos.xy;
                    float dist = length(toLight);
                    float2 dir = toLight / (dist + 0.0001);
                    float cosAngle = dot(dir, _HeadlightDir.xy);
                    float angleAtten = smoothstep(_HeadlightAngleCos, 1.0, cosAngle);
                    float distAtten = 1.0 - smoothstep(0.0, _HeadlightRange, dist);
                    float attenuation = angleAtten * distAtten;
                    darkness *= (1.0 - attenuation * _HeadlightIntensity);
                }
                float4 glowFlags = input.glowData;
                int iflags = int(round(glowFlags.z));
                bool hasGlow = (iflags & 1) != 0;
                bool isRoundable = (iflags & 2) != 0;
                if (hasGlow)
                {
                    float2 toMagma = input.worldPosition.xy - glowFlags.xy;
                    float dist = length(toMagma);
                    float glowAtten = 1.0 - smoothstep(0.0, 1.8, dist);
                    darkness *= (1.0 - glowAtten);
                }
                if (isRoundable)
                {
                    int sameMask = int(round(glowFlags.w));
                    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
                    bool4 hasSame = bits >= 0.5;
                    float2 p = input.uv - 0.5;
                    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
                    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
                    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
                    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
                    float dist = length(p);
                    float aa = min(fwidth(dist), 1.0 / 16.0);
                    float alpha = 1.0 - smoothstep(0.51 - aa, 0.51 + aa, dist);
                    if (rTL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rTR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    float cornerDist = abs(abs(p.x) - abs(p.y));
                    float cornerExclude = smoothstep(0.4, 0.5, cornerDist);
                    alpha = lerp(alpha, 1.0, cornerExclude);
                    finalAlpha *= alpha;
                }
                finalRgb *= (1.0 - darkness);

                return half4(finalRgb, finalAlpha);
            }
            ENDHLSL
        }
    }
}
