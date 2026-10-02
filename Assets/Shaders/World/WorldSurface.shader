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
        _SurfaceProjection ("Surface Projection", Vector) = (0,0,0,0)
        _SkyColor ("Sky Color", Color) = (0,0,0,0)
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
            #pragma multi_compile_local_fragment _ KERN_SURFACE_REDROCK KERN_SURFACE_TRANSIT KERN_SURFACE_PERSPECTIVE KERN_SURFACE_HORIZON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 customData : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 worldPosition : TEXCOORD2;
                float emissionMask : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            #include "WorldLightSampling.hlsl"

            float _WorldEmissionScale;

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float4 _BaseMapTileCount;
                float4 _WorldSize;
                float4 _SurfaceProjection;
                float4 _SkyColor;
            CBUFFER_END

            // Порог поля поверхности одинаков для всех материалов, поэтому это
            // глобальная юниформа: код кладёт её Shader.SetGlobalFloat.
            float _SurfaceFieldThreshold;

            Varyings VisibleVert(Attributes input)
            {
                Varyings output;
                float3 worldPosition = KernWorldGridVertex(TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = KernWorldGridClipPosition(worldPosition);
                output.uv = input.uv;
                output.uv2 = input.customData.xy;
                output.worldPosition = worldPosition.xy;
                output.emissionMask = saturate(input.customData.x);
                return output;
            }

            half4 RenderHorizon(float2 worldPosition, float2 uv)
            {
                float height = worldPosition.y - _WorldSize.y;
                float4 u_xlat0_d;
                float4 u_xlat1_d;
                float4 u_xlat2;
                float4 u_xlat3;
                float4 u_xlat4;

                u_xlat0_d.x = height - 4.0;
                u_xlat0_d.y = u_xlat0_d.x * (-0.152003);
                u_xlat0_d.y = exp(u_xlat0_d.y);
                u_xlat1_d.xyz = u_xlat0_d.yyy * float3(0.456, 0.528, 0.156);
                u_xlat0_d.z = uv.x * 80.0;
                u_xlat0_d.z = (worldPosition.x * 0.025) + u_xlat0_d.z;
                u_xlat0_d.w = u_xlat0_d.z * 0.2236;
                u_xlat0_d.w = sin(u_xlat0_d.w);
                u_xlat0_d.w = (u_xlat0_d.w * 3.0) + u_xlat0_d.z;
                u_xlat2.x = u_xlat0_d.w * 0.912542;
                u_xlat2.x = sin(u_xlat2.x);
                u_xlat0_d.w = (u_xlat2.x * 1.2) + u_xlat0_d.w;
                u_xlat2.x = u_xlat0_d.w * 0.091144;
                u_xlat2.x = sin(u_xlat2.x);
                u_xlat0_d.w = u_xlat0_d.w + u_xlat2.x;
                u_xlat2 = u_xlat0_d.wwww * float4(0.4236, 0.74236, 0.174236, 0.154424);
                float _tmp_dvx_0 = sin(u_xlat2.x);
                u_xlat2 = float4(_tmp_dvx_0, _tmp_dvx_0, _tmp_dvx_0, _tmp_dvx_0);
                u_xlat2.xy = u_xlat2.yw + u_xlat2.xy;
                u_xlat2.x = u_xlat2.z + u_xlat2.x;
                u_xlat2.y = u_xlat2.y + 3.0;
                u_xlat2.z = u_xlat0_d.w * 0.923159;
                u_xlat2.z = sin(u_xlat2.z);
                u_xlat2.y = u_xlat2.z + u_xlat2.y;
                u_xlat2.x = u_xlat2.y * u_xlat2.x;
                u_xlat2.x = (u_xlat2.x * 0.1) + 5.5;

                if (height < u_xlat2.x)
                {
                    u_xlat0_d.y = u_xlat0_d.y * 1.2;
                    u_xlat2.x = u_xlat0_d.y * u_xlat0_d.y;
                    u_xlat2.x = u_xlat2.x * u_xlat2.x;
                    u_xlat2.y = u_xlat0_d.y * u_xlat2.x;
                    u_xlat3.xyz = u_xlat0_d.zzz * float3(0.2, 0.04, 0.18);
                    u_xlat3.w = (height * 0.3) - 1.2;
                    u_xlat4 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, u_xlat3.xw);
                    u_xlat3.y = (u_xlat0_d.x * 0.3) - u_xlat3.y;
                    u_xlat3.x = (u_xlat3.y * 2.0) + u_xlat3.z;
                    u_xlat3 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, u_xlat3.xy);
                    u_xlat3 = max(u_xlat3, u_xlat4);
                    u_xlat0_d.x = u_xlat0_d.w + height;
                    u_xlat0_d.x = u_xlat0_d.x * u_xlat0_d.w;
                    u_xlat0_d.x = u_xlat0_d.x * height;
                    u_xlat0_d.x = u_xlat0_d.x * 5.0;
                    u_xlat0_d.x = sin(u_xlat0_d.x);
                    u_xlat0_d.x = (u_xlat0_d.x * 0.05) + 0.33;
                    u_xlat0_d.y = ((-u_xlat0_d.y) * u_xlat2.x) + 1.0;
                    u_xlat1_d.w = 1.0;
                    u_xlat4.x = u_xlat0_d.x * 0.8;
                    u_xlat4.yzw = float3(0.304, 0.088, 0.8);
                    u_xlat3 = (u_xlat3 * float4(0.2, 0.2, 0.2, 0.2)) + u_xlat4;
                    u_xlat2 = u_xlat2.yyyy * u_xlat3;
                    return half4((u_xlat0_d.yyyy * u_xlat1_d) + u_xlat2);
                }
                else
                {
                    return half4(u_xlat1_d.xyz, 1.0);
                }
            }

            half4 VisibleFrag(Varyings input) : SV_Target
            {
#if defined(KERN_SURFACE_HORIZON)
                return RenderHorizon(input.worldPosition, input.uv);
#elif defined(KERN_SURFACE_PERSPECTIVE)
                float4 u_xlat0_d;
                u_xlat0_d.x = input.uv2.y * input.uv2.y;
                u_xlat0_d.x = u_xlat0_d.x * u_xlat0_d.x;
                u_xlat0_d.x = u_xlat0_d.x * 40.0;
                u_xlat0_d.y = input.uv2.x - 0.5;
                u_xlat0_d.x = ((-u_xlat0_d.x) * u_xlat0_d.y) + input.uv.x;
                u_xlat0_d.y = input.uv.y;
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, u_xlat0_d.xy);
#elif defined(KERN_SURFACE_TRANSIT)
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
#elif defined(KERN_SURFACE_REDROCK)
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
                if (_WorldLightDebugView != 0)
                {
                    return half4(SampleWorldLightColorUnclamped(input.worldPosition).rgb, surface.a);
                }

                float3 emission = surface.rgb * _EmissionColor.rgb *
                    _EmissionStrength * input.emissionMask * _WorldEmissionScale;
                float3 worldLight = SampleWorldLightColorUnclamped(input.worldPosition).rgb;
                float3 litSurface = surface.rgb * worldLight;
                return half4(litSurface + emission, surface.a);
