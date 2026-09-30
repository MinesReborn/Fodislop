#nullable enable

using System;
using System.IO;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Lighting.Quality;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Lighting.Diagnostics;

public static class LightingFrameDumper
{
    private const int MaximumRetainedDumps = 3;

    [Serializable]
    public sealed class ConfigDump
    {
        public string architecture = "governed-lighting-v3";
        public int streamingQuantumCells;
        public int maximumCascadeDirections;
        public long maximumStaticCascadeRayWorkUnits;
        public int fieldWidth;
        public int fieldHeight;
        public int ambientOcclusionWidth;
        public int ambientOcclusionHeight;
        public int cellGridWidth;
        public int cellGridHeight;
        public Vector4 worldRect;
        public float cellSize;
        public Color ambientColor;
        public float ambientIntensity;
        public float emissionScale;
        public Color emptyExtinctionRGB;
        public Color solidExtinctionRGB;
        public string enabledFeatures = "";
        public string qualityMode = "";
        public CascadeInfo[] cascades = Array.Empty<CascadeInfo>();
    }

    [Serializable]
    public sealed class CascadeInfo
    {
        public int index;
        public int offset;
        public int entryCount;
        public int probeWidth;
        public int probeHeight;
        public int probeSpacing;
        public int directionCount;
        public float intervalStart;
        public float intervalEnd;
    }

    [Serializable]
    public sealed class CountersDump
    {
        public float buildCommandsTimeMs;
        public float executeCommandsTimeMs;
        public float cascadeTraceTimeMs;
        public float cascadeMergeTimeMs;
        public float dynamicLightingTimeMs;
        public float compositeTimeMs;
        public int ddaSegments;
        public long ddaTexelVisits;
        public int cascadeMergeSamples;
        public int activeDynamicLights;
        public int dynamicTraceCount;
        public long dynamicDispatchPixels;
        public long dynamicComposePixels;
        public long compositeDispatchPixels;
        public long polarRayWorkUnits;
        public long estimatedCascadeRayWorkUnits;
        public long estimatedCascadeDispatchThreads;
        public int atlasScrollCount;
        public long atlasReusedEntries;
        public long atlasClearedEntries;
        public long cascadePartialEntries;
        public long cascadePartialEntriesFrame;
        public long cascadeFullEntries;
        public long cascadeFullEntriesFrame;
        public int staticSolveCount;
        public int staticSolveFrameCount;
        public int staticDependencyMaskSolveCount;
        public int staticDenseFallbackCount;
        public int dynamicSolveCount;
        public int terrainRebuildCount;
        public int terrainFullPopulateCount;
        public int terrainChunkLoadCount;
        public int terrainDirtyPatchCount;
        public int lightingRegionInvalidationCount;
        public int lightingRegionInvalidationFrameCount;
        public int lightingRegionChangeCount;
        public int lightingGeometryChangeCount;
        public int lightingFieldRebuildCount;
        public float terrainMeshTimeMs;
        public float terrainCacheTimeMs;
        public float terrainFloodFillTimeMs;
        public float terrainGpuUploadTimeMs;
        public float terrainAtlasUploadTimeMs;
        public TerrainTextureUploadDump? terrainTextureUpload;
        public int streamingPlanKind;
        public int streamingWindowOriginX;
        public int streamingWindowOriginY;
        public int streamingWindowWidth;
        public int streamingWindowHeight;
        public int streamingDeltaX;
        public int streamingDeltaY;
    }

    [Serializable]
    public sealed class TerrainTextureUploadDump
    {
        public bool available;
        public long generation;
        public long applyCalls;
        public long applyPayloadBytes;
        public long copyTextureCalls;
        public long copyTexturePayloadBytes;
        public bool hasSourceFrame;
        public int sourceFrameId;
        public int observationFrameId;
        public bool frameDeltaValid;
        public long applyCallsFrameDelta;
        public long applyPayloadBytesFrameDelta;
        public long copyTextureCallsFrameDelta;
        public long copyTexturePayloadBytesFrameDelta;
        public int frameDeltaStartObservationFrameId;
        public int frameDeltaEndObservationFrameId;
    }

