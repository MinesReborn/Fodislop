Shader "Kern/UI/MissionVirtualRing"
{
    // Указатель направления на цель миссии: дуга на окружности вокруг
    // игрока, стоящая на пеленге цели. Ничего не вращается и не бежит по
    // кольцу — это указатель, а не эффект. Дуга меняет положение только
    // тогда, когда меняется само направление на цель.
    //
    // Шейдер собственный, но рисует его UI Toolkit: материал подаётся прямо
    // на элемент через style.unityMaterial (Unity 6.3+). Промежуточной
    // RenderTexture нет и быть не должно — она давала мыло, потому что кадр
    // фиксированной стороны растягивался фильтрацией на элемент во весь
    // вьюпорт. Здесь дуга считается в разрешении самой панели.
    //
    // Контракт UITK-шейдера (Assets ← PackageCache):
    //   * тег isCustomUITKShader — по нему UI Toolkit отличает свой шейдер;
    //   * Internal/UnityUIE.cginc — встроенная библиотека UI Toolkit;
    //   * uie_std_vert — трансформ элемента (translation → bone → group →
    //     clip) и разбор упакованных идентификаторов вершины;
    //   * uie_fragment_clip — прямоугольное отсечение родителями.
    // Всё это повторяет то, что Shader Graph генерирует для таргета UITK
    // (UniversalUISubTarget + UITKPass.hlsl), только руками и без графа.
    //
    // Альфа прямая, не домноженная, и такое же смешивание задаёт сам UITK
    // (SrcAlpha/OneMinusSrcAlpha): цвет остаётся полным и там, где альфа
    // близка к нулю, иначе фильтрация даёт чёрную кайму по краю дуги.
    //
    // Все каналы цвета лежат в 0..1: белый интерфейса равен paper white,
    // яркость выше единицы интерфейсу не принадлежит.
    Properties
    {
        _Color ("Ring Color", Color) = (1, 0.45, 0.12, 1)
        _MissionOpacity ("Mission Opacity", Range(0, 1)) = 1
        _MissionAngle ("Mission Angle", Float) = 0
        [HideInInspector] _RingRadius ("Ring Radius", Float) = 0.74
        [HideInInspector] _RingSquash ("Ring Squash", Float) = 0.90
        [HideInInspector] _ArcHalfAngle ("Arc Half Angle", Float) = 0.50
        [HideInInspector] _ArcThickness ("Arc Thickness", Float) = 0.030
        [HideInInspector] _ArcEndTaper ("Arc End Taper", Float) = 0.45
        [HideInInspector] _ArcCoreWidth ("Arc Core Width", Float) = 0.35
        [HideInInspector] _ArcEnergy ("Arc Energy", Float) = 1
        [HideInInspector] _CoreEnergy ("Core Energy", Float) = 0.85
        [HideInInspector] _CoreColor ("Core Color", Color) = (1, 0.95, 0.86, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "isCustomUITKShader" = "true"
        }

        Pass
        {
            // Состояния ровно те, что UITK ставит своему проходу
            // (UITKRenderStates.GenerateRenderStateDeclaration).
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            // Варианты, которые UI Toolkit выбирает сам. Объявить их обязан
            // шейдер: без нужного варианта рисовать будет нечем. Набор тот
            // же, что у сгенерированного прохода (UITKKeywords.Default).
            #pragma multi_compile_local __ _UIE_FORCE_GAMMA
            #pragma multi_compile_local __ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1
            #pragma multi_compile_local __ _UIE_RENDER_TYPE_SOLID _UIE_RENDER_TYPE_TEXTURE _UIE_RENDER_TYPE_TEXT _UIE_RENDER_TYPE_GRADIENT

            #include "Internal/UnityUIE.cginc"

            #define KERN_TAU 6.28318530718

            float4 _Color;
            float _MissionOpacity;
            float _MissionAngle;
            float _RingRadius;
            float _RingSquash;
            float _ArcHalfAngle;
            float _ArcThickness;
            float _ArcEndTaper;
            float _ArcCoreWidth;
            float _ArcEnergy;
            float _CoreEnergy;
            float4 _CoreColor;

            // Координата внутри элемента приезжает в layoutUV (uv.zw) и
            // кладётся в uvClip.xy: у сплошного элемента это поле занято
            // текстурной выборкой, которой здесь нет. Своего интерполятора
            // заводить не нужно — v2f у UITK фиксирован.
            v2f Vert(appdata_t input)
            {
                v2f output = uie_std_vert(input);
                output.uvClip.xy = input.uv.zw;
                return output;
            }

            // Кратчайший угол между направлениями, без разрыва на ±пи.
            float AngularDistance(float left, float right)
            {
                return abs(atan2(sin(left - right), cos(left - right)));
            }

            // Колокол вместо smoothstep: у дуги нет резкой границы, а
            // экспонента дешевле ветвления и не даёт ступеньки.
            float Bell(float distance, float width)
            {
                float normalized = distance / max(width, 1e-4);
                return exp(-normalized * normalized);
            }

            UIE_FRAG_T Frag(v2f input) : SV_Target
            {
                float2 p = (input.uvClip.xy * 2.0) - 1.0;
                p.y /= _RingSquash;

                float radius = length(p);
                float angle = atan2(p.y, p.x);

                // Дуга обрывается к концам, а не отрезается: резкий торец
                // читается как деталь интерфейса, плавный — как указатель.
                float angular = AngularDistance(angle, _MissionAngle);
                float along = saturate(angular / _ArcHalfAngle);
                float taper = 1.0 - (along * along);

                // Поперёк дуга тоньше к концам по той же причине.
                float thickness = _ArcThickness * lerp(1.0, _ArcEndTaper, along);
                float band = Bell(radius - _RingRadius, thickness);

                float arc = band * taper;

                // Ядро: узкая яркая линия по центру дуги. Без неё указатель
                // выглядит размытым пятном и не даёт точного направления.
                float core = Bell(radius - _RingRadius, thickness * _ArcCoreWidth) * taper;

                float3 emissive =
                    (_Color.rgb * arc * _ArcEnergy) +
                    (_CoreColor.rgb * core * _CoreEnergy);
                float weight = (arc * _ArcEnergy) + (core * _CoreEnergy);

                // Делим на сырую сумму весов, а не на итоговую альфу: после
                // домножения на прозрачность нормировка цвета перестала бы
                // быть нормировкой и ядро бы выбеливало.
                float3 color = saturate(emissive / max(weight, 1e-4));

                // Прозрачность элемента уже лежит в альфе вершинного цвета:
                // uie_std_vert домножает на неё color.a для сплошного типа.
                // Читать её отдельно значило бы применить дважды.
                float coverage = saturate(weight) * _MissionOpacity *
                    _Color.a * input.color.a;

                // Отсечение прямоугольниками родителей. Без него элемент
                // вылезает за свой контейнер, потому что UITK выполняет это
                // отсечение во фрагменте, а не ножницами.
                float clipping = uie_fragment_clip(input.uvClip.zw);
                clip(clipping - 0.5);

                return UIE_FRAG_T(color, coverage);
            }

            ENDCG
        }
    }
}
