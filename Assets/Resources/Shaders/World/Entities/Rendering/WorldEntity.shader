Shader "Kern/World Entity"
{
    // Sprite shader for everything rendered through WorldEntityBatchRenderer
    // (robots, buildings, tentacles, pooled visual effects, chat bubbles). Identical in
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

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ KERN_WORLD_LIGHTING
            #pragma multi_compile _ KERN_GPU_INSTANCING

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

            #include "Assets/Shaders/Sampling/PixelArt/PixelArtFiltering.hlsl"

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
            float _GlowFieldThreshold;

            #if defined(KERN_GPU_INSTANCING)
            struct EntityGpuInstance
            {
                float4 positionAndScale;
                float4 uvRect;
                float4 color;
                float4 rotationAndPivot;
            };

            StructuredBuffer<EntityGpuInstance> _EntityInstances;
            #endif

            #include "Assets/Shaders/World/Lighting/Sampling/WorldLightSampling.hlsl"

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;
#if defined(KERN_GPU_INSTANCING)
                EntityGpuInstance inst = _EntityInstances[instanceID];
                float2 localPos = (input.positionOS.xy - inst.rotationAndPivot.zw) * inst.positionAndScale.zw;
                float2 rotatedPos = float2(
                    localPos.x * inst.rotationAndPivot.x - localPos.y * inst.rotationAndPivot.y,
                    localPos.x * inst.rotationAndPivot.y + localPos.y * inst.rotationAndPivot.x);
                float3 worldPosition = KernWorldGridVertex(float3(rotatedPos + inst.positionAndScale.xy, input.positionOS.z));
                output.positionCS = KernWorldGridClipPosition(worldPosition);
                output.uv = lerp(inst.uvRect.xy, inst.uvRect.zw, input.uv);
                output.color = inst.color;
                output.worldPos = worldPosition.xy;
#else
                float3 worldPosition = KernWorldGridVertex(TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = KernWorldGridClipPosition(worldPosition);
                output.uv = input.uv;
                output.color = input.color;
                // Batch-mesh vertices are pre-transformed world positions, so
                // object space equals world space when rendering with identity matrix.
                // Using TransformObjectToWorld ensures correct light sampling if an entity
                // or preview is rendered with a non-identity GameObject transform.
                output.worldPos = worldPosition.xy;
#endif
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
                    float3 worldLight = GetWorldLightColor(input.worldPos).rgb;
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

        // Light-emitting sprites (buildings) in the lighting fields. Glow
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

            #pragma multi_compile _ KERN_GPU_INSTANCING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/World/Lighting/Fields/LightingFieldRaster.hlsl"

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
                half4 glow : SV_Target1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            float _SpriteAlphaCull;
            float _GlowFieldThreshold;
            // Доля альбедо, уходящая в glow-поле: полное альбедо, умноженное
            // транспортом на GlowScale, пересвечивало здания и всё вокруг.
            float _EntityGlowEmission;

            // Прямоугольники спрайтов в атласе. COLOR вершины здесь несёт
            // индекс спрайта (R — младшие 8 бит, G — старшие), а не тинт:
            // светящиеся спрайты не тонируются, а отдельный поток вершин
            // сломал бы записанную warmup-коллекцию.
            StructuredBuffer<float4> _EntityGlowRects;

            #if defined(KERN_GPU_INSTANCING)
            struct EntityGpuInstance
            {
                float4 positionAndScale;
                float4 uvRect;
                float4 color;
                float4 rotationAndPivot;
            };

            StructuredBuffer<EntityGpuInstance> _EntityInstances;
            #endif

            Varyings LightingFieldVert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;
#if defined(KERN_GPU_INSTANCING)
                EntityGpuInstance inst = _EntityInstances[instanceID];
                float2 localPos = (input.positionOS.xy - inst.rotationAndPivot.zw) * inst.positionAndScale.zw;
                float2 rotatedPos = float2(
                    localPos.x * inst.rotationAndPivot.x - localPos.y * inst.rotationAndPivot.y,
                    localPos.x * inst.rotationAndPivot.y + localPos.y * inst.rotationAndPivot.x);
                float3 worldPosition = float3(rotatedPos + inst.positionAndScale.xy, input.positionOS.z);
                output.positionCS = KernLightingFieldClipPositionWorld(worldPosition);
                output.uv = lerp(inst.uvRect.xy, inst.uvRect.zw, input.uv);
                output.color = inst.color;
#else
                output.positionCS = KernLightingFieldClipPositionWorld(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv;
                output.color = input.color;
#endif
                return output;
            }

            LightingFieldOutput LightingFieldFrag(Varyings input)
            {
                // Отбор муара здесь не нужен и был бы вреден: это не видимый
                // спрайт, а поле материалов для освещения. Производная fwidth
                // тут считалась бы в текселях поля, а не экрана, и ширина
                // смешивания означала бы другое. Поле и так читается с
                // интерполяцией, а текстура остаётся точечной намеренно.
                //
                // Поле переноса — два текселя на клетку, поэтому по-пиксельная
                // выборка спрайта превращала здание в мозаику из окон, стен и
                // надписей. Здание, как и блок, светит ровным средним тоном
                // своих непрозрачных пикселей.
                half4 center = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, input.uv, 0);
                uint spriteIndex = (uint)(input.color.r * 255.0 + 0.5) |
                    ((uint)(input.color.g * 255.0 + 0.5) << 8);
                float4 glowRect = _EntityGlowRects[spriteIndex];
                float3 sumColor = 0.0;
                float sumAlpha = 0.0;
                [unroll] for (int y = 0; y < 8; y++)
                {
                    [unroll] for (int x = 0; x < 8; x++)
                    {
                        float2 t = (float2(x, y) + 0.5) * (1.0 / 8.0);
                        float2 atlasUv = lerp(glowRect.xy, glowRect.zw, t);
                        half4 sampleColor = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, atlasUv, 0);
                        sumColor += sampleColor.rgb * sampleColor.a;
                        sumAlpha += sampleColor.a;
                    }
                }

                float3 averageColor = (sumAlpha > 0.0 ? sumColor / sumAlpha : center.rgb) * _Color.rgb;
                float strength = step(_GlowFieldThreshold, center.a * _Color.a) * center.a * _Color.a;

                LightingFieldOutput output;
                output.material = half4(0.0, 0.0, 0.0, 0.0);
                output.glow = half4(averageColor * strength * _EntityGlowEmission, strength);
                return output;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