    public static string DumpCurrentFrame(
        LightingResources resources,
        Vector4 worldRect,
        float cellSize,
        LightingQualityMode qualityMode,
        IFrameTelemetry telemetry,
        ComputeBuffer? lightingCounters = null,
        string? targetDirectory = null,
        bool includeTextures = true)
    {
        // Дамп — несколько json и png, тяжёлый: хранятся только последние.
        string dir = targetDirectory ?? DiagnosticArtifactPaths.CreateDirectory(
            "Lighting",
            "lighting_frame",
            MaximumRetainedDumps);
        Directory.CreateDirectory(dir);

        // 1. Config Dump
        var cascades = resources.Cascade.Layouts;
        var cascadeInfos = new CascadeInfo[cascades.Count];
        for (int i = 0; i < cascades.Count; i++)
        {
            cascadeInfos[i] = new CascadeInfo
            {
                index = i,
                offset = cascades[i].Offset,
                entryCount = cascades[i].EntryCount,
                probeWidth = cascades[i].ProbeWidth,
                probeHeight = cascades[i].ProbeHeight,
                probeSpacing = cascades[i].ProbeSpacing,
                directionCount = cascades[i].DirectionCount,
                intervalStart = cascades[i].IntervalStart,
                intervalEnd = cascades[i].IntervalEnd,
            };
        }

        var config = new ConfigDump
        {
            streamingQuantumCells = StreamingPolicy.Default.AllocationQuantumCells,
            maximumCascadeDirections = CascadeLayoutBuilder.DefaultMaximumCascadeDirections,
            maximumStaticCascadeRayWorkUnits = LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits,
            fieldWidth = resources.FieldWidth,
            fieldHeight = resources.FieldHeight,
            ambientOcclusionWidth = resources.Geometry.AmbientOcclusionWidth,
            ambientOcclusionHeight = resources.Geometry.AmbientOcclusionHeight,
            cellGridWidth = resources.Geometry.CellGridWidth,
            cellGridHeight = resources.Geometry.CellGridHeight,
            worldRect = worldRect,
            cellSize = cellSize,
            ambientColor = LightingConfigHolder.AmbientColor,
            ambientIntensity = LightingConfigHolder.AmbientIntensity,
            emissionScale = LightingConfigHolder.EmissionScale,
            emptyExtinctionRGB = LightingConfigHolder.EmptyExtinctionRGB,
            solidExtinctionRGB = LightingConfigHolder.SolidExtinctionRGB,
            enabledFeatures = LightingConfigHolder.EnabledFeatures.ToString(),
            qualityMode = qualityMode.ToString(),
            cascades = cascadeInfos,
        };
        File.WriteAllText(Path.Combine(dir, "config.json"), JsonUtility.ToJson(config, true));

        // 2. Counters Dump
        var counters = new CountersDump
        {
            buildCommandsTimeMs = telemetry.LightingBuildCommandsTimeMs,
            executeCommandsTimeMs = telemetry.LightingExecuteCommandsTimeMs,
            cascadeTraceTimeMs = telemetry.LightingCascadeTraceTimeMs,
            cascadeMergeTimeMs = telemetry.LightingCascadeMergeTimeMs,
            dynamicLightingTimeMs = telemetry.LightingDynamicLightingTimeMs,
            compositeTimeMs = telemetry.LightingCompositeTimeMs,
            ddaSegments = telemetry.LightingDdaSegments,
            ddaTexelVisits = telemetry.LightingDdaTexelVisits,
            cascadeMergeSamples = telemetry.LightingCascadeMergeSamples,
            activeDynamicLights = telemetry.ActiveDynamicLights,
            dynamicTraceCount = telemetry.LightingDynamicTraceCount,
            dynamicDispatchPixels = telemetry.LightingDynamicDispatchPixels,
            dynamicComposePixels = telemetry.LightingDynamicComposePixels,
            compositeDispatchPixels = telemetry.LightingCompositeDispatchPixels,
            polarRayWorkUnits = telemetry.LightingPolarRayWorkUnits,
            estimatedCascadeRayWorkUnits = telemetry.LightingEstimatedCascadeRayWorkUnits > 0
                ? telemetry.LightingEstimatedCascadeRayWorkUnits
                : CascadeCostCalculator.EstimateRayWorkUnits(resources.Cascade.Layouts),
            estimatedCascadeDispatchThreads = telemetry.LightingEstimatedCascadeDispatchThreads > 0
                ? telemetry.LightingEstimatedCascadeDispatchThreads
                : EstimateCascadeDispatchThreads(resources),
            atlasScrollCount = telemetry.LightingAtlasScrollCount,
            atlasReusedEntries = telemetry.LightingAtlasReusedEntries,
            atlasClearedEntries = telemetry.LightingAtlasClearedEntries,
            cascadePartialEntries = telemetry.LightingCascadePartialEntries,
            cascadePartialEntriesFrame = telemetry.LightingCascadePartialEntriesFrame,
            cascadeFullEntries = telemetry.LightingCascadeFullEntries,
            cascadeFullEntriesFrame = telemetry.LightingCascadeFullEntriesFrame,
            staticSolveCount = telemetry.LightingStaticSolveCount,
            staticSolveFrameCount = telemetry.LightingStaticSolveFrameCount,
            staticDependencyMaskSolveCount = telemetry.LightingStaticDependencyMaskSolveCount,
            staticDenseFallbackCount = telemetry.LightingStaticDenseFallbackCount,
            dynamicSolveCount = telemetry.LightingDynamicSolveCount,
            terrainRebuildCount = telemetry.TerrainRebuildCount,
            terrainFullPopulateCount = telemetry.TerrainFullPopulateCount,
            terrainChunkLoadCount = telemetry.TerrainChunkLoadCount,
            terrainDirtyPatchCount = telemetry.TerrainDirtyPatchCount,
            lightingRegionInvalidationCount = telemetry.LightingRegionInvalidationCount,
            lightingRegionInvalidationFrameCount = telemetry.LightingRegionInvalidationFrameCount,
            lightingRegionChangeCount = telemetry.LightingRegionChangeCount,
            lightingGeometryChangeCount = telemetry.LightingGeometryChangeCount,
            lightingFieldRebuildCount = telemetry.LightingFieldRebuildCount,
            terrainMeshTimeMs = telemetry.TerrainMeshTimeMs,
            terrainCacheTimeMs = telemetry.TerrainCacheTimeMs,
            terrainFloodFillTimeMs = telemetry.TerrainFloodFillTimeMs,
            terrainGpuUploadTimeMs = telemetry.TerrainGpuUploadTimeMs,
            terrainAtlasUploadTimeMs = telemetry.TerrainAtlasUploadTimeMs,
            terrainTextureUpload = CreateTerrainTextureUploadDump(telemetry),
            streamingPlanKind = telemetry.StreamingPlanKind,
            streamingWindowOriginX = telemetry.StreamingWindowOriginX,
            streamingWindowOriginY = telemetry.StreamingWindowOriginY,
            streamingWindowWidth = telemetry.StreamingWindowWidth,
            streamingWindowHeight = telemetry.StreamingWindowHeight,
            streamingDeltaX = telemetry.StreamingDeltaX,
            streamingDeltaY = telemetry.StreamingDeltaY,
        };
        if (includeTextures)
        {
            TryReadGpuCounters(lightingCounters, counters);
        }
        File.WriteAllText(Path.Combine(dir, "counters.json"), JsonUtility.ToJson(counters, true));

        if (!includeTextures)
        {
            return dir;
        }

        // 3. Textures Dump
        SaveRenderTexture(resources.Geometry.Material, Path.Combine(dir, "MaterialField.png"));
        SaveRenderTexture(resources.Geometry.StaticEmission, Path.Combine(dir, "StaticEmissionField.png"));
        SaveRenderTexture(resources.Geometry.CellSolidMask, Path.Combine(dir, "CellSolidMask.png"));
        SaveRenderTexture(resources.Geometry.AmbientOcclusion, Path.Combine(dir, "AmbientOcclusionField.png"));
        SaveRenderTexture(resources.Direct.Static, Path.Combine(dir, "StaticDirect.png"));
        SaveRenderTexture(resources.Direct.Dynamic, Path.Combine(dir, "DynamicDirect.png"));
        SaveRenderTexture(resources.Output.Lightmap, Path.Combine(dir, "FinalLightmap.png"));

        DiagnosticReport.Announce("Дамп кадра света", dir);
        return dir;
    }

