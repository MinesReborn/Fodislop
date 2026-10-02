#nullable enable

namespace Kern.World.Lighting;

using System;
using Kern.Core;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

internal static class LightingComputeBinder
{
    // All lighting kernels use [numthreads(8, 8, 1)]. Keep the GPU execution
    // detail here so a cell-space policy cannot be mistaken for a dispatch
    // threshold.
    public const int ThreadGroupSize = 8;
    internal static bool DiagnosticTransportCounters { get; set; }
    // Explicit differential-test reference; never selected by frame cost.
    internal static bool DiagnosticTexelTraversalReference { get; set; }
    internal static bool DiagnosticVectorPolarReference { get; set; }

    // Equal RGB extinction has one exact optical depth. Radiance/source color
    // remains RGB HDR; only this redundant transport quantity is scalar.
    private static bool HasNeutralExtinction =>
        LightingConfigHolder.EmptyExtinctionRGB.r == LightingConfigHolder.EmptyExtinctionRGB.g &&
        LightingConfigHolder.EmptyExtinctionRGB.r == LightingConfigHolder.EmptyExtinctionRGB.b &&
        LightingConfigHolder.SolidExtinctionRGB.r == LightingConfigHolder.SolidExtinctionRGB.g &&
        LightingConfigHolder.SolidExtinctionRGB.r == LightingConfigHolder.SolidExtinctionRGB.b;

    public static bool UsesScalarPolarExtinction => !DiagnosticVectorPolarReference && HasNeutralExtinction;
    public static bool UsesScalarDynamicRadiance => UsesScalarPolarExtinction;

    public static int DispatchGroups(int extent)
    {
        return (extent + ThreadGroupSize - 1) / ThreadGroupSize;
    }

