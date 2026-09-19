Shader "Kern/World Surface"
{
    Properties
    {
        // Runtime construction validates and injects every required property.
        // Neutral ShaderLab values are sentinels, not rendering fallbacks.
        [MainTexture] _BaseMap ("Surface Texture", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _EmissionStrength ("Emission Strength", Range(0, 8)) = 0
        _Occupancy ("Physical Occupancy", Range(0, 1)) = 0
        _BaseMapTileCount ("Surface Sheet Tile Count", Vector) = (0,0,0,0)
        _WorldSize ("World Size", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex VisibleVert
            #pragma fragment VisibleFrag
            #pragma multi_compile_local_fragment _ KERN_SURFACE_REDROCK KERN_SURFACE_TRANSIT KERN_SURFACE_PERSPECTIVE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 glowData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldPosition : TEXCOORD1;
                float emissionMask : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            Texture2D<float4> _WorldLightTexture;
            SamplerState sampler_WorldLightTexture;
            float4 _WorldLightRect;
            float4 _WorldLightTextureSize;
            float _WorldEmissionScale;
            int _WorldLightDebugView;
            int _WorldLightPerBlock;

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float4 _BaseMapTileCount;
                float4 _WorldSize;
            CBUFFER_END

            float3 SampleWorldLight(float2 worldPosition)
            {
                float2 rectSize = max(_WorldLightRect.zw, float2(0.0001, 0.0001));
                float2 lightUV = (worldPosition - _WorldLightRect.xy) / rectSize;
                if (_WorldLightPerBlock != 0 || (_WorldLightDebugView >= 1 && _WorldLightDebugView <= 3))
                {
                    int2 debugPixel = clamp(
                        int2(lightUV * _WorldLightTextureSize.xy),
                        int2(0, 0),
                        int2(_WorldLightTextureSize.xy) - 1);
                    return _WorldLightTexture.Load(int3(debugPixel, 0)).rgb;
                }

                return _WorldLightTexture.Sample(sampler_WorldLightTexture, lightUV).rgb;
            }

            Varyings VisibleVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz).xy;
                output.emissionMask = saturate(input.glowData.x);
                return output;
            }

            half4 VisibleFrag(Varyings input) : SV_Target
            {
#if !defined(KERN_SURFACE_REDROCK) && !defined(KERN_SURFACE_TRANSIT) && !defined(KERN_SURFACE_PERSPECTIVE)
                clip(-1.0);
#endif
                float2 baseMapUV = KernResolveSurfaceUv(
                    input.uv,
                    input.worldPosition,
                    _BaseMapTileCount.xy,
                    _WorldSize.y);
                half4 surface = SAMPLE_TEXTURE2D_LOD(
                    _BaseMap,
                    sampler_BaseMap,
                    baseMapUV,
                    0);
                float3 worldLight = SampleWorldLight(input.worldPosition);
                if (_WorldLightDebugView != 0)
                {
                    return half4(worldLight, surface.a);
                }

                float3 emission = surface.rgb * _EmissionColor.rgb *
                    _EmissionStrength * input.emissionMask * _WorldEmissionScale;
                float3 litSurface = surface.rgb * worldLight;
                return half4(litSurface + emission, surface.a);
            }
            ENDHLSL
        }

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
            #pragma multi_compile_local_fragment _ KERN_SURFACE_REDROCK KERN_SURFACE_TRANSIT KERN_SURFACE_PERSPECTIVE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 lightingData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float emissionMask : TEXCOORD1;
                float2 worldPosition : TEXCOORD2;
            };

            struct LightingFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float4 _BaseMapTileCount;
                float4 _WorldSize;
            CBUFFER_END

            Varyings LightingFieldVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.emissionMask = saturate(input.lightingData.x);
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz).xy;
                return output;
            }

            LightingFieldOutput LightingFieldFrag(Varyings input)
            {
#if !defined(KERN_SURFACE_REDROCK) && !defined(KERN_SURFACE_TRANSIT) && !defined(KERN_SURFACE_PERSPECTIVE)
                clip(-1.0);
#endif
                float2 baseMapUV = KernResolveSurfaceUv(
                    input.uv,
                    input.worldPosition,
                    _BaseMapTileCount.xy,
                    _WorldSize.y);
                half4 surface = SAMPLE_TEXTURE2D_LOD(
                    _BaseMap,
                    sampler_BaseMap,
                    baseMapUV,
                    0);
                float coverage = step(0.05, surface.a);
                float occupancy = coverage * surface.a * _Occupancy;
                float emissionStrength = coverage * surface.a *
                    _EmissionStrength * input.emissionMask;

                LightingFieldOutput output;
                output.material = half4(surface.rgb * coverage, occupancy);
                output.emission = half4(
                    surface.rgb * _EmissionColor.rgb * emissionStrength,
                    emissionStrength);
                return output;
            }
            ENDHLSL
        }
    }
}
