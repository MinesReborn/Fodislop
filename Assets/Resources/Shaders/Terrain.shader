Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
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
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
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

                return half4(texColor.rgb, 1.0);
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
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
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

                return half4(texColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
