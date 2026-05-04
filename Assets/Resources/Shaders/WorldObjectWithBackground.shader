Shader "Custom/WorldObjectWithBackground"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _BackgroundTex ("Background Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle]_CheckBackground("Check Background", Float) = 1
    }
    
    SubShader
    {
        Tags { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent"
        }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _CHECK_BACKGROUND_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 bgUV : TEXCOORD1;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 bgUV : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BackgroundTex);
            SAMPLER(sampler_BackgroundTex);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BackgroundTex_ST;
                float _Cutoff;
                float _CheckBackground;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.bgUV = TRANSFORM_TEX(input.bgUV, _BackgroundTex);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                half4 mainColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                #if _CHECK_BACKGROUND_ON
                half4 bgColor = SAMPLE_TEXTURE2D(_BackgroundTex, sampler_BackgroundTex, input.bgUV);
                
                if (mainColor.a < _Cutoff)
                {
                    return bgColor;
                }
                
                half3 finalColor = lerp(bgColor.rgb, mainColor.rgb, mainColor.a);
                return half4(finalColor, 1.0);
                #else
                return mainColor;
                #endif
            }
            ENDHLSL
        }
    }
}