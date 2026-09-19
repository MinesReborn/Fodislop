Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        // Runtime materials must inject both textures. Neutral shader values
        // deliberately make a missing injection visible instead of rendering
        // an implicit white/gray world.
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "black" {}
        _FlowMap ("Shimmer Flow Map", 2D) = "black" {}
        _ShimmerColor ("Shimmer Color", Color) = (0,0,0,0)
        _FlowScale ("Flow Scale", Vector) = (0,0,0,0)
        _ShimmerSpeedScale ("Shimmer Speed Scale", Float) = 0
        _PulseSpeedScale ("Pulse Speed Scale", Float) = 0
        _DebugColor ("Debug Color", Color) = (0,0,0,0)
        [ToggleUI] _DebugMode ("Debug Mode", Float) = 0
        [HideInInspector] _TerrainAtlasIndex ("Terrain Atlas Index", Float) = 0
        [HideInInspector] _TerrainAtlas0 ("Terrain Atlas 0", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas1 ("Terrain Atlas 1", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas2 ("Terrain Atlas 2", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas3 ("Terrain Atlas 3", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas4 ("Terrain Atlas 4", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas5 ("Terrain Atlas 5", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas6 ("Terrain Atlas 6", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas7 ("Terrain Atlas 7", 2D) = "black" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ KERN_WORLD_LIGHTING
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainColorAnimation.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/TerrainCellData.hlsl"

            #define EPS 0.0001

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float4 glowAttr     : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 glowData     : TEXCOORD7;
                nointerpolation float atlasIndex : TEXCOORD8;
                nointerpolation float isForeground : TEXCOORD9;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // ВСЁ, ЧТО ЗАВИСИТ ОТ МАТЕРИАЛА, ОБЯЗАНО ЛЕЖАТЬ ЗДЕСЬ.
            //
            // SRP Batcher склеивает вызовы отрисовки только у шейдеров, где ни
            // одно свойство материала не объявлено снаружи UnityPerMaterial.
            // `_BaseMap_TexelSize` Unity заводит сам под текстуру _BaseMap, то
            // есть это свойство материала; стоя снаружи, оно ломало совместимость
            // целиком, и батчер молча выключался на всём террейне — счётчик
            // пакетов показывал ноль при трёх сотнях смен материала.
            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _FlowScale;
                float _ShimmerSpeedScale;
                float _PulseSpeedScale;
                float4 _DebugColor;
                float _DebugMode;
                float4 _BaseMap_TexelSize;
                float _TerrainAtlasIndex;
                float4 _TerrainAtlas0_TexelSize;
                float4 _TerrainAtlas1_TexelSize;
                float4 _TerrainAtlas2_TexelSize;
                float4 _TerrainAtlas3_TexelSize;
                float4 _TerrainAtlas4_TexelSize;
                float4 _TerrainAtlas5_TexelSize;
                float4 _TerrainAtlas6_TexelSize;
                float4 _TerrainAtlas7_TexelSize;
            CBUFFER_END

            Texture2D<float4> _WorldLightTexture;
            SamplerState sampler_WorldLightTexture;
            float4 _WorldLightRect;
            float4 _WorldLightTextureSize;
            int _WorldLightDebugView;
            int _WorldLightPerBlock;

            float2 GetWorldLightUv(float2 worldPos)
            {
                float2 rectSize = max(_WorldLightRect.zw, float2(0.0001, 0.0001));
                return saturate((worldPos - _WorldLightRect.xy) / rectSize);
            }

            float3 GetWorldLightColor(float2 worldPos)
            {
                #if !defined(KERN_WORLD_LIGHTING)
                    return 1.0;
                #else
                float2 lightUV = GetWorldLightUv(worldPos);
                if (_WorldLightPerBlock != 0 || (_WorldLightDebugView >= 1 && _WorldLightDebugView <= 3))
                {
                    int2 debugPixel = clamp(
                        int2(lightUV * _WorldLightTextureSize.xy),
                        int2(0, 0),
                        int2(_WorldLightTextureSize.xy) - 1);
                    return _WorldLightTexture.Load(int3(debugPixel.x, debugPixel.y, 0)).rgb;
                }

                return _WorldLightTexture.Sample(
                    sampler_WorldLightTexture,
                    lightUV).rgb;
                #endif
            }

            // AO вокруг блоков по полю занятости.
            //
            // Радиус задаётся мипом: цепь у поля уже построена стадией
            // освещения, и мип 2 усредняет занятость по четырём текселям —
            // это и есть спад на несколько клеток от свободной стороны.
            // Тень под самим блоком не рисуется: её всё равно не видно.
            Texture2D<float4> _WorldOccupancyTexture;
            SamplerState sampler_WorldOccupancyTexture;
            int _WorldOccupancyYFlip;

            // Числа живут в TerrainLook (Assets/Scripts/Core/Rendering/VisualTuning.cs)
            // и приезжают сюда глобалями один раз при старте.
            float _TerrainAmbientOcclusionMip;
            float _TerrainAmbientOcclusionStrength;

            float GetAmbientOcclusion(float2 worldPos)
            {
                float2 uv = (worldPos - _WorldLightRect.xy) /
                    max(_WorldLightRect.zw, float2(0.0001, 0.0001));
                if (_WorldOccupancyYFlip != 0)
                {
                    uv.y = 1.0 - uv.y;
                }

                float nearby = _WorldOccupancyTexture.SampleLevel(
                    sampler_WorldOccupancyTexture,
                    saturate(uv),
                    _TerrainAmbientOcclusionMip).a;
                // Корень выравнивает углы. Мип усредняет по квадрату: у плоской
                // стороны в окрестности занята половина, у выпуклого угла —
                // четверть, и без правки выходит крест вместо кольца. Корень
                // поднимает четверть до половины, а половину только до 0.7, то
                // есть тянет вверх слабое сильнее, чем сильное.
                return saturate(sqrt(nearby) * _TerrainAmbientOcclusionStrength);
            }

            float MissingTextureHash(float2 position)
            {
                float3 p = frac(float3(position, position.x + position.y) *
                    float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 SampleMissingTexture(float2 worldPosition)
            {
                float2 cell = floor(worldPosition);
                float hue = MissingTextureHash(cell);
                float value = lerp(0.35, 0.8, MissingTextureHash(cell + 17.0));
                float saturation = lerp(0.55, 0.9, MissingTextureHash(cell + 43.0));
                return TerrainHSVToRGB(float3(hue, saturation, value));
            }

            // Тумблер режима выборки. Ноль — ближайшая без сглаживания,
            // единица — со сглаженной границей текселя. Раздаётся глобально
            // из DisplayManager: террейн и сущности рисуются разными
            // материалами, часть из них создаётся в рантайме.
            float _PixelArtFiltering;

            // Сглаженная ближайшая выборка.
            //
            // ОТКУДА МУАР. Тайл занимает 32 текселя, а на экране тексель
            // занимает дробное число пикселей — при высоте 1080 и обычном
            // зуме около 4.8. Ближайшая выборка обязана в этом случае
            // какие-то строки текселей вывести дважды, а какие-то потерять:
            // на регулярной кладке это муар, и он ползёт вместе с камерой.
            //
            // ЧТО ДЕЛАЕТ ЭТА ФУНКЦИЯ. Оставляет ближайшую выборку внутри
            // текселя и размывает только его границу — ровно на ширину
            // одного экранного пикселя, которую даёт fwidth. Тексель
            // остаётся плоским квадратом, а переход между соседями
            // перестаёт быть скачком, поэтому лишняя или потерянная строка
            // больше не возникает.
            //
            // Сглаживание идёт по ширине пикселя, а не по фиксированной
            // доле текселя: иначе на приближении картинка размывалась бы
            // тем сильнее, чем крупнее тексель, — а нужно ровно обратное.
            float2 PixelArtSampleUV(float2 uv, float2 textureSize)
            {
                if (_PixelArtFiltering < 0.5)
                {
                    return uv;
                }

                float2 uvTexels = uv * textureSize;
                float2 seam = floor(uvTexels + 0.5);
                float2 pixelWidth = max(fwidth(uvTexels), 1e-5);
                uvTexels = seam + clamp((uvTexels - seam) / pixelWidth, -0.5, 0.5);
                return uvTexels / textureSize;
            }

            float3 SampleFlowMap(float2 worldPos)
            {
                return SAMPLE_TEXTURE2D(
                    _FlowMap,
                    sampler_FlowMap,
                    worldPos / _FlowScale.xy).rgb;
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
            #if defined(KERN_TERRAIN_CELLS)
                // Один материал на все атласы: меш проходится один раз, атлас
                // выбирается во фрагменте по индексу квада.
                TerrainCellVertex cell = LoadTerrainCellVertex(input.positionOS.xyz, input.uv);
                output.positionCS = cell.atlasIndex >= 0.0
                    ? TransformObjectToHClip(cell.positionOS)
                    : TerrainCulledPosition();
                output.atlasIndex = cell.atlasIndex;
                output.uv = cell.uv;
                output.color = cell.color;
                output.subAtlasRect = cell.subAtlasRect;
                output.tileSizeUV = cell.tileSizeUV;
                output.worldPos = cell.worldPos;
                output.worldPosition = TransformObjectToWorld(cell.positionOS);
                output.glowData = cell.glowData;
                output.animData = cell.animData;
                output.packedData = cell.packedData;
                output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;
                return output;
            #endif
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.glowData = input.glowAttr;
                output.animData = input.animData;
                output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;
                output.packedData = input.packedData;
                output.atlasIndex = 0.0;

                return output;
            }

            #if defined(KERN_TERRAIN_CELLS)
            TEXTURE2D(_TerrainAtlas0);
            TEXTURE2D(_TerrainAtlas1);
            TEXTURE2D(_TerrainAtlas2);
            TEXTURE2D(_TerrainAtlas3);
            TEXTURE2D(_TerrainAtlas4);
            TEXTURE2D(_TerrainAtlas5);
            TEXTURE2D(_TerrainAtlas6);
            TEXTURE2D(_TerrainAtlas7);

            float4 TerrainAtlasTexelSize(int slot)
            {
                switch (slot)
                {
                    case 1: return _TerrainAtlas1_TexelSize;
                    case 2: return _TerrainAtlas2_TexelSize;
                    case 3: return _TerrainAtlas3_TexelSize;
                    case 4: return _TerrainAtlas4_TexelSize;
                    case 5: return _TerrainAtlas5_TexelSize;
                    case 6: return _TerrainAtlas6_TexelSize;
                    case 7: return _TerrainAtlas7_TexelSize;
                    default: return _TerrainAtlas0_TexelSize;
                }
            }

            half4 TerrainSampleAtlas(int slot, SamplerState atlasSampler, float2 uv)
            {
                switch (slot)
                {
                    case 1: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas1, atlasSampler, uv, 0);
                    case 2: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas2, atlasSampler, uv, 0);
                    case 3: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas3, atlasSampler, uv, 0);
                    case 4: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas4, atlasSampler, uv, 0);
                    case 5: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas5, atlasSampler, uv, 0);
                    case 6: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas6, atlasSampler, uv, 0);
                    case 7: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas7, atlasSampler, uv, 0);
                    default: return SAMPLE_TEXTURE2D_LOD(_TerrainAtlas0, atlasSampler, uv, 0);
                }
            }
            #endif

            half4 frag (Varyings input) : SV_Target
            {
                if (_WorldLightDebugView != 0)
                {
                    return half4(
                        GetWorldLightColor(input.worldPosition.xy),
                        1.0);
                }

                // TBDR: no discard anywhere in this shader — transparent output instead.
                // discard kills Hidden Surface Removal on Apple GPUs; alpha-0 blending is visually identical.
                if (input.worldPos.w > 1.5) return half4(0.0, 0.0, 0.0, 0.0);
                if (input.subAtlasRect.z < 0.0001)
                {
                    if (input.color.a < 0.05)
                    {
                        return half4(0.0, 0.0, 0.0, 0.0);
                    }

                    float3 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    float3 diagnosticTexture = SampleMissingTexture(input.worldPos.xy);
                    return half4(diagnosticTexture * worldLight, input.color.a);
                }
                if (input.color.a < 0.05) return half4(0.0, 0.0, 0.0, 0.0);

                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV.xy;

                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                {
                    if (input.color.a < 0.05) return half4(0.0, 0.0, 0.0, 0.0);
                    float3 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    return half4(0.0, 0.0, 0.0, input.color.a * worldLight.r);
                }

                float frameCount = input.tileSizeUV.z;
                float frameHeightTiles = input.tileSizeUV.w;
                float animOffsetUV = 0;

                if (frameCount > 1.5)
                {
                    float speed = input.animData.y;
                    float frameIndex = floor(fmod(_Time.y * speed, frameCount));
                    animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
                }

                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                bool isTiling = fmod(input.worldPos.w, 2.0) > 0.5;
                float2 wrapped = KernResolveTerrainTileIndex(
                    input.worldPos.xy,
                    tilesCount,
                    input.worldPos.z,
                    isTiling ? 1.0 : 0.0);
                float2 tileOffsetUV = wrapped * tileSizeUV;
                float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                float2 quadUV = input.uv;
                int animTypeEarly = (int)(input.animData.x + 0.5);
                bool isScrollAnimated = animTypeEarly == 4;

                if (input.packedData.x > 0.5)
                {
                    float2 anchoredUV = input.packedData.yz;
                    float2 stepUV = float2(0.0, 0.0);
                    stepUV.x = anchoredUV.x > 1.0 ? 1.0 : (anchoredUV.x < 0.0 ? -1.0 : 0.0);
                    stepUV.y = anchoredUV.y > 1.0 ? -1.0 : (anchoredUV.y < 0.0 ? 1.0 : 0.0);
                    bool outsideX = stepUV.x != 0.0;
                    bool outsideY = stepUV.y != 0.0;
                    if (isScrollAnimated)
                    {
                        quadUV.x = outsideX ? frac(anchoredUV.x) : anchoredUV.x;
                        quadUV.y = anchoredUV.y;
                    }
                    else
                    {
                        quadUV = (outsideX || outsideY) ? frac(anchoredUV) : anchoredUV;
                    }

                    if (outsideX || outsideY)
                    {
                        float2 stepPos = input.worldPos.xy + stepUV;
                        float2 wrappedStep = KernResolveTerrainTileIndex(
                            stepPos,
                            tilesCount,
                            input.worldPos.z,
                            isTiling ? 1.0 : 0.0);

                        if (isScrollAnimated)
                        {
                            wrappedStep.y = wrapped.y;
                        }

                        tileOffsetUV = wrappedStep * tileSizeUV;
                        availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                    }
                }

                quadUV.x = clamp(quadUV.x, EPS, 1.0 - EPS);
                if (!isScrollAnimated || input.packedData.x <= 0.5)
                {
                    quadUV.y = clamp(quadUV.y, EPS, 1.0 - EPS);
                }

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
                finalUV.y += animOffsetUV;

                if (isScrollAnimated)
                {
                    float speed = input.animData.y;
                    float scrollUV = fmod(_Time.y * speed * tileSizeUV.y * 0.05, subAtlasSizeUV.y);
                    finalUV.y = baseUV.y + fmod(finalUV.y - baseUV.y + scrollUV + subAtlasSizeUV.y, subAtlasSizeUV.y);
                }

            #if defined(KERN_TERRAIN_CELLS)
                int atlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainAtlasTexelSize(atlasSlot);
            #else
                float4 atlasTexelSize = _BaseMap_TexelSize;
            #endif
                float2 minTileUV = baseUV + tileOffsetUV + atlasTexelSize.xy * 0.5;
                float2 maxTileUV = baseUV + tileOffsetUV + availableTileSize - atlasTexelSize.xy * 0.5;

                // Сглаживание границ текселя выполняется до зажима в тайл.
                finalUV = PixelArtSampleUV(finalUV, atlasTexelSize.zw);

                if (!isScrollAnimated)
                {
                    finalUV = clamp(finalUV, minTileUV, maxTileUV);
                }
                else
                {
                    finalUV.x = clamp(finalUV.x, minTileUV.x, maxTileUV.x);
                }

                // Выборка линейная, и это не возврат к размытию: координата
                // уже загнана так, что внутри текселя линейная выборка даёт
                // ровно его цвет, а смешивание остаётся только в полосе
                // шириной в пиксель на самой границе.
                //
                // Зажим по тайлу стоит после сглаживания и попадает в центры
                // крайних текселей: там веса соседей нулевые, поэтому
                // соседняя клетка атласа не подтекает даже линейной выборкой.
                // Сэмплер выбирается режимом: без сглаживания выборка
                // обязана остаться точечной, иначе выключенный режим всё
                // равно размывал бы картинку линейным фильтром.
            #if defined(KERN_TERRAIN_CELLS)
                half4 texColor = _PixelArtFiltering < 0.5
                    ? TerrainSampleAtlas(atlasSlot, sampler_PointClamp, finalUV)
                    : TerrainSampleAtlas(atlasSlot, sampler_LinearClamp, finalUV);
            #else
                half4 texColor = _PixelArtFiltering < 0.5
                    ? SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointClamp, finalUV, 0)
                    : SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_LinearClamp, finalUV, 0);
            #endif

                if (texColor.a < 0.05)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float3 finalRGB = texColor.rgb;
                float2 artMinUV = baseUV + tileOffsetUV;
                float2 artMaxUV = artMinUV + availableTileSize;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                // Relief connectivity remains mesh metadata, but it must not
                // paint synthetic triangular shadows over the source texture.
                // All terrain darkening comes from the world light texture.

                // Карта потока нужна ТОЛЬКО мерцанию.
                //
                // До выноса анимации в общую функцию эта выборка стояла внутри
                // ветки мерцания, а после — на общем пути, то есть на каждом
                // пикселе террейна и почти всегда впустую. Клетки одного тайла
                // делят тип анимации, так что ветвление здесь когерентное и
                // волна честно пропускает выборку.
                float3 flowSample = 0.0;
                if (animType == 2)
                {
                    flowSample = SampleFlowMap(input.worldPos.xy + input.uv);
                }

                finalRGB = AnimateTerrainColor(
                    finalRGB,
                    texColor.rgb,
                    animType,
                    speed,
                    offset,
                    flowSample,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);

                float finalAlpha = 1.0;
                float4 glowFlags = input.glowData;
                int iflags = int(round(glowFlags.z));
                bool isRoundable = (iflags & 2) != 0;
                if (isRoundable)
                {
                    int sameMask = int(glowFlags.y) & 15; // bits 0-3 of packedLightingFlags = solidBoundaryMask
                    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
                    bool4 hasSame = bits >= 0.5;
                    float2 p = input.uv - 0.5;
                    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
                    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
                    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
                    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
                    float dist = length(p);
                    float aa = min(fwidth(dist), 1.0 / 16.0);
                    float alpha = 1.0 - smoothstep(0.51 - aa, 0.51 + aa, dist);
                    if (rTL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rTR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    float cornerDist = abs(abs(p.x) - abs(p.y));
                    float cornerExclude = smoothstep(0.4, 0.5, cornerDist);
                    alpha = lerp(alpha, 1.0, cornerExclude);
                    finalAlpha *= alpha;
                }

                float3 lightColor = GetWorldLightColor(input.worldPosition.xy);
                float3 litRGB = finalRGB * lightColor;

                // Тень получает только фон. Блоки все одной высоты и друг на
                // друга не падают, а бит 64 — как раз физическая масса
                // переднего плана. Заливка под блоком AO не получает: её видно
                // сквозь полупрозрачные текстуры, и затемнение превращало её в
                // чёрный.
                #ifdef KERN_WORLD_LIGHTING
                uint shadowFlags = (uint)floor(input.glowData.y + 0.0001);
                if ((shadowFlags & 64u) == 0u && input.isForeground > 0.5 && _WorldLightPerBlock == 0)
                {
                    litRGB *= 1.0 - GetAmbientOcclusion(input.worldPosition.xy);
                }
                #endif
                if (finalAlpha < 0.99 && finalAlpha > 0.01)
                {
                    litRGB /= max(finalAlpha, 0.15);
                }

                return half4(litRGB, finalAlpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "KernLightingMaterialField" }

            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MaterialFieldVert
            #pragma fragment MaterialFieldFrag
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainColorAnimation.hlsl"
            #include "Assets/Shaders/TerrainCellData.hlsl"

            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // Тот же UnityPerMaterial, что и в пассе Universal2D, слово в слово.
            //
            // SRP Batcher требует, чтобы КАЖДЫЙ пасс шейдера объявлял этот
            // блок и объявлял его одинаково. Пасс без блока делает несовместимым
            // весь шейдер целиком, а не только себя, — и батчер выключался на
            // террейне даже после того, как `_BaseMap_TexelSize` переехал
            // внутрь. Здесь ни одно из этих свойств не читается; блок стоит
            // ради совпадения раскладки, и убирать его как «мёртвый» нельзя.
            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _FlowScale;
                float _ShimmerSpeedScale;
                float _PulseSpeedScale;
                float4 _DebugColor;
                float _DebugMode;
                float4 _BaseMap_TexelSize;
                float _TerrainAtlasIndex;
                float4 _TerrainAtlas0_TexelSize;
                float4 _TerrainAtlas1_TexelSize;
                float4 _TerrainAtlas2_TexelSize;
                float4 _TerrainAtlas3_TexelSize;
                float4 _TerrainAtlas4_TexelSize;
                float4 _TerrainAtlas5_TexelSize;
                float4 _TerrainAtlas6_TexelSize;
                float4 _TerrainAtlas7_TexelSize;
            CBUFFER_END

            struct MaterialFieldAttributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float4 glowAttr     : TEXCOORD6;
            };

            struct MaterialFieldVaryings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 worldPos     : TEXCOORD1;
                float4 animData     : TEXCOORD2;
                float4 packedData   : TEXCOORD3;
                float4 glowData     : TEXCOORD4;
                nointerpolation float isForeground : TEXCOORD5;
            };

            struct MaterialFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            MaterialFieldVaryings MaterialFieldVert(MaterialFieldAttributes input)
            {
                MaterialFieldVaryings output;
            #if defined(KERN_TERRAIN_CELLS)
                // Поле рисуется одним материалом по всем квадам: атлас здесь
                // не читается, отбрасываются только незаполненные квады.
                TerrainCellVertex cell = LoadTerrainCellVertex(input.positionOS.xyz, input.uv);
                output.positionCS = cell.atlasIndex >= 0.0
                    ? TransformObjectToHClip(cell.positionOS)
                    : TerrainCulledPosition();
                output.uv = cell.uv;
                output.color = cell.color;
                output.worldPos = cell.worldPos;
                output.animData = cell.animData;
                output.packedData = cell.packedData;
                output.glowData = cell.glowData;
                output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;
                return output;
            #endif
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.packedData = input.packedData;
                output.glowData = input.glowAttr;
                output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;
                return output;
            }

            float PhysicalContour(
                float2 uv,
                int solidBoundaryMask,
                int solidDiagonalMask)
            {
                bool top = (solidBoundaryMask & 1) != 0;
                bool left = (solidBoundaryMask & 2) != 0;
                bool bottom = (solidBoundaryMask & 4) != 0;
                bool right = (solidBoundaryMask & 8) != 0;
                float2 p = uv - 0.5;
                float antialias = min(fwidth(length(p)), 1.0 / 16.0);
                float contour = 1.0 - smoothstep(0.5 - antialias, 0.5 + antialias, length(p));
                contour = (top || left) && p.x <= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = (top || right) && p.x >= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = (bottom || left) && p.x <= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                contour = (bottom || right) && p.x >= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                bool diagTL = (solidDiagonalMask & 1) != 0;
                bool diagTR = (solidDiagonalMask & 2) != 0;
                bool diagBL = (solidDiagonalMask & 4) != 0;
                bool diagBR = (solidDiagonalMask & 8) != 0;
                contour = diagTL && p.x <= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = diagTR && p.x >= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = diagBL && p.x <= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                contour = diagBR && p.x >= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                return contour;
            }

            MaterialFieldOutput MaterialFieldFrag(MaterialFieldVaryings input)
            {
                MaterialFieldOutput output;
                float isForeground = input.isForeground;
                uint packedColor = (uint)round(input.glowData.x);
                float3 surfaceAlbedo = float3(
                    packedColor & 255u,
                    (packedColor >> 8) & 255u,
                    (packedColor >> 16) & 255u) / 255.0;
                uint lightingFlags = (uint)floor(input.glowData.y + 0.0001);
                int solidBoundaryMask = int(lightingFlags & 15u);
                int solidDiagonalMask = (int(round(input.glowData.z)) >> 2) & 15;
                float emissionStrength = (lightingFlags & 16u) != 0u
                    ? saturate(frac(input.glowData.y) * 4.0)
                    : 0.0;
                bool hasRoundedPhysicalContour = (lightingFlags & 32u) != 0u;
                bool isPhysicalMass = (lightingFlags & 64u) != 0u;
                // Occupancy — физическая масса переднего плана. isPhysicalMass уже
                // гарантирует !isBackground (фон никогда не получает флаг 64),
                // поэтому isForeground здесь избыточен и только добавлял хрупкую
                // зависимость от точности positionOS.z.
                float occupancy = isPhysicalMass ? 1.0 : 0.0;
                float2 contourUV = input.packedData.x > 0.5 ? input.packedData.yz : input.uv;
                occupancy *= hasRoundedPhysicalContour
                    ? PhysicalContour(
                        contourUV,
                        solidBoundaryMask,
                        solidDiagonalMask)
                    : 1.0;
                // Альбедо анимируется ровно так же, как видимый цвет.
                //
                // Без этого поле материалов отдавало решателю постоянный цвет
                // при мигающей и переливающейся картинке: мигающая лава светила
                // ровно, радужный блок красил отскок одним оттенком. Маска
                // яркости для мерцания берётся от самого альбедо — текстуры в
                // этом пассе нет, и средний цвет клетки тут лучшее, что есть.
                // Как и в видимом пассе: поток читается только мерцанием.
                int albedoAnimationType = (int)(input.animData.x + 0.5);
                float3 flowSample = 0.0;
                if (albedoAnimationType == 2)
                {
                    flowSample = SAMPLE_TEXTURE2D(
                        _FlowMap,
                        sampler_FlowMap,
                        (input.worldPos.xy + input.uv) / _FlowScale.xy).rgb;
                }

                surfaceAlbedo = AnimateTerrainColor(
                    surfaceAlbedo,
                    surfaceAlbedo,
                    albedoAnimationType,
                    input.animData.y,
                    input.animData.z,
                    flowSample,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);

                float surface = step(0.05, input.color.a) * isForeground;
                output.material = half4(surfaceAlbedo * surface, occupancy);
                output.emission = half4(
                    surfaceAlbedo * emissionStrength * surface,
                    emissionStrength * surface);
                return output;
            }
            ENDHLSL
        }
    }
}