    public static readonly int MaterialFieldId = Shader.PropertyToID("_MaterialField");
    public static readonly int EmissionFieldId = Shader.PropertyToID("_EmissionField");
    public static readonly int RadianceAtlasId = Shader.PropertyToID("_RadianceAtlas");
    public static readonly int RadianceAtlasInputId = Shader.PropertyToID("_RadianceAtlasInput");
    public static readonly int RadianceAtlasOutputId = Shader.PropertyToID("_RadianceAtlasOutput");
    public static readonly int DirectTextureId = Shader.PropertyToID("_DirectTexture");
    public static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
    public static readonly int StaticDirectInputId = Shader.PropertyToID("_StaticDirectInput");
    public static readonly int ResultId = Shader.PropertyToID("_Result");
    public static readonly int FieldSizeId = Shader.PropertyToID("_FieldSize");
    public static readonly int LightSizeId = Shader.PropertyToID("_LightSize");
    public static readonly int FieldTexelsPerLightTexelId = Shader.PropertyToID("_FieldTexelsPerLightTexel");
    public static readonly int CompositeDispatchOriginId = Shader.PropertyToID("_CompositeDispatchOrigin");
    public static readonly int CompositeDispatchSizeId = Shader.PropertyToID("_CompositeDispatchSize");
    public static readonly int WorldRectId = Shader.PropertyToID("_WorldRect");
    public static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
    public static readonly int EmptyExtinctionRGBId = Shader.PropertyToID("_EmptyExtinctionRGB");
    public static readonly int SolidExtinctionRGBId = Shader.PropertyToID("_SolidExtinctionRGB");
    public static readonly int EmissionScaleId = Shader.PropertyToID("_EmissionScale");
    public static readonly int MaximumLightMultiplierId = Shader.PropertyToID("_MaximumLightMultiplier");
    public static readonly int SurfaceReflectionReachId =
        Shader.PropertyToID("_SurfaceReflectionReachCells");
    public static readonly int DynamicNearCellsId = Shader.PropertyToID("_DynamicNearCells");
    public static readonly int DynamicAngularSampleCountId =
        Shader.PropertyToID("_DynamicAngularSampleCount");
    public static readonly int DynamicEmitterPointsPerAxisId =
        Shader.PropertyToID("_DynamicEmitterPointsPerAxis");
    public static readonly int SolidOccupancyThresholdId =
        Shader.PropertyToID("_SolidOccupancyThreshold");
    public static readonly int TransportSolidThresholdId =
        Shader.PropertyToID("_TransportSolidThreshold");
    public static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    public static readonly int TransmittanceDebugDistanceCellsId = Shader.PropertyToID("_TransmittanceDebugDistanceCells");
    public static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
    public static readonly int MaterialYFlipId = Shader.PropertyToID("_MaterialYFlip");
    public static readonly int EnableBilinearFixId = Shader.PropertyToID("_EnableBilinearFix");
    public static readonly int CascadeOffsetId = Shader.PropertyToID("_CascadeOffset");
    public static readonly int CascadeProbeSizeId = Shader.PropertyToID("_CascadeProbeSize");
    public static readonly int CascadeProbeSpacingId = Shader.PropertyToID("_CascadeProbeSpacing");
    public static readonly int CascadeDirectionCountId = Shader.PropertyToID("_CascadeDirectionCount");
    public static readonly int CascadeIntervalId = Shader.PropertyToID("_CascadeInterval");
    public static readonly int FarCascadeOffsetId = Shader.PropertyToID("_FarCascadeOffset");
    public static readonly int FarCascadeProbeSizeId = Shader.PropertyToID("_FarCascadeProbeSize");
    public static readonly int FarCascadeProbeSpacingId = Shader.PropertyToID("_FarCascadeProbeSpacing");
    public static readonly int FarCascadeDirectionCountId = Shader.PropertyToID("_FarCascadeDirectionCount");
    public static readonly int FarCascadeIntervalId = Shader.PropertyToID("_FarCascadeInterval");
    public static readonly int HasFarCascadeId = Shader.PropertyToID("_HasFarCascade");
    public static readonly int CascadeEntryCountId = Shader.PropertyToID("_CascadeEntryCount");
    public static readonly int CascadeDispatchRowWidthId = Shader.PropertyToID("_CascadeDispatchRowWidth");
    public static readonly int CascadeDispatchOriginId = Shader.PropertyToID("_CascadeDispatchOrigin");
    public static readonly int CascadeDispatchSizeId = Shader.PropertyToID("_CascadeDispatchSize");
    public static readonly int ScrollCascadeOffsetId = Shader.PropertyToID("_ScrollCascadeOffset");
    public static readonly int ScrollCascadeEntryCountId = Shader.PropertyToID("_ScrollCascadeEntryCount");
    public static readonly int ScrollProbeSizeId = Shader.PropertyToID("_ScrollProbeSize");
    public static readonly int ScrollDirectionCountId = Shader.PropertyToID("_ScrollDirectionCount");
    public static readonly int ScrollDeltaProbesId = Shader.PropertyToID("_ScrollDeltaProbes");
    public static readonly int DirtyRegionsId = Shader.PropertyToID("_DirtyRegions");
    public static readonly int DirtyRegionCountId = Shader.PropertyToID("_DirtyRegionCount");
    public static readonly int CascadeChangedMaskId = Shader.PropertyToID("_CascadeChangedMask");
    public static readonly int CascadeChangedMaskCountId = Shader.PropertyToID("_CascadeChangedMaskCount");
    public static readonly int CascadeMaskEnabledId = Shader.PropertyToID("_CascadeMaskEnabled");
    public static readonly int CascadeReanchorEnabledId = Shader.PropertyToID("_CascadeReanchorEnabled");
    public static readonly int CascadePhaseMatchesId = Shader.PropertyToID("_CascadePhaseMatches");
    public static readonly int ReanchorDeltaTexelsId = Shader.PropertyToID("_ReanchorDeltaTexels");
    public static readonly int ReanchorFarDeltaProbesId = Shader.PropertyToID("_ReanchorFarDeltaProbes");
    public static readonly int ReanchorFarPhaseMatchesId = Shader.PropertyToID("_ReanchorFarPhaseMatches");
    public static readonly int DynamicLightsId = Shader.PropertyToID("_DynamicLights");
    public static readonly int DynamicDispatchOriginId = Shader.PropertyToID("_DynamicDispatchOrigin");
    public static readonly int DynamicDispatchSizeId = Shader.PropertyToID("_DynamicDispatchSize");
    public static readonly int DynamicLightIndexId = Shader.PropertyToID("_DynamicLightIndex");
    public static readonly int WriteDynamicDirectId = Shader.PropertyToID("_WriteDynamicDirect");
    public static readonly int DynamicTileOffsetId = Shader.PropertyToID("_DynamicTileOffset");
    public static readonly int DynamicTilesId = Shader.PropertyToID("_DynamicTiles");
    public static readonly int DynamicTilesInputId = Shader.PropertyToID("_DynamicTilesInput");
    public static readonly int DynamicTileInfosId = Shader.PropertyToID("_DynamicTileInfos");
    public static readonly int DynamicTileCountId = Shader.PropertyToID("_DynamicTileCount");
    public static readonly int ComposeOriginId = Shader.PropertyToID("_ComposeOrigin");
    public static readonly int ComposeSizeId = Shader.PropertyToID("_ComposeSize");
    public static readonly int DynamicPolarId = Shader.PropertyToID("_DynamicPolar");
    public static readonly int DynamicPolarLayerOffsetId = Shader.PropertyToID("_DynamicPolarLayerOffset");
    public static readonly int DynamicReachIndexId = Shader.PropertyToID("_DynamicReachIndex");
    public static readonly int DynamicHorizonId = Shader.PropertyToID("_DynamicHorizon");
    public static readonly int DynamicHorizonInputId = Shader.PropertyToID("_DynamicHorizonInput");
    public static readonly int CleanCellRowsOutputId = Shader.PropertyToID("_CleanCellRowsOutput");
    public static readonly int CleanCellRowsId = Shader.PropertyToID("_CleanCellRows");
    public static readonly int CleanCellPrefixOutputId = Shader.PropertyToID("_CleanCellPrefixOutput");
    public static readonly int CleanCellPrefixId = Shader.PropertyToID("_CleanCellPrefix");
    public static readonly int DynamicHorizonBaseId = Shader.PropertyToID("_DynamicHorizonBase");
    public static readonly int DynamicHorizonStrideId = Shader.PropertyToID("_DynamicHorizonStride");
    public static readonly int DynamicPolarInputId = Shader.PropertyToID("_DynamicPolarInput");
    public static readonly int DynamicPolarSizeId = Shader.PropertyToID("_DynamicPolarSize");
    public static readonly int DynamicPolarTextureSizeId = Shader.PropertyToID("_DynamicPolarTextureSize");
    public static readonly int DynamicPolarScalarExtinctionId = Shader.PropertyToID("_DynamicPolarScalarExtinction");
    public static readonly int NeutralExtinctionId = Shader.PropertyToID("_NeutralExtinction");
    public static readonly int DynamicTilesScalarRadianceId = Shader.PropertyToID("_DynamicTilesScalarRadiance");

