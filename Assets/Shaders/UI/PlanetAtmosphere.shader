Shader "Kern/UI/PlanetAtmosphere"
{
    // Оболочка атмосферы: сфера примерно на полтора процента больше
    // поверхности, аддитивная, без записи в глубину. Никакого объёмного
    // рассеяния — форма держится на длине хорды луча внутри оболочки и на том,
    // что свечение гаснет к ночной стороне.
    //
    // Дешёвая атмосфера портит планету двумя способами: ровным кольцом по
    // всему кругу, если не гасить теневую сторону, и жёстким внешним краем,
    // если брать степень френеля — та максимальна ровно на силуэте оболочки и
    // обрывается вместе с геометрией. Оба лечатся здесь.
    Properties
    {
        _AtmosphereColor ("Atmosphere Color", Color) = (0.42, 0.56, 0.78, 1)
        _HorizonColor ("Horizon Color (grazing)", Color) = (0.86, 0.72, 0.52, 1)
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.68, 0.24, 0.52, 0)

        _Density ("Density", Range(0, 4)) = 1.15
        _RimPower ("Rim Falloff", Range(0.3, 4)) = 1.1

        // Отношение радиуса поверхности к радиусу оболочки. Должно совпадать с
        // тем, что стоит в сцене: из него считается длина хорды, а по ней —
        // где свечение гаснет.
        _RadiusRatio ("Surface / Shell Radius", Range(0.9, 0.999)) = 0.98522

        _SunWrap ("Sun Wrap (terminator softness)", Range(0, 1)) = 0.35
        _NightFloor ("Night Side Floor", Range(0, 0.2)) = 0.02

        // Прямое рассеяние: на просвет у самого лимба, со стороны звезды,
        // атмосфера вспыхивает заметно ярче — это и читается как воздух.
        _ForwardScatter ("Forward Scatter", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Blend One One
            ZWrite Off
            // Ближняя стенка оболочки, а не дальняя. Дальняя почти целиком
            // закрыта самой планетой по глубине, и от неё осталось бы только
            // кольцо шириной в те самые полтора процента радиуса — то есть
            // обводка, а не атмосфера. Ближняя лежит поверх всего диска, и
            // френель гасит её к центру сам.
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosphereColor;
                float4 _HorizonColor;
                float4 _SunDirWS;
                float _Density;
                float _RimPower;
                float _RadiusRatio;
                float _SunWrap;
                float _NightFloor;
                float _ForwardScatter;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(TransformObjectToWorldNormal(normalize(input.positionOS.xyz)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 lightWS = normalize(_SunDirWS.xyz);

                // Длина хорды луча внутри оболочки, а не степень френеля.
                //
                // Степень 1-|N.V| максимальна ровно на силуэте оболочки и там
                // обрывается вместе с геометрией: получается полоса с жёстким
                // внешним краем — щит вокруг планеты, а не воздух. Хорда же
                // максимальна у лимба ПОВЕРХНОСТИ и сама сходит в ноль на краю
                // оболочки, потому что там лучу проходить уже нечего.
                float impact = sqrt(saturate(1.0 - (dot(normalWS, viewWS) * dot(normalWS, viewWS))));
                float outerHalf = sqrt(saturate(1.0 - (impact * impact)));
                float innerHalf = sqrt(saturate((_RadiusRatio * _RadiusRatio) - (impact * impact)));

                // Нормировка на максимум, который достигается у лимба
                // поверхности: без неё плотность зависела бы от толщины
                // оболочки, и подбор пришлось бы повторять при каждой правке
                // масштаба в сцене.
                float maxChord = max(sqrt(saturate(1.0 - (_RadiusRatio * _RadiusRatio))), 1e-4);
                float rim = pow(saturate((outerHalf - innerHalf) / maxChord), _RimPower);

                // Обёрнутое освещение вместо чистого N.L: у терминатора
                // атмосфера светится и там, куда прямой луч уже не достаёт.
                float NdotL = dot(normalWS, lightWS);
                float sunAmount = saturate((NdotL + _SunWrap) / (1.0 + _SunWrap));
                sunAmount = max(sunAmount, _NightFloor);

                // На просвет: смотрим почти вдоль луча звезды.
                float forward = pow(saturate(dot(viewWS, -lightWS)), 6.0) * _ForwardScatter;

                float3 tint = lerp(_AtmosphereColor.rgb, _HorizonColor.rgb, saturate(rim * 1.4));
                float3 color = tint * rim * sunAmount * _Density * (1.0 + forward);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
