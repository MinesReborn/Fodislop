Shader "Fodinae/World Entity"
{
    // Sprite shader for everything rendered through WorldEntityBatchRenderer
    // (robots, buildings, tentacles, pooled VFX, chat bubbles). Identical in
    // look and blending to Sprites/Default, plus one addition: the fragment
    // samples the global _WorldLightTexture radiance field at its world
    // position and multiplies, so world entities finally receive the same
    // lighting as the terrain instead of glowing full-bright in dark caves.
    //
    // The FODINAE_WORLD_LIGHTING keyword is toggled globally by LightingEngine
    // (same mechanism as Terrain.shader): when it is off, lighting is disabled
    // and the lookup short-circuits to white, so the shader is safe before the
    // first solve and under the "Off" quality mode.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FODINAE_WORLD_LIGHTING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float2 worldPos   : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            // Тумблер режима выборки. Ноль — ближайшая без сглаживания,
            // единица — со сглаженной границей текселя. Раздаётся глобально
            // из DisplayManager: террейн и сущности рисуются разными
            // материалами, часть из них создаётся в рантайме.
            float _PixelArtFiltering;

            // Сглаженная ближайшая выборка — та же, что у террейна.
            //
            // Тексель спрайта занимает на экране дробное число пикселей,
            // и ближайшая выборка вынуждена одни строки текселей
            // дублировать, а другие терять. Функция оставляет выборку
            // ближайшей внутри текселя и размывает только его границу,
            // ровно на ширину экранного пикселя из fwidth.
            //
            // Выйти за край спрайта эта полоса может лишь на полтекселя и
            // попадает в отступ атласа, который заполнен прозрачным: край
            // смягчается, соседняя запись атласа не подтекает.
            float2 PixelArtSampleUV(float2 uv, float2 textureSize)
            {
                if (_PixelArtFiltering < 0.5)
                {
                    return uv;
                }

                float2 uvTexels = uv * textureSize;
                float2 seam = floor(uvTexels + 0.5);
                float2 pixelWidth = max(fwidth(uvTexels), 1e-5);
                uvTexels = seam + clamp((uvTexels - seam) / pixelWidth, -0.5, 0.5);
                return uvTexels / textureSize;
            }

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Texture2D<float4> _WorldLightTexture;
            SamplerState sampler_WorldLightTexture;
            float4 _WorldLightRect;
            float4 _WorldLightTextureSize;
            int _WorldLightDebugView;

            float3 GetWorldLightColor(float2 worldPos)
            {
                #if !defined(FODINAE_WORLD_LIGHTING)
                    return 1.0;
                #else
                float2 rectSize = max(_WorldLightRect.zw, float2(0.0001, 0.0001));
                float2 lightUV = saturate((worldPos - _WorldLightRect.xy) / rectSize);
                if (_WorldLightDebugView != 0)
                {
                    int2 debugPixel = clamp(
                        int2(lightUV * _WorldLightTextureSize.xy),
                        int2(0, 0),
                        int2(_WorldLightTextureSize.xy) - 1);
                    return _WorldLightTexture.Load(int3(debugPixel.x, debugPixel.y, 0)).rgb;
                }

                return _WorldLightTexture.Sample(
                    sampler_WorldLightTexture,
                    lightUV).rgb;
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                // Batch-mesh vertices are pre-transformed world positions, so
                // object space equals world space when rendering with identity matrix.
                // Using TransformObjectToWorld ensures correct light sampling if an entity
                // or preview is rendered with a non-identity GameObject transform.
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz).xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 sampleUV = PixelArtSampleUV(input.uv, _MainTex_TexelSize.zw);
                half4 texColor = _PixelArtFiltering < 0.5
                    ? SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, sampleUV)
                    : SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, sampleUV);
                half4 color = texColor * input.color * _Color;
                float3 worldLight = GetWorldLightColor(input.worldPos);
                if (_WorldLightDebugView != 0)
                {
                    return half4(worldLight, color.a);
                }

                color.rgb *= worldLight;
                // Premultiplied output, matching Sprites/Default's blend.
                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
