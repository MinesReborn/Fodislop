#ifndef KERN_TERRAIN_FORWARD_PASS_INCLUDED
#define KERN_TERRAIN_FORWARD_PASS_INCLUDED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Terrain/Animation/TerrainColorAnimation.hlsl"
            #include "Assets/Shaders/Terrain/Autotiling/TerrainTileAddressing.hlsl"
            #define KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW
            #include "Assets/Shaders/Terrain/Lighting/TerrainLightingData.hlsl"
            #include "Assets/Shaders/Sampling/PixelArt/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/World/Lighting/Sampling/WorldLightSampling.hlsl"
            #include "Assets/Shaders/Terrain/Diagnostics/TerrainDebugView.hlsl"
            #include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/Terrain/Textures/TerrainSampling.hlsl"
            #include "Assets/Shaders/Terrain/Geometry/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/Cells/Assembly/TerrainCellData.hlsl"
            #include "Assets/Shaders/Terrain/Occlusion/TerrainAmbientOcclusion.hlsl"
            #include "Assets/Shaders/Terrain/Decals/TerrainDecals.hlsl"

            #define EPS 0.0001
            // Explicit benchmark-only cumulative fragment checkpoints; zero is production.
            int _KernTerrainBenchmarkStage;
            float4 _TerrainVolumeViewSize;

            TEXTURE2D(_PrismaticFlowMap);
            SAMPLER(sampler_PrismaticFlowMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);
            #include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
            #define KERN_TERRAIN_RETAIN_BACKGROUND (_TerrainDepthPixels >= 0.5)
            #include "Assets/Shaders/Terrain/Rendering/Passes/Common/TerrainPassCommon.hlsl"
            #include "Assets/Shaders/Terrain/Animation/TerrainAnimationSampling.hlsl"

            #include "Assets/Shaders/Terrain/Geometry/TerrainDepth.hlsl"

            // Both presentation and physical fields require POSITION only.
            // Side endpoints are encoded exactly in presentation addresses.
            struct TerrainVolumeVertexInput
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                nointerpolation float4 worldPos : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 lightContourDecal : TEXCOORD7;
                nointerpolation float atlasIndex : TEXCOORD8;
                nointerpolation float isForeground : TEXCOORD9;
                nointerpolation float4 geometryCornersX : TEXCOORD10;
                nointerpolation float4 geometryCornersY : TEXCOORD11;
                nointerpolation float uvBits : TEXCOORD12;
                // Мировая клетка квада — целые числа, точные в float32.
                nointerpolation float2 cellWorldOrigin : TEXCOORD13;
                nointerpolation float isDepth : TEXCOORD14;
                float2 materialCellSample : TEXCOORD15;
                nointerpolation float reliefGroup : TEXCOORD16;
            };

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


            Varyings vert (TerrainVolumeVertexInput input)
            {
                Varyings output = (Varyings)0;
                bool isDepth = input.positionOS.z > 2.5;
                float4 volumeEdge = 0.0;
                if (isDepth)
                {
                    input.positionOS = TerrainVolumeDecodeAddress(input.positionOS, volumeEdge);
                }
                if (isDepth && (_TerrainDepthPixels < 0.5 ||
                    KernTerrainDebugActive() || _WorldLightDebugView != 0))
                {
                    output.positionCS = TerrainCulledPosition();
                    return output;
                }
                if (isDepth && (input.positionOS.x < _TerrainCellViewOffset.x ||
                    input.positionOS.y < _TerrainCellViewOffset.y ||
                    input.positionOS.x >= _TerrainCellViewOffset.x + _TerrainVolumeViewSize.x ||
                    input.positionOS.y >= _TerrainCellViewOffset.y + _TerrainVolumeViewSize.y))
                {
                    output.positionCS = TerrainCulledPosition();
                    return output;
                }
                input.positionOS.z = isDepth ? 2.0 : input.positionOS.z;
                TERRAIN_RESOLVE_CELL_VERTEX(input, output)
                output.reliefGroup = (float)cell.reliefGroup;
                output.isDepth = isDepth ? 1.0 : 0.0;
                output.worldPosition = TransformObjectToWorld(cell.positionOS);
                output.cellWorldOrigin = TransformObjectToWorld(float3(
                    cell.positionOS.xy - (cell.packedData.yz * _TerrainCellGridSize.z),
                    0.0)).xy;
                uint flags = KernTerrainLightingFlags(cell.lightContourDecal.y);
                bool raised = cell.layer > 0.5 && _TerrainDepthPixels >= 0.5 &&
                    (flags & KERN_TERRAIN_PHYSICAL_MASS_FLAG) != 0u &&
                    !KernTerrainDebugActive() && _WorldLightDebugView == 0;
                output.materialCellSample = cell.packedData.yz;
                float2 geometryPoint = cell.packedData.yz;
                float heightFraction = raised ? 1.0 : 0.0;
                float2 cameraCellPosition = (_WorldSpaceCameraPos.xy - output.cellWorldOrigin) / _TerrainCellGridSize.z;
                if (isDepth)
                {
                    if (!raised)
                    {
                        output.positionCS = TerrainCulledPosition();
                        return output;
                    }
                    float2 normal = TerrainVolumeOutwardNormal(volumeEdge.xy, volumeEdge.zw);
                    float2 edgePoint = lerp(volumeEdge.xy, volumeEdge.zw,
                        TerrainCornerBase(input.positionOS.w).x);
                    if (dot(cameraCellPosition - edgePoint, normal) <= 0.0)
                    {
                        output.positionCS = TerrainCulledPosition();
                        return output;
                    }
                    heightFraction = TerrainCornerBase(input.positionOS.w).y;
                    geometryPoint = edgePoint;
                    // Real wall UVs: tangent along the edge, height into the
                    // block's material patch. Illumination stays at its base.
                    output.packedData.yz = TerrainVolumeLightingPoint(edgePoint, normal);
                    output.materialCellSample = TerrainVolumeMaterialPoint(edgePoint, normal,
                        heightFraction, _TerrainDepthPixels);
                }
                output.uv = TerrainResolveGeometryTileUV(output.uv, output.materialCellSample,
                    output.geometryCornersX, output.geometryCornersY, output.uvBits, 1.0);
                float2 projectedPoint = TerrainVolumeProjectPoint(geometryPoint, cameraCellPosition,
                    heightFraction, _TerrainDepthPixels);
                float3 rasterWorldPosition = KernWorldGridVertex(float3(
                    output.cellWorldOrigin + projectedPoint * _TerrainCellGridSize.z,
                    output.worldPosition.z));
                if (output.atlasIndex >= 0.0)
                {
                    output.positionCS = KernWorldGridClipPosition(rasterWorldPosition);
                    // Preserve virtual-eye homogeneous W so wall material UVs
                    // interpolate perspectively between base and raised plane.
                    output.positionCS *= (KERN_TERRAIN_PRESENTATION_EYE_HEIGHT -
                        heightFraction * min(round(_TerrainDepthPixels), 16.0) / 32.0) /
                        KERN_TERRAIN_PRESENTATION_EYE_HEIGHT;
                }
                output.worldPosition = rasterWorldPosition;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // До любых ветвлений и clip: см. TerrainPixelArtWidthTexels.
                float2 carrierPixelWidth = fwidth(input.isDepth > 0.5 ? input.materialCellSample : input.packedData.yz);
                [branch]
                if (_KernTerrainBenchmarkStage == 1)
                {
                    return half4(0.5, 0.5, 0.5, 1.0);
                }

                float wallDarkening = input.isDepth > 0.5 ? 1.0 - _TerrainDepthDarkening : 1.0;
                float2 textureCellSample = input.materialCellSample;
                uint presentationFlags = KernTerrainLightingFlags(input.lightContourDecal.y);
                bool raisedSurface = input.isForeground > 0.5 &&
                    (presentationFlags & KERN_TERRAIN_PHYSICAL_MASS_FLAG) != 0u &&
                    !KernTerrainDebugActive() && _WorldLightDebugView == 0;

                // Мировая позиция фрагмента нужна только свету и AO. Интерполянт
                // около y≈40000 держит 1/256 клетки (1/8 арт-пикселя): у границы
                // текселя AO (32 на клетку, точечный) до 17% строк пикселей брали
                // соседний тексель, и какие именно — менялось с зумом и сдвигом
                // камеры. Точка выборки собирается из целой клетки и центра
                // арт-пикселя внутри неё — оба числа float32 хранит точно.
                // Текстура и силуэт считаются по packedData, как прежде.
                input.worldPosition.xy = input.cellWorldOrigin +
                    (QuantizeTerrainPixelCenter(input.packedData.yz) * _TerrainCellGridSize.z);
                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.lightContourDecal);
                int cellAnimationType = (int)(input.animData.x + 0.5);
                // Geometry belongs to the foreground layer.  Keep the
                // background quad rectangular so it can fill the area exposed
                // by a displaced foreground silhouette.
                float applyGeometry = input.isForeground;
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    1.0,
                    applyGeometry);
                if (raisedSurface)
                {
                    cellCoverage = input.isDepth > 0.5 ? 1.0 : TerrainDepthFaceCoverage(surface);
                }

                if (!KernTerrainDebugActive() &&
                    _WorldLightDebugView != 0 && _WorldLightDebugView != 9)
                {
                    if (input.isForeground > 0.5)
                    {
                        clip(cellCoverage - 0.5);
                    }
                    return half4(
                        GetWorldLightColor(input.worldPosition.xy).rgb,
                        1.0);
                }

                if (!KernTerrainDebugActive() && _WorldLightDebugView == 9)
                {
                    if (input.isForeground > 0.5)
                    {
                        clip(cellCoverage - 0.5);
                    }
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
                    if (!KernTerrainDebugNeedsSurfaceSample())
                    {
                        if (_TerrainDebugView == KERN_TERRAIN_DEBUG_BACKGROUND_TILE_IDENTITY ||
                            _TerrainDebugView == KERN_TERRAIN_DEBUG_FOREGROUND_TILE_IDENTITY)
                        {
                            if (_TerrainDebugView == KERN_TERRAIN_DEBUG_BACKGROUND_TILE_IDENTITY)
                            {
                                clip(0.5 - input.isForeground);
                            }
                            else
                            {
                                clip(input.isForeground - 0.5);
                                clip(cellCoverage - 0.5);
                            }

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
                            if (input.isForeground < 0.5 && debugTile.isValid &&
                                KernTerrainStaticBackgroundAddressMismatch(
                                    input.worldPos, input.subAtlasRect, input.tileSizeUV,
                                    debugTile.identityTileOffsetUV))
                            {
                                return half4(1.0, 0.0, 0.0, 1.0);
                            }
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

                        if (input.isForeground > 0.5 &&
                            _TerrainDebugView != KERN_TERRAIN_DEBUG_COVERAGE)
                        {
                            clip(cellCoverage - 0.5);
                        }
                        float debugOcclusion = 1.0;
                        #ifdef KERN_WORLD_LIGHTING
                        debugOcclusion = KernTerrainAmbientOcclusionMultiplier(
                            input.lightContourDecal.y,
                            input.worldPosition.xy,
                            _WorldLightRect);
                        #endif
                        float debugForeground = input.isForeground;
                        return half4(
                            KernTerrainDebugColor(
                                surface,
                                cellCoverage,
                                debugForeground,
                                input.worldPos.z,
                                input.reliefGroup,
                                debugOcclusion),
                            1.0);
                    }
                }

                clip(cellCoverage - 0.5);
                [branch]
                if (_KernTerrainBenchmarkStage == 2)
                {
                    return half4(0.5, 0.5, 0.5, cellCoverage);
                }

                if (input.subAtlasRect.z < 0.0001)
                {
                    if (KernTerrainDebugNeedsSurfaceSample())
                    {
                        return half4(1.0, 0.0, 0.8, 1.0);
                    }

                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    float3 diagnosticTexture = SampleMissingTexture(input.worldPos.xy);
                    return half4(
                        diagnosticTexture * worldLight.rgb * wallDarkening,
                        cellCoverage);
                }

                int atlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(atlasSlot);

                float2 terrainTileUv = TerrainResolveGeometryTileUV(
                    input.uv,
                    textureCellSample,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.uvBits,
                    input.packedData.x);
                TerrainTileUvResult tileUV = ResolveTerrainTileUV(
                    terrainTileUv,
                    textureCellSample,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    _Time.y,
                    atlasTexelSize.xy);

                if (!tileUV.isValid)
                {
                    if (KernTerrainDebugNeedsSurfaceSample())
                    {
                        return half4(1.0, 0.0, 0.8, 1.0);
                    }

                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    return half4(0.0, 0.0, 0.0, cellCoverage * worldLight.r);
                }

                float3 flowSample = TerrainResolveFlowSample(
                    cellAnimationType, input.worldPos, input.packedData, _FlowScale);

                half4 texColor = SampleTerrainSurfaceTexel(
                    atlasSlot, tileUV, atlasTexelSize, input.tileSizeUV, carrierPixelWidth);
                if (texColor.a < _AlphaCutoff)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                [branch]
                if (_KernTerrainBenchmarkStage == 3)
                {
                    return half4(texColor.rgb, cellCoverage);
                }

                // Decals are part of the sampled surface albedo. Both the
                // visible and material-field passes compose them before any
                // presentation animation or illumination.
                float3 decalRGB = ApplyTerrainDecal(
                    texColor.rgb,
                    textureCellSample,
                    input.lightContourDecal.x,
                    input.lightContourDecal.w);
                float3 animatedRGB = AnimateTerrainColor(
                    decalRGB,
                    texColor.rgb,
                    terrainTileUv,
                    TerrainAnimationWorldPosition(input.worldPos, input.packedData),
                    cellAnimationType,
                    input.animData.z,
                    input.animData.y,
                    flowSample,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _BlinkingSpeedScale);
                // The bevel is a surface-lighting term: apply after animated
                // color and decals, before incoming world illumination.
                float rimBevel = TerrainRimBevel(surface);
                float3 finalRGB = animatedRGB * rimBevel;
                [branch]
                if (_KernTerrainBenchmarkStage == 4)
                {
                    return half4(finalRGB, cellCoverage);
                }
                if (KernTerrainDebugNeedsSurfaceSample())
                {
                    float glintSignal = 0.0;
                    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_FACETED_GLINT)
                    {
                        float glintStrength = EvaluateFacetedGlintStrength(
                            terrainTileUv,
                            texColor.rgb,
                            cellAnimationType,
                            KERN_TERRAIN_FACETED_ANIMATION_SPEED,
                            input.animData.z);
                        glintSignal = glintStrength / max(_FacetedGlintStrength, 0.0001);
                    }

                    return half4(
                        KernTerrainDebugSurfaceColor(
                            texColor.rgb,
                            flowSample,
                            animatedRGB,
                            decalRGB,
                            glintSignal,
                            cellAnimationType,
                            _TerrainDebugDeltaContrast),
                        1.0);
                }
                // The contour is only one part of coverage. Atlas filtering
                // also produces fractional coverage along authored silhouettes
                // (boulders, sand). Do not turn those dark edge texels opaque.
                float finalAlpha = cellCoverage * texColor.a;

                float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                float3 litRGB = finalRGB * worldLight.rgb;
                #ifdef KERN_WORLD_LIGHTING
                litRGB *= KernTerrainAmbientOcclusionMultiplier(
                    input.lightContourDecal.y,
                    input.worldPosition.xy,
                    _WorldLightRect);
                #endif
                if (cellCoverage < 0.99 && cellCoverage > 0.01)
                {
                    litRGB /= max(cellCoverage, _PremultiplyAlphaFloor);
                }

                // Keep texture coverage in SrcAlpha blending; compensating
                // RGB by texColor.a would cancel the authored transparency.
                uint emissionFlags = KernTerrainLightingFlags(input.lightContourDecal.y);
                float emissionStrength = KernTerrainGlow(input.lightContourDecal.y, emissionFlags);
                [branch]
                if (emissionStrength > 0.0)
                {
                    float emissionMask = TerrainSourceEmission(float4(texColor.rgb, 1.0)).a;
                    litRGB += animatedRGB * emissionMask * emissionStrength * _SurfaceEmissionStrength;
                }
                return half4(litRGB * wallDarkening, finalAlpha);
            }

#endif // KERN_TERRAIN_FORWARD_PASS_INCLUDED