    private static TerrainTextureUploadDump? CreateTerrainTextureUploadDump(IFrameTelemetry telemetry)
    {
        if (telemetry is not FrameTelemetry frameTelemetry ||
            frameTelemetry.TerrainTextureUploadSnapshot is not { } snapshot)
        {
            return null;
        }

        var dump = new TerrainTextureUploadDump
        {
            available = snapshot.IsAvailable,
            generation = snapshot.Generation,
            applyCalls = snapshot.IsAvailable ? snapshot.ApplyCalls : 0L,
            applyPayloadBytes = snapshot.IsAvailable ? snapshot.ApplyPayloadBytes : 0L,
            copyTextureCalls = snapshot.IsAvailable ? snapshot.CopyTextureCalls : 0L,
            copyTexturePayloadBytes = snapshot.IsAvailable ? snapshot.CopyTexturePayloadBytes : 0L,
            hasSourceFrame = snapshot.HasSourceFrame,
            sourceFrameId = snapshot.SourceFrameId,
            observationFrameId = snapshot.ObservationFrameId,
        };
        if (frameTelemetry.TerrainTextureUploadFrameDelta is { } delta)
        {
            dump.frameDeltaValid = true;
            dump.applyCallsFrameDelta = delta.ApplyCalls;
            dump.applyPayloadBytesFrameDelta = delta.ApplyPayloadBytes;
            dump.copyTextureCallsFrameDelta = delta.CopyTextureCalls;
            dump.copyTexturePayloadBytesFrameDelta = delta.CopyTexturePayloadBytes;
            dump.frameDeltaStartObservationFrameId = delta.StartObservationFrameId;
            dump.frameDeltaEndObservationFrameId = delta.EndObservationFrameId;
        }

        return dump;
    }

