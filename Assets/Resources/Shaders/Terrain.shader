Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
        _FallbackColor ("Fallback Color", Color) = (0.2, 0.2, 0.2, 1.0)
        _DebugColor ("Debug Color", Color) = (1, 1, 1, 1)
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
                float2 tileSizeUV   : TEXCOORD2;
                float2 worldPos     : TEXCOORD3;
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
                output.tileSizeUV = input.tileSizeUV.xy;
                output.worldPos = input.worldPosAttr.xy;
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
                float2 tileSizeUV = input.tileSizeUV;

                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                    return half4(1, 0, 1, 1);

                float2 tilesCount = round(subAtlasSizeUV / tileSizeUV);
                tilesCount = max(tilesCount, 1.0);

                int globalX = (int)floor(input.worldPos.x + 0.001);
                int globalY = (int)floor(input.worldPos.y + 0.001);

                int wrappedX = ((globalX % (int)tilesCount.x) + (int)tilesCount.x) % (int)tilesCount.x;
                int wrappedY = ((int)tilesCount.y - 1) - (((globalY % (int)tilesCount.y) + (int)tilesCount.y) % (int)tilesCount.y);

                float2 tileOffsetUV = float2(wrappedX, wrappedY) * tileSizeUV;
                float2 finalUV = baseUV + tileOffsetUV + input.uv * tileSizeUV;

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                // If the texture is pure black and transparent, or just very transparent, use fallback
                // This handles cases where the atlas might be empty/cleared but still bound
                if (texColor.a < 0.1)
                {
                    return _FallbackColor;
                }

                return half4(texColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