#else
                clip(-1.0);
                return 0;
#endif
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
            #pragma multi_compile_local_fragment _ KERN_SURFACE_REDROCK KERN_SURFACE_TRANSIT KERN_SURFACE_PERSPECTIVE KERN_SURFACE_HORIZON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/World/LightingFieldRaster.hlsl"
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
                float4 _SurfaceProjection;
                float4 _SkyColor;
            CBUFFER_END

            float _SurfaceFieldThreshold;

            Varyings LightingFieldVert(Attributes input)
            {
                Varyings output;
                output.positionCS = KernLightingFieldClipPositionWorld(TransformObjectToWorld(input.positionOS.xyz));
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
                float coverage = step(_SurfaceFieldThreshold, surface.a);
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

        Pass
        {
            Name "LightingAmbientOcclusionField"
            Tags { "LightMode" = "KernLightingAmbientOcclusionField" }

            Blend One One
            BlendOp Max
            ColorMask A
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LightingAmbientOcclusionVert
            #pragma fragment LightingAmbientOcclusionFrag
            #pragma multi_compile_local_fragment _ KERN_SURFACE_REDROCK KERN_SURFACE_TRANSIT KERN_SURFACE_PERSPECTIVE KERN_SURFACE_HORIZON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/World/LightingFieldRaster.hlsl"
            #include "WorldSurfaceCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldPosition : TEXCOORD1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float4 _BaseMapTileCount;
                float4 _WorldSize;
                float4 _SurfaceProjection;
                float4 _SkyColor;
            CBUFFER_END

            float _SurfaceFieldThreshold;

            Varyings LightingAmbientOcclusionVert(Attributes input)
            {
                Varyings output;
                output.positionCS = KernLightingFieldClipPositionWorld(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv;
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz).xy;
                return output;
            }

            half4 LightingAmbientOcclusionFrag(Varyings input) : SV_Target
            {
#if !defined(KERN_SURFACE_REDROCK) && !defined(KERN_SURFACE_TRANSIT) && !defined(KERN_SURFACE_PERSPECTIVE)
                clip(-1.0);
#endif
                float2 baseMapUV = KernResolveSurfaceUv(
                    input.uv,
                    input.worldPosition,
                    _BaseMapTileCount.xy,
                    _WorldSize.y);
                half alpha = SAMPLE_TEXTURE2D_LOD(
                    _BaseMap,
                    sampler_BaseMap,
                    baseMapUV,
                    0).a;
                half occupancy = step(_SurfaceFieldThreshold, alpha) * alpha * _Occupancy;
                return half4(0.0, 0.0, 0.0, occupancy);
            }
            ENDHLSL
        }
    }
}