    private static long EstimateCascadeDispatchThreads(LightingResources resources)
    {
        long total = 0;
        foreach (CascadeLayout cascade in resources.Cascade.Layouts)
        {
            total = checked(total + cascade.EntryCount);
        }

        return total;
    }

    private static void TryReadGpuCounters(ComputeBuffer? lightingCounters, CountersDump counters)
    {
        if (lightingCounters == null)
        {
            return;
        }

        try
        {
            var values = new uint[3];
            lightingCounters.GetData(values);
            counters.ddaSegments = (int)Math.Min(values[0], int.MaxValue);
            counters.ddaTexelVisits = values[1];
            counters.cascadeMergeSamples = (int)Math.Min(values[2], int.MaxValue);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[LightingFrameDumper] GPU counters unavailable: {exception.Message}");
        }
    }

    private static void SaveRenderTexture(RenderTexture? rt, string filePath)
    {
        if (rt == null || !rt.IsCreated())
        {
            return;
        }

        RenderTexture currentActive = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = RuntimeTextureFactory.CreateRGBAHalfNoMip(
            rt.width,
            rt.height,
            "LightingFrameDump",
            RuntimeTextureColorSpace.Linear,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = currentActive;

        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);
        UnityEngine.Object.Destroy(tex);
    }
}
