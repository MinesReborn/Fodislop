Shader "Hidden/Kern/WorldBloomAdd"
{
    Properties
    {
        [HideInInspector] _WorldBloomSceneBlend ("Scene blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend One [_WorldBloomSceneBlend]
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment AddBloom
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _WorldBloomIntensity;
            float4 AddBloom(Varyings input) : SV_Target
            {
                return float4(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp,
                    input.texcoord, 0).rgb * _WorldBloomIntensity, 0.0);
            }
            ENDHLSL
        }
    }
}
