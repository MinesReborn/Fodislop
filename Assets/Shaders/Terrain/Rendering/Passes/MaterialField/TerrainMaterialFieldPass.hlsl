#ifndef KERN_TERRAIN_MATERIALFIELD_PASS_INCLUDED
#define KERN_TERRAIN_MATERIALFIELD_PASS_INCLUDED
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Sampling/PixelArt/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/Terrain/Autotiling/TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/Terrain/Lighting/TerrainLightingData.hlsl"
            #include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/Terrain/Textures/TerrainSampling.hlsl"
            #include "Assets/Shaders/Terrain/Geometry/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/Cells/Assembly/TerrainCellData.hlsl"
            #include "Assets/Shaders/Terrain/Decals/TerrainDecals.hlsl"

            // Lighting field stores a time-independent albedo snapshot. Visual
            // crystal/shimmer animation belongs to the screen pass; sampling it
            // here would make a region rebuild capture a different random phase.

            #include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/Terrain/Rendering/Passes/Common/TerrainPassCommon.hlsl"
            #include "Assets/Shaders/Terrain/Lighting/TerrainLightingFieldCommon.hlsl"

            struct MaterialFieldOutput
            {
                half4 material : SV_Target0;
                half4 glow : SV_Target1;
            };

            int _KernLightingFieldDiagnosticStage;

            MaterialFieldOutput MaterialFieldFrag(TerrainLightingFieldVaryings input)
            {
                // До любых ветвлений и clip: см. TerrainPixelArtWidthTexels.
                float2 carrierPixelWidth = fwidth(input.packedData.yz);
                float2 cellDx = ddx(input.packedData.yz);
                float2 cellDy = ddy(input.packedData.yz);
                float2 uvDx = ddx(input.uv);
                float2 uvDy = ddy(input.uv);
                MaterialFieldOutput output;
                if (_KernLightingFieldDiagnosticStage == 1)
                {
                    output.material = half4(0.0, 1.0, 0.0, 1.0);
                    output.glow = 0.0;
                    return output;
                }

                // Keep the same geometry and atlas addressing as the screen
                // pass, but use the authored base albedo rather than its
                // time-varying display animation. Static transport must not
                // change merely because this field was rebuilt at a new time.
                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.lightContourDecal);

                int albedoAtlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(albedoAtlasSlot);
                float2 geometryTileUv = TerrainResolveGeometryTileUV(
                    input.uv,
                    input.packedData.yz,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.uvBits,
                    input.packedData.x);
                half4 albedoTexel = SampleTerrainLightingFieldAlbedoTexel(
                    geometryTileUv,
                    input.packedData.yz,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    albedoAtlasSlot,
                    atlasTexelSize,
                    carrierPixelWidth);
                if (_KernLightingFieldDiagnosticStage == 2)
                {
                    output.material = albedoTexel;
                    output.glow = 0.0;
                    return output;
                }

                // Без фолбеков: нет текселя — нет альбедо. Плоский цвет
                // миникарты сюда больше не попадает ни в каком виде.
                float3 surfaceAlbedo = albedoTexel.a >= _AlphaCutoff
                    ? albedoTexel.rgb
                    : 0.0;
                uint lightingFlags = KernTerrainLightingFlags(input.lightContourDecal.y);
                float glow = KernTerrainGlow(
                    input.lightContourDecal.y,
                    lightingFlags);
                bool isPhysicalMass = KernTerrainIsPhysicalMass(lightingFlags);
                // Occupancy — физическая масса переднего плана. isPhysicalMass уже
                // гарантирует !isBackground (фон не получает PhysicalMass),
                // поэтому isForeground здесь избыточен и только добавлял хрупкую
                // зависимость от точности positionOS.z.
                float applyGeometry = input.isForeground;
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    1.0,
                    applyGeometry);
                float occupancy = isPhysicalMass ? cellCoverage : 0.0;
                // The carrier encloses the displaced polygon but is not the
                // material itself. Reject the same absent fragments as the
                // visible pass before decals or glow can color them.
                clip(cellCoverage - 0.5);
                clip(albedoTexel.a - _AlphaCutoff);
                // Material occupancy is the hard physical-mass input for
                // lighting transport. AO filtering is isolated in its own pass.
                occupancy *= albedoTexel.a >= _AlphaCutoff ? 1.0 : 0.0;

                surfaceAlbedo = ApplyTerrainDecal(
                    surfaceAlbedo,
                    input.packedData.yz,
                    input.lightContourDecal.x,
                    input.lightContourDecal.w);
                float4 sourceEmission = 0.0;
                if (glow > 0.0)
                {
                    // Integrate source pixels only for emissive cells and only
                    // when the retained material/glow field is rebuilt. At the
                    // default 2 field pixels/cell this is 16x16 taps per field
                    // texel (1024/cell), rather than losing sub-texel crystals.
                    int2 taps = max(int2(1, 1), (int2)ceil(carrierPixelWidth * 32.0 - 0.0001));
                    [loop]
                    for (int y = 0; y < taps.y; y++)
                    {
                        [loop]
                        for (int x = 0; x < taps.x; x++)
                        {
                            float2 offset = (float2(x, y) + 0.5) / float2(taps) - 0.5;
                            float2 samplePosition = input.packedData.yz + cellDx * offset.x + cellDy * offset.y;
                            float2 sampleUv = input.uv + uvDx * offset.x + uvDy * offset.y;
                            // World sheets consume samplePosition directly;
                            // do not invert the displaced polygon for an unused UV.
                            if (((uint)round(input.worldPos.z) & KERN_TERRAIN_COLUMN_WORLD_TEXTURE_ANCHOR) == 0u)
                            {
                                sampleUv = TerrainResolveGeometryTileUV(
                                    sampleUv, samplePosition, input.geometryCornersX, input.geometryCornersY,
                                    input.uvBits, input.packedData.x);
                            }
                            TerrainTileUvResult emissionTile = ResolveTerrainTileUV(
                                sampleUv, samplePosition, input.subAtlasRect, input.tileSizeUV,
                                input.worldPos, input.animData, input.packedData, 0.0, atlasTexelSize.xy);
                            if (emissionTile.isValid)
                            {
                                float4 sampleTexel = TerrainSampleAtlas(albedoAtlasSlot, sampler_PointClamp,
                                    ClampTerrainTileUV(emissionTile.finalUV, emissionTile));
                                sourceEmission += TerrainSourceEmission(sampleTexel);
                            }
                        }
                    }
                    sourceEmission /= (float)(taps.x * taps.y);
                }
                surfaceAlbedo *= TerrainRimBevel(surface);

                // Фон вносит альбедо и свечение, как любой видимый слой;
                // массы у него нет (isPhysicalMass — только передний план).
                output.material = half4(surfaceAlbedo, occupancy);
                output.glow = sourceEmission * glow * cellCoverage;
                return output;
            }

#endif // KERN_TERRAIN_MATERIALFIELD_PASS_INCLUDED
