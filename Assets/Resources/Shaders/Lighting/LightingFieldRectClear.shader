Shader "Hidden/Kern/LightingFieldRectClear"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ClearAmbientOcclusionRect"
            Blend Off
            ColorMask A
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                return float4(uv * 2.0 - 1.0, 0.0, 1.0);
            }

            half4 Frag() : SV_Target
            {
                return 0.0;
            }
            ENDHLSL
        }
    }
}
