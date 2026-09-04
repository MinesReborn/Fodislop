Shader "Fodinae/UI/PlanetSurface"
{
    // Поверхность планеты главного меню: три выборки из запечённых
    // равнопромежуточных карт и обычный GGX. Никакого шума в рантайме — весь
    // рельеф, породы и рифты посчитаны заранее в scripts/generate_planet_maps.py.
    //
    // Смещения нет: силуэт остаётся гладким, что для планеты с орбиты и верно —
    // рельеф читается через затенение, а не через профиль.
    Properties
    {
        [Header(Baked Maps)]
        [NoScaleOffset] _AlbedoMap ("Albedo (equirect, sRGB)", 2D) = "gray" {}
        [NoScaleOffset] _NormalMap ("Normal (equirect, linear)", 2D) = "bump" {}
        [NoScaleOffset] _PackedMap ("R Roughness / G Rift / B Clouds", 2D) = "white" {}

        [Header(Lighting)]
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.68, 0.24, 0.52, 0)
        _SunColor ("Sun Color", Color) = (1.0, 0.92, 0.82, 1)
        _SunIntensity ("Sun Intensity", Range(0, 8)) = 4.6
        _NightAmbient ("Night Ambient", Range(0, 0.05)) = 0.004
        _TwilightColor ("Twilight Scatter", Color) = (0.58, 0.65, 0.22, 1)
        _TwilightIntensity ("Twilight Intensity", Range(0, 2)) = 1.10

        // Ширина терминатора.
        //
        // Чистый ламбертов косинус обрывается в ноль с ненулевой производной,
        // и на шаре это читается как проведённая ножом граница. У планеты её
        // размывает рассеяние в атмосфере: свет заворачивается за лимб и ночная
        // сторона у края остаётся подсвеченной. Обёртка сдвигает и сжимает
        // косинус, растягивая переход на заметную полосу вместо линии.
        _TerminatorSoftness ("Terminator Softness", Range(0, 0.6)) = 0.34

        [Header(Surface)]
        // Верхняя граница намеренно не доходит до зеркала. Именно низкая
        // шероховатость превращает планету в миску с маслом, и оставлять
        // ползунок, которым это можно сделать одним движением, незачем.
        _RoughnessMin ("Roughness Min", Range(0.65, 0.9)) = 0.68
        _RoughnessMax ("Roughness Max", Range(0.65, 0.9)) = 0.88
        _NormalStrength ("Normal Strength", Range(0, 0.6)) = 0.22

        [Header(Rifts)]
        _MagmaColor ("Magma Color", Color) = (1.0, 0.35, 0.08, 1)
        _MagmaIntensity ("Magma Intensity", Range(0, 2)) = 2.0

        [Header(Clouds)]
        _CloudColor ("Cloud Color", Color) = (0.86, 0.80, 0.66, 1)
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.72
        _CloudCoverage ("Cloud Coverage Threshold", Range(0, 1)) = 0.48
        _CloudSoftness ("Cloud Edge Softness", Range(0.01, 0.5)) = 0.18

        [Header(Exposure)]
        _Exposure ("Exposure", Range(0.1, 4)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_AlbedoMap);
            SAMPLER(sampler_AlbedoMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_PackedMap);
            SAMPLER(sampler_PackedMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirWS;
                float4 _SunColor;
                float _SunIntensity;
                float _NightAmbient;
                float4 _TwilightColor;
                float _TwilightIntensity;
                float _TerminatorSoftness;
                float _RoughnessMin;
                float _RoughnessMax;
                float _NormalStrength;
                float4 _MagmaColor;
                float _MagmaIntensity;
                float4 _CloudColor;
                float _CloudOpacity;
                float _CloudCoverage;
                float _CloudSoftness;
                float _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.directionOS = normalize(input.positionOS.xyz);
                return output;
            }

            // Развёртка направления в равнопромежуточные координаты.
            float2 DirectionToEquirect(float3 dir)
            {
                return float2(
                    (atan2(dir.z, dir.x) * (0.5 / PI)) + 0.5,
                    (asin(clamp(dir.y, -1.0, 1.0)) * (1.0 / PI)) + 0.5);
            }

            // Аппаратные производные считаются по квадрату 2x2 пикселей. На
            // меридиане, где u перескакивает с 1 на 0, ddx(u) внутри квадрата
            // получается близким к единице — то есть «текстура сжата в точку», —
            // и выбирается самый мелкий мип: по всей высоте планеты идёт
            // резкий шов.
            //
            // Та же производная, снятая с развёрнутой на пол-оборота копии, на
            // этом меридиане непрерывна — но разрывается уже на
            // противоположном. Верна везде та из двух, что меньше по модулю:
            // скачок всегда даёт заведомо большее значение.
            float2 SeamlessDerivative(float2 d, float2 dShifted)
            {
                return abs(d) < abs(dShifted) ? d : dShifted;
            }

            struct EquirectGrad
            {
                float2 dx;
                float2 dy;
            };

            EquirectGrad EquirectGradients(float2 uv)
            {
                float2 shifted = frac(uv + float2(0.5, 0.0));

                EquirectGrad grad;
                grad.dx = SeamlessDerivative(ddx(uv), ddx(shifted));
                grad.dy = SeamlessDerivative(ddy(uv), ddy(shifted));
                return grad;
            }

            float3 SampleEquirectRGB(TEXTURE2D_PARAM(tex, samp), float2 uv, EquirectGrad grad)
            {
                return SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, grad.dx, grad.dy).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.directionOS);
                float2 uv = DirectionToEquirect(dir);

                EquirectGrad grad = EquirectGradients(uv);

                float3 albedo = SampleEquirectRGB(TEXTURE2D_ARGS(_AlbedoMap, sampler_AlbedoMap), uv, grad);
                float3 packed = SampleEquirectRGB(TEXTURE2D_ARGS(_PackedMap, sampler_PackedMap), uv, grad);
                float3 normalTS = (SampleEquirectRGB(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), uv, grad) * 2.0) - 1.0;

                float roughness = lerp(_RoughnessMin, _RoughnessMax, saturate(packed.r));
                float rift = packed.g;
                float cloudCoverage = packed.b;

                // Касательный базис строится тем же способом, каким считалась
                // карта: у запекания и у чтения должен быть один базис, иначе
                // рельеф развернётся относительно освещения.
                float3 geometricNormalWS = normalize(TransformObjectToWorldNormal(dir));
                float3 up = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangentOS = normalize(cross(up, dir));
                float3 bitangentOS = cross(dir, tangentOS);
                float3 tangentWS = normalize(TransformObjectToWorldDir(tangentOS));
                float3 bitangentWS = normalize(TransformObjectToWorldDir(bitangentOS));

                float3 normalWS = normalize(
                    (tangentWS * normalTS.x * _NormalStrength)
                    + (bitangentWS * normalTS.y * _NormalStrength)
                    + geometricNormalWS);

                float3 lightWS = normalize(_SunDirWS.xyz);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfWS = normalize(lightWS + viewWS);

                float NdotL = saturate(dot(normalWS, lightWS));
                float NdotV = saturate(dot(normalWS, viewWS));
                float NdotH = saturate(dot(normalWS, halfWS));
                float VdotH = saturate(dot(viewWS, halfWS));

                float alpha = roughness * roughness;
                float alphaSq = alpha * alpha;
                float denom = (NdotH * NdotH * (alphaSq - 1.0)) + 1.0;
                float distribution = alphaSq / max(PI * denom * denom, 1e-4);
                float k = ((roughness + 1.0) * (roughness + 1.0)) * 0.125;
                float geometry = (NdotL / max(NdotL * (1.0 - k) + k, 1e-4))
                    * (NdotV / max(NdotV * (1.0 - k) + k, 1e-4));
                float3 fresnel = 0.04 + (0.96 * pow(1.0 - VdotH, 5.0));
                float3 specular = (distribution * geometry * fresnel) / max(4.0 * NdotL * NdotV, 1e-4);

                // Облака лежат на той же сфере, а не на отдельной оболочке:
                // движения нет, а значит и параллакс между палубой и грунтом
                // никогда не проявится — платить за вторую сферу не за что.
                float cloud = smoothstep(
                    _CloudCoverage,
                    _CloudCoverage + _CloudSoftness,
                    cloudCoverage) * _CloudOpacity;
                albedo = lerp(albedo, _CloudColor.rgb, cloud);
                // Под облаком порода не видна, значит и рифт из-под него не светит.
                rift *= 1.0 - cloud;
                roughness = lerp(roughness, 0.95, cloud);

                float3 sun = _SunColor.rgb * _SunIntensity;

                // Обёрнутый косинус только для диффуза. Зеркальная часть берёт
                // сырой NdotL: обёртка там зажгла бы блик на неосвещённой
                // стороне, где его физически быть не может.
                float wrap = _TerminatorSoftness;
                float wrappedNdotL = saturate((dot(normalWS, lightWS) + wrap) / (1.0 + wrap));
                float3 diffuse = (albedo / PI) * (1.0 - fresnel) * wrappedNdotL;
                float3 direct = (diffuse + specular) * sun;

                // Сумеречный член: у терминатора свет идёт через толщу
                // атмосферы. Без него граница дня и ночи режет диск ножом.
                float twilight = pow(saturate(1.0 - abs(dot(normalWS, lightWS))), 1.8)
                    * saturate(dot(normalWS, lightWS) + 0.55);
                float3 scatter = albedo * _TwilightColor.rgb * twilight * _TwilightIntensity;

                float3 emission = _MagmaColor.rgb * rift * _MagmaIntensity;
                float3 ambient = albedo * _NightAmbient;

                float3 linearColor = (direct + scatter + emission + ambient) * _Exposure;

                // UI Toolkit composites this texture through its SDR UI target.
                // Keep a local shoulder so highlights cannot clip into solid
                // blocks before Unity maps the UI plane to HDR paper white.
                float3 mapped = (linearColor * ((2.51 * linearColor) + 0.03))
                    / ((linearColor * ((2.43 * linearColor) + 0.59)) + 0.14);
                return half4(saturate(mapped), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
