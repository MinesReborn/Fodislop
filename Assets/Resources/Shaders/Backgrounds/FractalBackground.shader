Shader "Kern/Backgrounds/FractalBackground"
{
    Properties
    {
        _Speed ("Speed", Float) = 1.0
        [HDR] _ColorTint ("Color Tint", Color) = (1, 1, 1, 1)
        [IntRange] _Iterations ("Iterations", Range(10, 99)) = 50
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
        }

        Pass
        {
            Name "FractalBackground"
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

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

            CBUFFER_START(UnityPerMaterial)
                float _Speed;
                float4 _ColorTint;
                int _Iterations;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 fragCoord = input.uv * _ScreenParams.xy;
                float2 resolution = _ScreenParams.xy;

                float z = 0;
                float d;
                float4 O = float4(0, 0, 0, 0);

                float time = _Time.y * _Speed;

                // Луч не зависит ни от номера итерации, ни от z: он один на
                // пиксель. Считался внутри цикла — пятьдесят normalize на
                // пиксель вместо одного, и это на весь экран каждый кадр.
                float3 rayDirection = normalize(
                    float3(2.0 * fragCoord - resolution.xy, resolution.y));

                // Раньше здесь крутился цикл `d = 1; повторить пять раз d += d`.
                // Он не зависит ни от чего и всегда даёт 32 — то есть двести
                // пятьдесят сложений на пиксель ради константы.
                const float foldFrequency = 32.0;

                for (int iter = 0; iter < _Iterations; iter++)
                {
                    float3 p = z * rayDirection;

                    p.z -= time;

                    p += sin(p * foldFrequency + p.z * foldFrequency) / foldFrequency;

                    float2 offset = float2(0, 2);
                    d = 0.1 * length(1.0 + p.xy * sin(p.z + offset));
                    z += d;

                    O += (float4(0.7, 0.7, 0.7, 0.7) - (p.y / z) * float4(0, 1, 2, 0)) / d;
                }

                O = tanh(O / 2000.0);
                return float4(saturate(O.rgb * _ColorTint.rgb), 1);
            }
            ENDHLSL
        }
    }
}
