#nullable enable

using System;
using Kern.Core.Diagnostics;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Manages allocation, resizing, double-buffering, and disposal of compute structured buffers
/// used by static and dynamic lighting solvers.
/// </summary>
internal sealed class CascadeBufferManager
{
    private readonly ComputeBuffer?[] _lightingCounterBuffers = new ComputeBuffer?[2];
    private int _activeLightingCounterBuffer;

    public ComputeBuffer? RadianceAtlas { get; private set; }
    public ComputeBuffer? RadianceScratchAtlas { get; private set; }
    public ComputeBuffer? DirtyRegions { get; private set; }
    public ComputeBuffer? CascadeChangedMask { get; private set; }
    public ComputeBuffer? DynamicLightBuffer { get; private set; }
    public ComputeBuffer? LightingCounters => _lightingCounterBuffers[_activeLightingCounterBuffer];
    public int AtlasCapacity { get; private set; }

    public void EnsurePersistentBuffers(
        int atlasEntryCount,
        long atlasDimension,
        int maximumLightCount)
    {
        long maximumCapacity = atlasDimension * atlasDimension * 4;

        if (maximumCapacity <= 0 || maximumCapacity > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Radiance cascade atlas capacity exceeds the supported structured-buffer size.");
        }

        if (atlasEntryCount > maximumCapacity)
        {
            throw new InvalidOperationException(
                "Radiance cascade layout exceeds the configured atlas capacity.");
        }

        int requiredCapacity = Mathf.Max(1, atlasEntryCount);
        int clampedLightCount = Mathf.Max(1, maximumLightCount);
        long plannedBytes = 0;
        if (RadianceAtlas == null || AtlasCapacity < requiredCapacity) { plannedBytes += (long)requiredCapacity * 12; }
        if (CascadeChangedMask == null || CascadeChangedMask.count < requiredCapacity) { plannedBytes += (long)requiredCapacity * 4; }
        if (DynamicLightBuffer == null || DynamicLightBuffer.count != clampedLightCount) { plannedBytes += (long)clampedLightCount * 32; }
        if (plannedBytes > 0) { MemoryAllocationGuard.Require("Lighting persistent buffers", plannedBytes + 24); }

        if (RadianceAtlas == null || AtlasCapacity < requiredCapacity)
        {
            RadianceAtlas?.Release();
            RadianceAtlas = new ComputeBuffer(
                requiredCapacity,
                sizeof(uint) * 3,
                ComputeBufferType.Structured);
            AtlasCapacity = requiredCapacity;
        }

        if (CascadeChangedMask == null || CascadeChangedMask.count < requiredCapacity)
        {
            CascadeChangedMask?.Release();
            CascadeChangedMask = new ComputeBuffer(
                requiredCapacity,
                sizeof(uint),
                ComputeBufferType.Structured);
        }

        if (DynamicLightBuffer == null || DynamicLightBuffer.count != clampedLightCount)
        {
            DynamicLightBuffer?.Release();
            DynamicLightBuffer = new ComputeBuffer(
                clampedLightCount,
                sizeof(float) * 8,
                ComputeBufferType.Structured);
        }

        if (_lightingCounterBuffers[0] == null || _lightingCounterBuffers[0]!.count != 3 ||
            _lightingCounterBuffers[1] == null || _lightingCounterBuffers[1]!.count != 3)
        {
            for (int index = 0; index < _lightingCounterBuffers.Length; index++)
            {
                _lightingCounterBuffers[index]?.Release();
                _lightingCounterBuffers[index] = new ComputeBuffer(
                    3,
                    sizeof(uint),
                    ComputeBufferType.Structured);
            }
        }
    }

    public void EnsureDirtyRegionCapacity(int capacity)
    {
        int requiredCapacity = Mathf.Max(1, capacity);
        if (DirtyRegions != null && DirtyRegions.count >= requiredCapacity)
        {
            return;
        }

        MemoryAllocationGuard.Require("Lighting dirty regions", (long)requiredCapacity * 16);
        DirtyRegions?.Release();
        DirtyRegions = new ComputeBuffer(
            requiredCapacity,
            sizeof(int) * 4,
            ComputeBufferType.Structured);
    }

    public void SwapRadianceAtlases()
    {
        EnsureScratchAtlas();
        (RadianceAtlas, RadianceScratchAtlas) =
            (RadianceScratchAtlas, RadianceAtlas);
    }

    // The atlas scroll path is disabled (see LightingUpdateCoordinator), so
    // its scratch duplicate is allocated lazily on first scroll use instead
    // of pinning a full atlas in VRAM forever. Re-enabling scroll needs no
    // other change: RecordScroll reaches this through SwapRadianceAtlases.
    public void EnsureScratchAtlas()
    {
        if (RadianceScratchAtlas != null && RadianceScratchAtlas.count == AtlasCapacity && AtlasCapacity > 0)
        {
            return;
        }

        if (AtlasCapacity > 0) { MemoryAllocationGuard.Require("Lighting scratch atlas", (long)AtlasCapacity * 12); }
        RadianceScratchAtlas?.Release();
        RadianceScratchAtlas = AtlasCapacity > 0
            ? new ComputeBuffer(AtlasCapacity, sizeof(uint) * 3, ComputeBufferType.Structured)
            : null;
    }

    public void ReleaseBuffers()
    {
        DynamicLightBuffer?.Release();
        DynamicLightBuffer = null;
        for (int index = 0; index < _lightingCounterBuffers.Length; index++)
        {
            _lightingCounterBuffers[index]?.Release();
            _lightingCounterBuffers[index] = null;
        }

        _activeLightingCounterBuffer = 0;
        RadianceAtlas?.Release();
        RadianceAtlas = null;
        RadianceScratchAtlas?.Release();
        RadianceScratchAtlas = null;
        DirtyRegions?.Release();
        DirtyRegions = null;
        CascadeChangedMask?.Release();
        CascadeChangedMask = null;
        AtlasCapacity = 0;
    }
}
