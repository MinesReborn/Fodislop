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
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
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
