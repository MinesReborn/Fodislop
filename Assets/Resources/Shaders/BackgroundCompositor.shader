Shader "Custom/BackgroundCompositor"
{
    Properties
    {
        _MainTex ("Background Atlas", 2D) = "white" {}
        _ObjectTex ("Objects Render Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_ObjectTex);
            SAMPLER(sampler_ObjectTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 bgColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 objColor = SAMPLE_TEXTURE2D(_ObjectTex, sampler_ObjectTex, input.uv);

                if (objColor.a > 0.1)
                {
                    return objColor;
                }

                return bgColor;
            }
            ENDHLSL
        }
    }
}
