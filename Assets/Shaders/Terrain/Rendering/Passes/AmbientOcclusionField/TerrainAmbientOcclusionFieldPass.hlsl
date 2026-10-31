#ifndef KERN_TERRAIN_AMBIENTOCCLUSIONFIELD_PASS_INCLUDED
#define KERN_TERRAIN_AMBIENTOCCLUSIONFIELD_PASS_INCLUDED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Sampling/PixelArt/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/Terrain/Autotiling/TerrainTileAddressing.hlsl"
            #define KERN_TERRAIN_AO_FIELD
            #include "Assets/Shaders/Terrain/Geometry/TerrainContour.hlsl"
            #include "Assets/Shaders/Terrain/Cells/Assembly/TerrainCellData.hlsl"
            #include "Assets/Shaders/Terrain/Lighting/TerrainLightingData.hlsl"
            #include "Assets/Shaders/Terrain/Textures/TerrainSampling.hlsl"

            #include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/Terrain/Rendering/Passes/Common/TerrainPassCommon.hlsl"
            #include "Assets/Shaders/Terrain/Lighting/TerrainLightingFieldCommon.hlsl"
            float _TerrainAmbientOcclusionDistance;

            half4 AmbientOcclusionFieldFrag(TerrainLightingFieldVaryings input) : SV_Target
            {
                // Evaluate coverage and falloff at the native 1/32-cell sample.
                // No resampling/quantization pass follows this field calculation.
                input.packedData.yz = QuantizeTerrainPixelCenter(input.packedData.yz);
                uint lightingFlags = KernTerrainLightingFlags(input.lightContourDecal.y);
                if (input.isForeground < 0.5 || !KernTerrainIsPhysicalMass(lightingFlags))
                {
                    clip(-1.0);
                }

                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.lightContourDecal);

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
                    if (TerrainGeometryIsDeepInterior(
                        surface.cellSample,
                        surface.cornersX,
                        surface.cornersY))
                    {
                        // Contact is maximal throughout the proven interior;
                        // avoid projecting onto every polygon segment there.
                        geometryDistance = 1.0;
                        closestGeometryPosition = surface.cellSample;
                    }
                    else
                    {
                        geometryDistance = TerrainGeometrySignedDistance(
                            surface.cellSample,
                            surface.cornersX,
                            surface.cornersY,
                            closestGeometryPosition);
                    }
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
                // combines neighbouring masses without directional probes.
                float exteriorDistance = max(-signedDistance, 0.0);
                if (exteriorDistance >= _TerrainAmbientOcclusionDistance)
                {
                    clip(-1.0);
                }

                float contact = 1.0 - smoothstep(
                    0.0, _TerrainAmbientOcclusionDistance, exteriorDistance);
                return half4(contact, 0.0, 0.0, 0.0);
            }

#endif // KERN_TERRAIN_AMBIENTOCCLUSIONFIELD_PASS_INCLUDED
