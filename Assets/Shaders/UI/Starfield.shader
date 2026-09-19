Shader "Kern/UI/Starfield"
{
    // Procedural starfield for the main menu backdrop.
    //
    // Replaces a baked PNG of scattered dots. Baked stars cannot scintillate,
    // and their point spread was a hard 2px disc, which is what made the old
    // background read as noise rather than as a sky.
    //
    // Three things carry the realism here:
    //  - a magnitude distribution biased hard toward faint stars, so the eye
    //    picks out a handful of bright ones against a dense faint field rather
    //    than a uniform sprinkle;
    //  - a point spread with a tight core AND a wide low-amplitude wing, which
    //    is how a real point source lands on any optic - a bare Gaussian looks
    //    like a soft dot, the wing is what makes it look like a star;
    //  - colour drawn from a stellar temperature ramp, so the field is not
    //    monochrome white.
    //
    // Field scale was pulled down hard after the first pass: bright stars at
    // the old sizes (glow 0.16, density 68) rendered as 12-16px soft discs on
    // screen - a sky of blobs, not points, with visible edges that read as
    // aliasing. The current defaults sit at the fine end: the brightest stars
    // are ~2-3px across (core plus wing), the field is denser, and twinkle is
    // quieter so the sub-pixel faint field does not shimmer.
    Properties
    {
        _Density ("Star Density (cells across)", Range(10, 200)) = 96
        _Brightness ("Brightness", Range(0, 4)) = 1.6
        _CoreSize ("Core Size", Range(0.002, 0.08)) = 0.010
        _GlowSize ("Glow Size", Range(0.02, 0.6)) = 0.06
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.1
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 0.8
        _SkyColor ("Deep Sky Color", Color) = (0.012, 0.018, 0.032, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0, 2)) = 0.65
        _NebulaColor1 ("Nebula Deep Indigo", Color) = (0.025, 0.055, 0.11, 1)
        _NebulaColor2 ("Nebula Warm Dust", Color) = (0.09, 0.045, 0.025, 1)
        _ParallaxOffset ("Parallax Offset", Vector) = (0, 0, 0, 0)
        _ShaderTime ("Shader Time", Float) = 0
        _Aspect ("Aspect Ratio", Float) = 1.7777
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Density;
                float _Brightness;
                float _CoreSize;
                float _GlowSize;
                float _TwinkleAmount;
                float _TwinkleSpeed;
                float4 _SkyColor;
                float _NebulaIntensity;
                float4 _NebulaColor1;
                float4 _NebulaColor2;
                float4 _ParallaxOffset;
                float _ShaderTime;
                float _Aspect;
            CBUFFER_END

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

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = Hash12(i);
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float NebulaFbm(float2 p)
            {
                float val = 0.0;
                float amp = 0.5;
                float2 shift = float2(100.0, 100.0);
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    val += amp * ValueNoise(p);
                    p = (p * 2.1) + shift;
                    amp *= 0.5;
                }

                return val;
            }

            float3 EvaluateNebula(float2 uv)
            {
                float2 p = uv * 1.5;
                float2 warp = float2(
                    NebulaFbm(p),
                    NebulaFbm(p + float2(5.2, 1.3)));

                float n1 = NebulaFbm(p + (warp * 0.7));

                float dustMask = smoothstep(0.34, 0.76, n1);
                float gasMask = smoothstep(0.42, 0.82, n1 * 1.15);

                float3 nebula = (dustMask * _NebulaColor1.rgb) + (gasMask * _NebulaColor2.rgb * 0.65);
                return nebula * _NebulaIntensity;
            }

            // Rough main-sequence colour ramp, blue-white through to red dwarf.
            float3 StarColor(float t)
            {
                float3 blue = float3(0.72, 0.80, 1.00);
                float3 white = float3(1.00, 0.98, 0.96);
                float3 yellow = float3(1.00, 0.93, 0.78);
                float3 orange = float3(1.00, 0.80, 0.60);
                float3 red = float3(1.00, 0.68, 0.50);

                float3 c = lerp(blue, white, smoothstep(0.00, 0.22, t));
                c = lerp(c, yellow, smoothstep(0.22, 0.48, t));
                c = lerp(c, orange, smoothstep(0.48, 0.75, t));
                c = lerp(c, red, smoothstep(0.75, 1.00, t));
                return c;
            }

            // One layer of stars on a jittered grid. Neighbouring cells are
            // sampled too, so a star near a cell edge is not clipped by it.
            float3 StarLayer(float2 uv, float density, float sizeScale, float seed)
            {
                float2 grid = uv * density;
                float2 cell = floor(grid);
                float2 local = frac(grid);

                float3 accum = 0.0;
                float activeTime = _ShaderTime > 0.0 ? _ShaderTime : _Time.y;

                [unroll]
                for (int oy = -1; oy <= 1; oy++)
                {
                    [unroll]
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        float2 offset = float2(ox, oy);
                        float2 id = cell + offset + seed;

                        float2 jitter = Hash22(id);
                        float presence = Hash12(id + 7.13);

                        // Most cells are empty; a sparse field reads far more
                        // like a sky than one star per cell ever does.
                        if (presence < 0.62)
                        {
                            continue;
                        }

                        // Magnitude: pow() with a high exponent leaves only a
                        // few percent of stars visibly bright.
                        float m = Hash12(id + 19.7);
                        float mag = pow(m, 6.0);

                        float2 delta = local - offset - jitter;
                        float dist = length(delta);

                        // Brighter stars present a larger disc, as they do
                        // through any real optic.
                        float radius = sizeScale * (0.55 + (mag * 1.9));

                        float core = exp(-(dist * dist) / max(radius * radius, 1e-6));
                        float wing = radius / (radius + (dist * dist * 42.0));
                        float shape = core + (wing * 0.10);

                        // In deep space vacuum, stars do NOT twinkle: there is no atmosphere
                        // to refract light. Stars shine with pure, static crystal clarity.
                        // Micro-sensor variation is available if _TwinkleAmount > 0.
                        float phase = Hash12(id + 3.77) * 6.2831853;
                        float rate = 0.5 + (Hash12(id + 11.3) * 1.5);
                        float t = (activeTime * _TwinkleSpeed * rate);
                        float sensorBreathing = sin(t + phase) * 0.05;
                        float twinkle = 1.0 + (sensorBreathing * _TwinkleAmount);

                        float3 color = StarColor(Hash12(id + 5.19));
                        accum += color * shape * (mag + 0.03) * twinkle;
                    }
                }

                return accum;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv + _ParallaxOffset.xy;
                uv.x *= (_Aspect > 0.01 ? _Aspect : 1.7777);

                float3 nebula = EvaluateNebula(uv);

                float3 stars = StarLayer(uv, _Density, _CoreSize, 0.0);
                stars += StarLayer(uv, _Density * 0.43, _GlowSize * 0.22, 41.7) * 1.6;

                float3 color = _SkyColor.rgb + nebula + (stars * _Brightness);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
