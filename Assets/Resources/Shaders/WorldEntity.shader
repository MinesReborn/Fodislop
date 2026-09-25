Shader "Kern/World Entity"
{
    // Sprite shader for everything rendered through WorldEntityBatchRenderer
    // (robots, buildings, tentacles, pooled VFX, chat bubbles). Identical in
    // look and blending to Sprites/Default, plus one addition: the fragment
    // samples the global _WorldLightTexture radiance field at its world
    // position and multiplies, so world entities finally receive the same
    // lighting as the terrain instead of glowing full-bright in dark caves.
    //
    // The KERN_WORLD_LIGHTING keyword is toggled globally by LightingEngine
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
            #pragma multi_compile _ KERN_WORLD_LIGHTING

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

            #include "Assets/Shaders/PixelArtFiltering.hlsl"

            // Свойства материала держатся вместе: одно, объявленное снаружи,
            // выключает SRP Batcher на всём шейдере. `_MainTex_TexelSize` Unity
            // заводит сам под текстуру _MainTex — это свойство материала.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            // Пороги отсечения одинаковы для всех материалов мира сущностей,
            // поэтому это глобальные юниформы: код кладёт их Shader.SetGlobalFloat.
            float _SpriteAlphaCull;
            float _EmissiveFieldThreshold;

            #include "Assets/Shaders/World/WorldLightSampling.hlsl"

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
                half4 texColor = PixelArtSample(
                    TEXTURE2D_ARGS(_MainTex, sampler_PointClamp),
                    input.uv,
                    _MainTex_TexelSize.zw);
                half4 color = texColor * input.color * _Color;
                if (color.a > _SpriteAlphaCull)
                {
                    float3 worldLight = GetWorldLightColor(input.worldPos);
                    color.rgb *= worldLight;
                    // Premultiplied output, matching Sprites/Default's blend.
                    color.rgb *= color.a;
                }
                else
                {
                    color = half4(0.0, 0.0, 0.0, 0.0);
                }

                return color;
            }
            ENDHLSL
        }

        // Light-emitting sprites (buildings) in the lighting fields. Emission
        // is the sprite's own colour, the way glowing terrain emits its
        // albedo. Material output stays zero: with Max blending it keeps the
        // occupancy and albedo the terrain wrote, so a building glows without
        // becoming a wall.
        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "KernLightingMaterialField" }

            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LightingFieldVert
            #pragma fragment LightingFieldFrag

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
            };

            struct LightingFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            float _SpriteAlphaCull;
            float _EmissiveFieldThreshold;

            Varyings LightingFieldVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            LightingFieldOutput LightingFieldFrag(Varyings input)
            {
                // Отбор муара здесь не нужен и был бы вреден: это не видимый
                // спрайт, а поле материалов для освещения. Производная fwidth
                // тут считалась бы в текселях поля, а не экрана, и ширина
                // смешивания означала бы другое. Поле и так читается с
                // интерполяцией, а текстура остаётся точечной намеренно.
                half4 color = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, input.uv, 0) *
                    input.color * _Color;
                float strength = step(_EmissiveFieldThreshold, color.a) * color.a;

                LightingFieldOutput output;
                output.material = half4(0.0, 0.0, 0.0, 0.0);
                output.emission = half4(color.rgb * strength, strength);
                return output;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