    // Квадрат плотности эмиттера: столько вееров лучей на фонарь, по полосе
    // строк каждый в текстуре полярных лучей.
    public static int DynamicEmitterPointCount =>
        LightingQualityTuningController.DynamicEmitterPointsPerAxis *
        LightingQualityTuningController.DynamicEmitterPointsPerAxis;

    // Absolute scene radiance (1.0 = paper white) below which one dynamic
    // light's contribution cannot move any display level. Derived, never tuned:
    // half the first code above black of the active output (8-bit sRGB in SDR,
    // 10-bit PQ at the calibrated paper white in HDR), divided by the exposure
    // gain, by the Neutral tonemap's toe slope and by the number of sources
    // (rounded up to a power of two) whose tails may meet in one pixel. Below
    // half the smallest float16 subnormal the radiance textures store zero, so
    // tracing further cannot change the frame. Bound to `_InvisibleDynamicRadiance`.
    public static float InvisibleDynamicRadiance { get; private set; } = ResolveInvisibleDynamicRadiance(1);

    public static readonly int InvisibleDynamicRadianceId = Shader.PropertyToID("_InvisibleDynamicRadiance");

    // d/dx of URP's Neutral curve at black with its white scale applied
    // (b * (c * f - e) / (d * f^2) * whiteScale^2 = 1.063): near black the
    // display moves slightly faster than the scene.
    private const float NeutralToeSlope = 1.07f;

    // Half of 2^-24, the smallest positive float16.
    private const float HalfFloatRoundsToZero = 2.9802322e-8f;

    // Returns true when the bound changed: rays, horizons and source culling
    // that used the old bound must be redone.
    public static bool UpdateInvisibleDynamicRadiance(int sourceCount)
    {
        float bound = ResolveInvisibleDynamicRadiance(sourceCount);
        if (bound.Equals(InvisibleDynamicRadiance))
        {
            return false;
        }

        InvisibleDynamicRadiance = bound;
        return true;
    }

    private static float ResolveInvisibleDynamicRadiance(int sourceCount)
    {
        float exposureGain = Mathf.Pow(2f, Kern.Rendering.PostProcessing.PostProcessLook.Exposure.Stops);
        float perSource = DisplayOutputPrecision.HalfStepAtBlack /
            (exposureGain * NeutralToeSlope * Mathf.NextPowerOfTwo(Mathf.Max(1, sourceCount)));
        return Mathf.Max(perSource, HalfFloatRoundsToZero);
    }
    public static readonly int CellGridSizeId = Shader.PropertyToID("_CellGridSize");
    public static readonly int CellSolidMaskId = Shader.PropertyToID("_CellSolidMask");
    public static readonly int CellSolidMaskOutputId = Shader.PropertyToID("_CellSolidMaskOutput");
    public static readonly int SurfaceAirCacheId = Shader.PropertyToID("_SurfaceAirCache");
    public static readonly int SurfaceAirCacheOutputId = Shader.PropertyToID("_SurfaceAirCacheOutput");
    public static readonly int LightingCountersId = Shader.PropertyToID("_LightingCounters");
    public static readonly int LightingCountersEnabledId = Shader.PropertyToID("_LightingCountersEnabled");

