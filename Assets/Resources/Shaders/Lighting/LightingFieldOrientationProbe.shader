Shader "Hidden/Kern/LightingFieldOrientationProbe"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex ProbeVert
            #pragma fragment ProbeFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/World/LightingFieldRaster.hlsl"

            // Same raster transform as the terrain material/emission/AO fields.
            float4 ProbeVert(float3 positionOS : POSITION) : SV_POSITION
            {
                return KernLightingFieldClipPosition(positionOS);
            }

            half4 ProbeFrag() : SV_Target
            {
                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }
}
