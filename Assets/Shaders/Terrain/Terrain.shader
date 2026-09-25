Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        // Runtime materials must inject both textures. Neutral shader values
        // deliberately make a missing injection visible instead of rendering
        // an implicit white/gray world.
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "black" {}
        _PrismaticFlowMap ("X Crystal Phase Vectors", 2D) = "black" {}
        _FlowMap ("Shimmer Flow Map", 2D) = "black" {}
        _TerrainDecalAtlas ("Terrain Decal Atlas", 2D) = "black" {}
        _TerrainDecalStoneAtlas ("Terrain Decal Stone Atlas", 2D) = "black" {}
        _ShimmerColor ("Shimmer Color", Color) = (0,0,0,0)
        _FlowScale ("Flow Scale", Vector) = (0,0,0,0)
        _ShimmerSpeedScale ("Shimmer Speed Scale", Float) = 0
        _PulseSpeedScale ("Pulse Speed Scale", Float) = 0
        _OrganicBendStrength ("Organic Bend Strength", Float) = 1
        _OrganicBendPivot ("Organic Bend Pivot", Float) = 0.35
        _RoundableCornerRadius ("Roundable Corner Radius", Float) = 0.51
        _DebugColor ("Debug Color", Color) = (0,0,0,0)
        [ToggleUI] _DebugMode ("Debug Mode", Float) = 0
        // Авторский вид поверхности: значения приезжают из TerrainConfigHolder
        // свойствами материала, дефолт здесь — те же числа.
        _GroundDecalStrength ("Ground Decal Strength", Float) = 0.35
        _StoneDecalStrength ("Stone Decal Strength", Float) = 0.7
        _DecalPlacementOffset ("Decal Placement Offset", Float) = 0.5
        _ReliefRimDistanceScale ("Relief Rim Distance Scale", Float) = 2
        _ReliefRimFalloff ("Relief Rim Falloff", Float) = 0.5
        _FacetedGlintDirection ("Faceted Glint Direction", Vector) = (0.62,0.38,0,0)
        _FacetedGlintSweepStart ("Faceted Glint Sweep Start", Float) = -0.12
        _FacetedGlintSweepEnd ("Faceted Glint Sweep End", Float) = 1.12
        _FacetedGlintBandStart ("Faceted Glint Band Start", Float) = 0.035
        _FacetedGlintBandEnd ("Faceted Glint Band End", Float) = 0.13
        _FacetedGlintMaskStart ("Faceted Glint Mask Start", Float) = 0.2
        _FacetedGlintMaskEnd ("Faceted Glint Mask End", Float) = 0.75
        _FacetedGlintStrength ("Faceted Glint Strength", Float) = 0.45
        _FacetedGlintMix ("Faceted Glint Mix", Float) = 0.72
        _FacetedGlintRiseEnd ("Faceted Glint Rise End", Float) = 0.04
        _FacetedGlintFallStart ("Faceted Glint Fall Start", Float) = 0.28
        _FacetedGlintFallEnd ("Faceted Glint Fall End", Float) = 0.40
        _FacetedGlintSweepDuration ("Faceted Glint Sweep Duration", Float) = 0.40
        _ShimmerChromaFloor ("Shimmer Chroma Floor", Float) = 0.65
        _MoltenContourAntialiasScale ("Molten Contour Antialias Scale", Float) = 2.5
        _PrismaticPhaseSpeed ("Prismatic Phase Speed", Float) = 0.05
        _MoltenPhaseSpeed ("Molten Phase Speed", Float) = 0.12
        _RainbowHueDivisor ("Rainbow Hue Divisor", Float) = 255
        _MoltenFlowDirectionA ("Molten Flow Direction A", Vector) = (1.9,-1.3,0,0)
        _MoltenFlowDirectionB ("Molten Flow Direction B", Vector) = (-1.1,2.1,0,0)
        _MoltenFlowPhase ("Molten Flow Phase", Float) = 0.7
        _MoltenFlowWeightA ("Molten Flow Weight A", Float) = 0.3
        _MoltenFlowWeightB ("Molten Flow Weight B", Float) = 0.2
        _MoltenFlowWeightC ("Molten Flow Weight C", Float) = 0.5
        _MoltenHeatBase ("Molten Heat Base", Float) = 0.35
        _MoltenHeatScale ("Molten Heat Scale", Float) = 0.8
        _MoltenHotColor ("Molten Hot Color", Color) = (0.6,0.35,0.035,1)
        _MoltenSheetScrollSpeed ("Molten Sheet Scroll Speed", Float) = 0.05
        _PrismaticTintA ("Prismatic Tint A", Color) = (0.2,1,0.2,1)
        _PrismaticTintB ("Prismatic Tint B", Color) = (0.2,0.2,1,1)
        _PrismaticTintC ("Prismatic Tint C", Color) = (1,1,1,1)
        _PrismaticTintD ("Prismatic Tint D", Color) = (0.1,1,1,1)
        _PrismaticTintE ("Prismatic Tint E", Color) = (1,0,0,1)
        _PremultiplyAlphaFloor ("Premultiply Alpha Floor", Float) = 0.15
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.05
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
            #include "Assets/Shaders/Terrain/TerrainColorAnimation.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #define KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW
            #include "Assets/Shaders/Terrain/TerrainCellData.hlsl"
            #include "Assets/Shaders/Terrain/TerrainLightingData.hlsl"
            #include "Assets/Shaders/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/World/WorldLightSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainDebugView.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAmbientOcclusion.hlsl"
            #include "Assets/Shaders/Terrain/TerrainDecals.hlsl"

            #define EPS 0.0001

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PrismaticFlowMap);
            SAMPLER(sampler_PrismaticFlowMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);
            #include "Assets/Shaders/Terrain/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/Terrain/TerrainPassCommon.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAnimationSampling.hlsl"

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
                nointerpolation float4 geometryCornersX : TEXCOORD10;
                nointerpolation float4 geometryCornersY : TEXCOORD11;
                nointerpolation float uvBits : TEXCOORD12;
            };

            half4 SampleAtlasColor(int slot, float2 uv)
            {
            #if defined(KERN_TERRAIN_CELLS)
                [branch]
                if (_PixelArtFiltering < 0.5)
                {
                    return TerrainSampleAtlas(slot, sampler_PointClamp, uv);
                }

                return TerrainSampleAtlas(slot, sampler_LinearClamp, uv);
            #else
                [branch]
                if (_PixelArtFiltering < 0.5)
                {
                    return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointClamp, uv, 0);
                }

                return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_LinearClamp, uv, 0);
            #endif
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


            Varyings vert (TerrainVertexInput input)
            {
                Varyings output = (Varyings)0;
            #if defined(KERN_TERRAIN_CELLS)
                TERRAIN_RESOLVE_CELL_VERTEX(input, output)
                output.worldPosition = TransformObjectToWorld(cell.positionOS);
                return output;
            #else
                TERRAIN_RESOLVE_ATTRIBUTE_VERTEX(input, output)
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            #endif
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (!KernTerrainDebugActive() &&
                    _WorldLightDebugView != 0 && _WorldLightDebugView != 9)
                {
                    return half4(
                        GetWorldLightColor(input.worldPosition.xy).rgb,
                        1.0);
                }

                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.glowData,
                    input.animData.w);
                int animationProfile = surface.animationProfile;
                float applyGeometry = 0.0;
            #if defined(KERN_TERRAIN_CELLS)
                // Geometry belongs to the foreground layer.  Keep the
                // background quad rectangular so it can fill the area exposed
                // by a displaced foreground silhouette.
                applyGeometry = input.isForeground;
            #endif
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    TerrainContourAntialiasScale(animationProfile),
                    applyGeometry);

                if (!KernTerrainDebugActive() && _WorldLightDebugView == 9)
                {
                #if defined(KERN_TERRAIN_CELLS)
                    if (input.isForeground > 0.5)
                    {
                        clip(cellCoverage - 0.5);
                    }
                #endif
                    float occlusion = KernSampleTerrainAmbientOcclusion(
                        input.worldPosition.xy,
                        _WorldLightRect);
                    return half4(occlusion, occlusion, occlusion, 1.0);
                }

                // Foreground diagnostics use the same displaced silhouette as
                // the visible terrain. Coverage inspects the carrier itself
                // so its cutouts remain visible.
                if (KernTerrainDebugActive())
                {
                    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_BACKGROUND_TILE_IDENTITY)
                    {
                        clip(0.5 - input.isForeground);
                        int debugAtlasSlot = (int)round(input.atlasIndex);
                        float4 debugAtlasTexelSize = TerrainMaterialAtlasTexelSize(debugAtlasSlot);
                        float2 geometryTileUv = TerrainResolveGeometryTileUV(
                            input.uv,
                            input.packedData.yz,
                            input.geometryCornersX,
                            input.geometryCornersY,
                            input.uvBits,
                            input.packedData.x);
                        TerrainTileUvResult debugTile = ResolveTerrainTileUV(
                            geometryTileUv,
                            input.packedData.yz,
                            input.subAtlasRect,
                            input.tileSizeUV,
                            input.worldPos,
                            input.animData,
                            input.packedData,
                            _Time.y,
                            debugAtlasTexelSize.xy);
                        return half4(
                            KernTerrainUniqueTileColor(
                                input.atlasIndex,
                                input.subAtlasRect,
                                debugTile.identityTileOffsetUV,
                                debugTile.identityAvailableTileSize,
                                debugAtlasTexelSize,
                                debugTile.isValid),
                            1.0);
                    }

                #if defined(KERN_TERRAIN_CELLS)
                    if (input.isForeground > 0.5 &&
                        _TerrainDebugView != KERN_TERRAIN_DEBUG_COVERAGE)
                    {
                        clip(cellCoverage - 0.5);
                    }
                #endif
                    float debugOcclusion = 1.0;
                    #ifdef KERN_WORLD_LIGHTING
                    debugOcclusion = KernTerrainAmbientOcclusionMultiplier(
                        input.glowData.y,
                        input.worldPosition.xy,
                        _WorldLightRect);
                    #endif
                    float debugForeground = 1.0;
                #if defined(KERN_TERRAIN_CELLS)
                    debugForeground = input.isForeground;
                #endif
                    return half4(
                        KernTerrainDebugColor(
                            surface,
                            cellCoverage,
                            debugForeground,
                            input.worldPos.z,
                            debugOcclusion),
                        1.0);
                }

            #if defined(KERN_TERRAIN_CELLS)
                clip(cellCoverage - 0.5);
            #endif

                if (input.subAtlasRect.z < 0.0001)
                {
                    if (input.color.a < _AlphaCutoff)
                    {
                        return half4(0.0, 0.0, 0.0, 0.0);
                    }

                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    float3 diagnosticTexture = SampleMissingTexture(input.worldPos.xy);
                    return half4(
                        diagnosticTexture * worldLight.rgb,
                        input.color.a * cellCoverage);
                }
                if (input.color.a < _AlphaCutoff) return half4(0.0, 0.0, 0.0, 0.0);

                int atlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(atlasSlot);

                float2 terrainTileUv = TerrainResolveGeometryTileUV(
                    input.uv,
                    input.packedData.yz,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.uvBits,
                    input.packedData.x);
                TerrainTileUvResult tileUV = ResolveTerrainTileUV(
                    terrainTileUv,
                    input.packedData.yz,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    _Time.y,
                    atlasTexelSize.xy);

                if (!tileUV.isValid)
                {
                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    return half4(0.0, 0.0, 0.0, input.color.a * cellCoverage * worldLight.r);
                }

                int animType = (int)(input.animData.x + 0.5);
                float3 flowSample = TerrainResolveFlowSample(
                    animationProfile, animType, input.worldPos, input.packedData, _FlowScale);

                float2 finalUV = tileUV.finalUV;
                finalUV = PixelArtSampleUV(finalUV, atlasTexelSize.zw);
                finalUV = ClampTerrainTileUV(finalUV, tileUV);

                half4 texColor = SampleAtlasColor(atlasSlot, finalUV);
                if (texColor.a < _AlphaCutoff)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                // Relief mask остаётся в cell data для диагностики и
                // downstream lighting, но не затемняет альбедо: это создавало
                // видимую рамку вокруг каждой клетки и плиточные щели.
                float3 finalRGB = texColor.rgb;
                finalRGB = AnimateTerrainColor(
                    finalRGB,
                    texColor.rgb,
                    terrainTileUv,
                    TerrainAnimationWorldPosition(input.worldPos, input.packedData),
                    animType,
                    animationProfile,
                    input.animData.y,
                    input.animData.z,
                    flowSample,
                    input.glowData.x,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);
                finalRGB = ApplyTerrainDecal(
                    finalRGB,
                    terrainTileUv,
                    input.glowData.w);

                float finalAlpha = cellCoverage;

                float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                float3 litRGB = finalRGB * worldLight.rgb;
                #ifdef KERN_WORLD_LIGHTING
                litRGB *= KernTerrainAmbientOcclusionMultiplier(
                    input.glowData.y,
                    input.worldPosition.xy,
                    _WorldLightRect);
                #endif
                if (finalAlpha < 0.99 && finalAlpha > 0.01)
                {
                    litRGB /= max(finalAlpha, _PremultiplyAlphaFloor);
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
            #pragma vertex TerrainLightingFieldVert
            #pragma fragment MaterialFieldFrag
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/Terrain/TerrainColorAnimation.hlsl"
            #include "Assets/Shaders/Terrain/TerrainCellData.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/Terrain/TerrainLightingData.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/TerrainDecals.hlsl"

            TEXTURE2D(_PrismaticFlowMap);
            SAMPLER(sampler_PrismaticFlowMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // Альбедо поля — из атласа тем же UV-конвейером, что видимый пасс.
            TEXTURE2D(_BaseMap);

            #include "Assets/Shaders/Terrain/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/Terrain/TerrainPassCommon.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAnimationSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainLightingFieldCommon.hlsl"

            struct MaterialFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            MaterialFieldOutput MaterialFieldFrag(TerrainLightingFieldVaryings input)
            {
                MaterialFieldOutput output;

                // Тот же разбор вершины, что и в экранном проходе: поле
                // материалов обязано нести ровно то альбедо, которое видно,
                // иначе свет отскакивает от цвета, которого в кадре нет.
                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.glowData,
                    input.animData.w);

                float isForeground = input.isForeground;
                int albedoAtlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(albedoAtlasSlot);
                int albedoAnimationType = (int)(input.animData.x + 0.5);
                int albedoAnimationProfile = surface.animationProfile;
                float2 geometryTileUv = TerrainResolveGeometryTileUV(
                    input.uv,
                    input.packedData.yz,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.uvBits,
                    input.packedData.x);
                float3 flowSample = TerrainResolveFlowSample(
                    albedoAnimationProfile,
                    albedoAnimationType,
                    input.worldPos,
                    input.packedData,
                    _FlowScale);

                half4 albedoTexel = SampleTerrainLightingFieldAlbedoTexel(
                    geometryTileUv,
                    input.packedData.yz,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    albedoAtlasSlot,
                    atlasTexelSize);

                // Без фолбеков: нет текселя — нет альбедо. Плоский цвет
                // миникарты сюда больше не попадает ни в каком виде.
                float3 surfaceAlbedo = albedoTexel.a >= _AlphaCutoff
                    ? albedoTexel.rgb
                    : 0.0;
                uint lightingFlags = KernTerrainLightingFlags(input.glowData.y);
                float emissionStrength = KernTerrainEmissionStrength(
                    input.glowData.y,
                    lightingFlags);
                bool isPhysicalMass = KernTerrainIsPhysicalMass(lightingFlags);
                // Occupancy — физическая масса переднего плана. isPhysicalMass уже
                // гарантирует !isBackground (фон не получает PhysicalMass),
                // поэтому isForeground здесь избыточен и только добавлял хрупкую
                // зависимость от точности positionOS.z.
                float applyGeometry = 0.0;
            #if defined(KERN_TERRAIN_CELLS)
                applyGeometry = input.isForeground;
            #endif
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    TerrainContourAntialiasScale(albedoAnimationProfile),
                    applyGeometry);
                float occupancy = isPhysicalMass ? cellCoverage : 0.0;
                // Material occupancy is the hard physical-solid input for
                // lighting transport. AO filtering is isolated in its own pass.
                occupancy *= albedoTexel.a >= _AlphaCutoff ? 1.0 : 0.0;

                surfaceAlbedo = AnimateTerrainColor(
                    surfaceAlbedo,
                    surfaceAlbedo,
                    geometryTileUv,
                    TerrainAnimationWorldPosition(input.worldPos, input.packedData),
                    albedoAnimationType,
                    albedoAnimationProfile,
                    input.animData.y,
                    input.animData.z,
                    flowSample,
                    input.glowData.x,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);
                surfaceAlbedo = ApplyTerrainDecal(
                    surfaceAlbedo,
                    geometryTileUv,
                    input.glowData.w);

                // Маска присутствия материала в поле: прозрачные и фоновые
                // фрагменты не вносят в поле ни альбедо, ни свечения.
                float materialMask = step(_AlphaCutoff, input.color.a) * isForeground;
                output.material = half4(surfaceAlbedo * materialMask, occupancy);
                output.emission = half4(
                    surfaceAlbedo * emissionStrength * materialMask * cellCoverage,
                    emissionStrength * materialMask * cellCoverage);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "LightingAmbientOcclusionField"
            Tags { "LightMode" = "KernLightingAmbientOcclusionField" }

            Blend One One
            BlendOp Max
            ColorMask A
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLightingFieldVert
            #pragma fragment AmbientOcclusionFieldFrag
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/PixelArtFiltering.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/Terrain/TerrainCellData.hlsl"
            #include "Assets/Shaders/Terrain/TerrainLightingData.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainSampling.hlsl"
            #include "Assets/Shaders/Terrain/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/TerrainAnimationProfile.hlsl"

            TEXTURE2D(_BaseMap);

            #include "Assets/Shaders/Terrain/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/Terrain/TerrainPassCommon.hlsl"
            #define KERN_TERRAIN_AO_FIELD
            #include "Assets/Shaders/Terrain/TerrainLightingFieldCommon.hlsl"
            float _TerrainAmbientOcclusionDistance;

            half4 AmbientOcclusionFieldFrag(TerrainLightingFieldVaryings input) : SV_Target
            {
                uint lightingFlags = KernTerrainLightingFlags(input.glowData.y);
                if (input.isForeground < 0.5 || !KernTerrainIsPhysicalMass(lightingFlags))
                {
                    clip(-1.0);
                }

                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.glowData,
                    input.animData.w);

                float geometryDistance;
                float2 closestGeometryPosition;
                [branch]
                if (surface.anchored < 0.5)
                {
                    geometryDistance = -TerrainSignedDistanceToBox(
                        surface.cellSample,
                        float2(0.5, 0.5),
                        float2(0.5, 0.5));
                    closestGeometryPosition = saturate(surface.cellSample);
                }
                else
                {
                    geometryDistance = TerrainGeometrySignedDistance(
                        surface.cellSample,
                        surface.cornersX,
                        surface.cornersY,
                        surface.packedOrganicEdges,
                        surface.packedOrganicEdges > 0.5,
                        closestGeometryPosition);
                }
                float signedDistance = geometryDistance;
                if (KernTerrainIsRoundable(surface.packedContour))
                {
                    signedDistance = min(signedDistance,
                        TerrainRoundableSignedDistance(
                            surface.contourUV, surface.packedLightingFlags));
                }

                // Distances are in cell units. The field stores contact
                // falloff from the actual displaced polygon, and Max blending
                // combines neighboring solids without directional probes.
                float exteriorDistance = max(-signedDistance, 0.0);
                if (exteriorDistance >= _TerrainAmbientOcclusionDistance)
                {
                    clip(-1.0);
                }

                float2 opacityCellPosition = geometryDistance < 0.0
                    ? closestGeometryPosition
                    : surface.cellSample;
                int atlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(atlasSlot);
                float2 geometryTileUv = TerrainResolveGeometryTileUV(
                    input.uv,
                    opacityCellPosition,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.uvBits,
                    input.packedData.x);
                half4 albedoTexel = SampleTerrainLightingFieldAlbedoTexel(
                    geometryTileUv,
                    opacityCellPosition,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    atlasSlot,
                    atlasTexelSize);
                if (albedoTexel.a < _AlphaCutoff)
                {
                    clip(-1.0);
                }

                float contact = 1.0 - smoothstep(
                    0.0, _TerrainAmbientOcclusionDistance, exteriorDistance);
                return half4(0.0, 0.0, 0.0, contact);
            }
            ENDHLSL
        }
    }
}
