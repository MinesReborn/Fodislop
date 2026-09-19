Shader "Kern/UI/UnpremultiplyAlpha"
{
    // Converts a premultiplied-alpha render target into straight (unassociated)
    // alpha.
    //
    // The scenery camera has to composite internally with premultiplied alpha -
    // that is the only operator that lets the atmosphere both add in-scattered
    // light and attenuate the crust behind it in a single pass. But UI Toolkit
    // draws its Image elements with a plain SrcAlpha / OneMinusSrcAlpha blend,
    // which multiplies by alpha a second time. The opaque disc (alpha 1) is
    // unaffected, so the error is invisible there, while the atmosphere limb at
    // alpha ~0.2 came out at ~0.04 of its intended brightness - i.e. the
    // atmosphere was present in the render target and then almost entirely
    // erased by the UI blend.
    //
    // The same blit is also the only place the render is anti-aliased. The
    // camera target carries no MSAA: multisampling only supersamples triangle
    // coverage - it cannot touch the aliasing that lives inside a fragment: the
    // fractal crust detail, the cloud deck's zonal bands and the sub-pixel
    // orbit line all shimmer because of shader-generated high frequency, not
    // geometry edges - and the one edge it would help with, the limb, this pass
    // already covers. So MSAA was pure cost on every pixel of the target for a
    // single arc. This pass applies a lightweight FXAA (a 9-tap luma-guided
    // blend) to the premultiplied image, then unpremultiplies. Blending the
    // premultiplied RGBA directly is the correct operator here: at the
    // atmosphere limb the premultiplied colour is exactly the coverage-weighted
    // average, so an edge blend and the alpha blend agree.
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Not declared by the TEXTURE2D macro in this include context -
            // without this the fragment fails to compile and the whole menu
            // renders as a magenta screen (broken resolve shader).
            float4 _MainTex_TexelSize;

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half Luma(half3 rgb)
            {
                return dot(rgb, half3(0.299, 0.587, 0.114));
            }

            // FXAA 3.11 ("fast" variant), operating on the premultiplied RGBA so
            // the same blend that smooths colour also smooths coverage.
            half4 Fxaa(half2 uv, half2 texelSize)
            {
                half4 cNW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(-texelSize.x, -texelSize.y));
                half4 cNE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(texelSize.x, -texelSize.y));
                half4 cSW = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(-texelSize.x, texelSize.y));
                half4 cSE = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + half2(texelSize.x, texelSize.y));
                half4 cM = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                half lumaNW = Luma(cNW.rgb);
                half lumaNE = Luma(cNE.rgb);
                half lumaSW = Luma(cSW.rgb);
                half lumaSE = Luma(cSE.rgb);
                half lumaM = Luma(cM.rgb);

                half lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
                half lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));

                // Relative contrast gate: smooth gradients (terminator, haze,
                // shading falloff) must pass through untouched. The scene is
                // HDR, so the gate scales with the local brightness instead of
                // being an LDR constant.
                half span = lumaMax - lumaMin;
                if (span < max(lumaMax, 0.02) * 0.03)
                {
                    return cM;
                }

                half2 dir;
                dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
                dir.y = ((lumaNW + lumaSW) - (lumaNE + lumaSE));

                half dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * 0.25 * 0.125, 1.0 / 128.0);
                half rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);

                dir = min(half2(8.0, 8.0), max(half2(-8.0, -8.0), dir * rcpDirMin)) * texelSize;

                half4 rgbA = 0.5 * (
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + dir * (1.0 / 3.0 - 0.5)) +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + dir * (2.0 / 3.0 - 0.5)));

                half4 rgbB = rgbA * 0.5 + 0.25 * (
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + dir * -0.5) +
                    SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + dir * 0.5));

                half lumaB = Luma(rgbB.rgb);
                return (lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = Fxaa(input.uv, _MainTex_TexelSize.xy);

                // Fully transparent texels carry no colour to recover, and the
                // guard keeps the divide from exploding there.
                c.rgb /= max(c.a, 1e-4);
                return c;
            }
            ENDHLSL
        }
    }
}