    public static void BindLightingCounters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        ComputeBuffer counters)
    {
        commandBuffer.SetComputeBufferParam(compute, kernel, LightingCountersId, counters);
    }

    public static float ResolveTransmittanceDebugDistance()
    {
        // Fixed physical distance: changing sigma must change the measured
        // transmission, not silently change the distance in the opposite direction.
        return 1f;
    }

    public static void BindExtinction(CommandBuffer commandBuffer, ComputeShader compute)
    {
        commandBuffer.SetComputeIntParam(compute, DynamicPolarScalarExtinctionId,
            UsesScalarPolarExtinction ? 1 : 0);
        commandBuffer.SetComputeIntParam(compute, DynamicTilesScalarRadianceId,
            UsesScalarDynamicRadiance ? 1 : 0);
        // Mathematical specialization and polar storage have independent
        // references: a format comparison must run identical attenuation math.
        commandBuffer.SetComputeIntParam(compute, NeutralExtinctionId,
            HasNeutralExtinction && !DiagnosticTexelTraversalReference ? 1 : 0);
        commandBuffer.SetComputeVectorParam(
            compute,
            EmptyExtinctionRGBId,
            LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier);
        commandBuffer.SetComputeVectorParam(
            compute,
            SolidExtinctionRGBId,
            LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier);
    }

    // Weakest extinction of any medium and RGB channel, per cell. Every path
    // is attenuated at least this much per cell of length.
    public static float ResolveMinimumExtinction()
    {
        Color empty = LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;
        Color solid = LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;
        return Mathf.Max(0f, Mathf.Min(
            Mathf.Min(empty.r, Mathf.Min(empty.g, empty.b)),
            Mathf.Min(solid.r, Mathf.Min(solid.g, solid.b))));
    }

    public static void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel,
        RenderTexture materialField,
        RenderTexture emissionField,
        ComputeBuffer? lightingCounters = null)
    {
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            MaterialFieldId,
            materialField);
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            EmissionFieldId,
            emissionField);
        if (lightingCounters != null)
        {
            BindLightingCounters(commandBuffer, compute, kernel, lightingCounters);
        }
    }

    public static void BindSharedParameters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int fieldWidth,
        int fieldHeight,
        int lightWidth,
        int lightHeight,
        Vector4 worldRect,
        float cellSize,
        LightingEngine.DebugView debugView,
        RenderTexture materialField,
        RenderTexture emissionField,
        int solveCascadeKernel,
        int resolveDirectKernel,
        int compositeLightingKernel,
        int cellGridWidth = 0,
        int cellGridHeight = 0)
    {
        if (lightWidth <= 0 || lightHeight <= 0 ||
            fieldWidth % lightWidth != 0 || fieldHeight % lightHeight != 0 ||
            fieldWidth / lightWidth != fieldHeight / lightHeight)
        {
            throw new InvalidOperationException(
                $"Light lattice {lightWidth}x{lightHeight} must evenly divide field {fieldWidth}x{fieldHeight}.");
        }
        commandBuffer.SetComputeIntParams(compute, FieldSizeId, fieldWidth, fieldHeight);
        commandBuffer.SetComputeIntParams(compute, LightSizeId, lightWidth, lightHeight);
        commandBuffer.SetComputeIntParam(compute, FieldTexelsPerLightTexelId, fieldWidth / lightWidth);
        if (cellGridWidth > 0 && cellGridHeight > 0)
        {
            commandBuffer.SetComputeIntParams(compute, CellGridSizeId, cellGridWidth, cellGridHeight);
        }
        commandBuffer.SetComputeVectorParam(compute, WorldRectId, worldRect);
        commandBuffer.SetComputeVectorParam(
            compute,
            AmbientColorId,
            LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity);
        BindExtinction(commandBuffer, compute);
        commandBuffer.SetComputeFloatParam(
            compute,
            SolidOccupancyThresholdId,
            LightingConfigHolder.SolidOccupancyThreshold);
        commandBuffer.SetComputeFloatParam(
            compute,
            TransportSolidThresholdId,
            LightingConfigHolder.TransportSolidThreshold);
        commandBuffer.SetComputeFloatParam(compute, EmissionScaleId, LightingConfigHolder.EmissionScale);
        commandBuffer.SetComputeFloatParam(compute, MaximumLightMultiplierId, LightingConfigHolder.MaximumLightMultiplier);
        commandBuffer.SetComputeFloatParam(
            compute,
            SurfaceReflectionReachId,
            LightingConfigHolder.SurfaceReflectionReachCells);
        commandBuffer.SetComputeFloatParam(
            compute,
            DynamicNearCellsId,
            LightingQualityTuningController.DynamicNearCells);
        commandBuffer.SetComputeIntParam(
            compute,
            DynamicAngularSampleCountId,
            LightingQualityTuningController.DynamicAngularSampleCount);
        commandBuffer.SetComputeIntParam(
            compute,
            DynamicEmitterPointsPerAxisId,
            LightingQualityTuningController.DynamicEmitterPointsPerAxis);
        commandBuffer.SetComputeIntParam(compute, LightingCountersEnabledId, 0);
        commandBuffer.SetComputeIntParam(compute, "_UniformCellTraversalEnabled",
            DiagnosticTexelTraversalReference ? 0 : 1);
        commandBuffer.SetComputeFloatParam(compute, CellSizeId, cellSize);
        commandBuffer.SetComputeFloatParam(
            compute,
            TransmittanceDebugDistanceCellsId,
            ResolveTransmittanceDebugDistance());
        commandBuffer.SetComputeIntParam(compute, DebugViewId, (int)debugView);
        commandBuffer.SetComputeIntParam(
            compute,
            MaterialYFlipId,
            LightingFieldOrientation.RowsTopDown ? 1 : 0);
        commandBuffer.SetComputeIntParam(
            compute,
            EnableBilinearFixId,
            LightingConfigHolder.EnableBilinearFix ? 1 : 0);

        BindFieldTextures(commandBuffer, compute, solveCascadeKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, resolveDirectKernel, materialField, emissionField);
        BindFieldTextures(commandBuffer, compute, compositeLightingKernel, materialField, emissionField);
    }

    public static int ResolveCascadeScrollDelta(int cellDelta, int fieldSize, int cellGridSize, int probeSpacing)
    {
        if (cellGridSize <= 0 || fieldSize <= 0 || fieldSize % cellGridSize != 0 || probeSpacing <= 0)
        {
            throw new ArgumentException("Atlas scrolling requires an integer texel scale and positive probe spacing.");
        }

        long texelDelta = (long)cellDelta * (fieldSize / cellGridSize);
        if (texelDelta % probeSpacing != 0)
        {
            throw new ArgumentException("Atlas scrolling cannot reuse probes with a different world-space phase.");
        }

        return checked((int)(texelDelta / probeSpacing));
    }

    public static void BindCascadeParameters(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        CascadeLayout cascade,
        CascadeLayout farCascade,
        bool hasFarCascade)
    {
        commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, cascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeProbeSizeId,
            cascade.ProbeWidth,
            cascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeProbeSpacingId,
            cascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeDirectionCountId,
            cascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            CascadeIntervalId,
            new Vector4(cascade.IntervalStart, cascade.IntervalEnd, 0f, 0f));
        commandBuffer.SetComputeIntParam(compute, FarCascadeOffsetId, farCascade.Offset);
        commandBuffer.SetComputeIntParams(
            compute,
            FarCascadeProbeSizeId,
            farCascade.ProbeWidth,
            farCascade.ProbeHeight);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeProbeSpacingId,
            farCascade.ProbeSpacing);
        commandBuffer.SetComputeIntParam(
            compute,
            FarCascadeDirectionCountId,
            farCascade.DirectionCount);
        commandBuffer.SetComputeVectorParam(
            compute,
            FarCascadeIntervalId,
            new Vector4(
                farCascade.IntervalStart,
                farCascade.IntervalEnd,
                0f,
                0f));
        commandBuffer.SetComputeIntParam(compute, HasFarCascadeId, hasFarCascade ? 1 : 0);
    }

    public static void BindCascadeDispatch(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int originX,
        int originY,
        int width,
        int height,
        int directionCount)
    {
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeDispatchOriginId,
            originX,
            originY);
        commandBuffer.SetComputeIntParams(
            compute,
            CascadeDispatchSizeId,
            width,
            height);
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeEntryCountId,
            checked(width * height * directionCount));
        commandBuffer.SetComputeIntParam(
            compute,
            CascadeDispatchRowWidthId,
            checked(width * directionCount));
    }
}
