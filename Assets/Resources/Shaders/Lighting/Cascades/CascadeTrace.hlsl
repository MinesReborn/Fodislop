#ifndef KERN_CASCADE_TRACE_HLSL
#define KERN_CASCADE_TRACE_HLSL

// SolveCascade: DDA traversal каскадов и запись в атлас.
//
// READS: _MaterialField, _EmissionField, _RadianceAtlas[cascade+1]
// WRITES: _RadianceAtlas[cascade]
// MAY: вызывать DDA (TraceRadianceSegment)
// MUST NOT: писать финальный свет, трогать DynamicLight buffers

bool DirtySegmentOverlap(float2 start, float2 end)
{
    float2 segmentMin = min(start, end);
    float2 segmentMax = max(start, end);
    bool overlap = false;
    [loop]
    for (int dirtyIndex = 0;
        dirtyIndex < _DirtyRegionCount && !overlap;
        dirtyIndex++)
    {
        int4 dirty = _DirtyRegions[dirtyIndex];
        if (segmentMax.x >= dirty.x && segmentMin.x <= dirty.z &&
            segmentMax.y >= dirty.y && segmentMin.y <= dirty.w)
        {
            overlap = true;
        }
    }

    return overlap;
}

bool CascadeEntryMayChange(int2 probe, uint directionIndex)
{
    float2 origin = (float2(probe) + 0.5) * _CascadeProbeSpacing;
    float angle = (float(directionIndex) + 0.5) * PI2 /
        float(_CascadeDirectionCount);
    float raySine;
    float rayCosine;
    sincos(angle, raySine, rayCosine);
    float2 rayDirection = float2(rayCosine, raySine);
    float2 intervalStart = origin + rayDirection * _CascadeInterval.x;
    float2 intervalEnd = origin + rayDirection * _CascadeInterval.y;

    // Single exit: Metal treats early returns as possibly-uninitialized.
    bool changed = false;
    if (_HasFarCascade == 0 || _EnableBilinearFix == 0)
    {
        changed = DirtySegmentOverlap(intervalStart, intervalEnd);
    }

    if (!changed && _HasFarCascade != 0)
    {
        uint directionBranchCount = clamp(
            (uint)_FarCascadeDirectionCount / (uint)_CascadeDirectionCount,
            1u,
            4u);
        uint farDirectionBase = directionIndex * directionBranchCount;
        float2 farProbePosition = origin / float(_FarCascadeProbeSpacing) - 0.5;
        int2 farProbeBase = int2(floor(farProbePosition));

        [loop]
        for (uint farDirectionBranch = 0u;
            farDirectionBranch < directionBranchCount && !changed;
            farDirectionBranch++)
        {
            uint farDirection = (farDirectionBase + farDirectionBranch) %
                (uint)_FarCascadeDirectionCount;
            [unroll]
            for (int farY = 0; farY < 2 && !changed; farY++)
            {
                [unroll]
                for (int farX = 0; farX < 2 && !changed; farX++)
                {
                    int2 farProbe = clamp(
                        farProbeBase + int2(farX, farY),
                        int2(0, 0),
                        _FarCascadeProbeSize - 1);
                    int farIndex = _FarCascadeOffset +
                        (farProbe.y * _FarCascadeProbeSize.x + farProbe.x) *
                        _FarCascadeDirectionCount + (int)farDirection;

                    bool overlap = false;
                    if (_EnableBilinearFix != 0)
                    {
                        float farAngle = (float(farDirection) + 0.5) * PI2 /
                            float(_FarCascadeDirectionCount);
                        float farSine;
                        float farCosine;
                        sincos(farAngle, farSine, farCosine);
                        float2 farOrigin =
                            (float2(farProbe) + 0.5) * _FarCascadeProbeSpacing;
                        float2 childIntervalStart = farOrigin +
                            float2(farCosine, farSine) * _FarCascadeInterval.x;
                        overlap = DirtySegmentOverlap(intervalStart, childIntervalStart);
                    }

                    if (_CascadeChangedMask[farIndex] != 0 || overlap)
                    {
                        changed = true;
                    }
                }
            }
        }
    }

    return changed;
}

[numthreads(64, 1, 1)]
void SolveCascade(uint3 dispatchId : SV_DispatchThreadID)
{
    uint localIndex = dispatchId.x + dispatchId.y * (uint)_CascadeDispatchRowWidth;
    if (localIndex >= (uint)_CascadeEntryCount)
    {
        return;
    }

    uint directionIndex = localIndex % (uint)_CascadeDirectionCount;
    uint probeIndex = localIndex / (uint)_CascadeDirectionCount;
    int2 localProbe = int2(
        probeIndex % (uint)_CascadeDispatchSize.x,
        probeIndex / (uint)_CascadeDispatchSize.x);
    int2 probe = _CascadeDispatchOrigin + localProbe;
    int atlasIndex = _CascadeOffset +
        (probe.y * _CascadeProbeSize.x + probe.x) * _CascadeDirectionCount +
        (int)directionIndex;
    if (_CascadeMaskEnabled != 0 &&
        !CascadeEntryMayChange(probe, directionIndex))
    {
        _CascadeChangedMask[atlasIndex] = 0;
        return;
    }

    uint3 previousPackedInterval = _RadianceAtlas[atlasIndex];
    float2 origin = (float2(probe) + 0.5) * _CascadeProbeSpacing;
    float angle = (float(directionIndex) + 0.5) * PI2 /
        float(_CascadeDirectionCount);
    float raySine;
    float rayCosine;
    sincos(angle, raySine, rayCosine);
    float2 rayDirection = float2(rayCosine, raySine);
    float2 intervalStart = origin + rayDirection * _CascadeInterval.x;
    float2 intervalEnd = origin + rayDirection * _CascadeInterval.y;
    float3 radiance = 0.0;
    float3 transmittance = 1.0;

    // Each interpolated far interval needs its own continuous near path.
    // Otherwise a far probe across a wall contributes without crossing it.
    if (_HasFarCascade == 0 || _EnableBilinearFix == 0)
    {
        TraceRadianceSegment(
            intervalStart,
            intervalEnd,
            true,
            radiance,
            transmittance);
    }

    if (_HasFarCascade != 0)
    {
        // Every cascade stores the next contiguous radial interval from the
        // same receiver position. Interpolate the coarser probe field at this
        // probe's position; offsetting the lookup to the near interval end
        // would apply the far cascade's interval start twice and leave gaps.
        float2 farProbePosition = origin / float(_FarCascadeProbeSpacing) - 0.5;
        int2 farProbeBase = int2(floor(farProbePosition));
        float2 farProbeBlend = frac(farProbePosition);
        uint directionBranchCount = clamp(
            (uint)_FarCascadeDirectionCount / (uint)_CascadeDirectionCount,
            1u,
            4u);
        uint farDirectionBase = directionIndex * directionBranchCount;

        if (_EnableBilinearFix == 0)
        {
            float3 farRadiance = 0.0;
            float3 farTransmittance = 0.0;

        [loop]
        for (uint farDirectionBranch = 0u; farDirectionBranch < directionBranchCount;
            farDirectionBranch++)
        {
            uint farDirection = (farDirectionBase + farDirectionBranch) %
                (uint)_FarCascadeDirectionCount;

            [unroll]
            for (int farY = 0; farY < 2; farY++)
            {
                [unroll]
                for (int farX = 0; farX < 2; farX++)
                {
                    int2 farProbe = clamp(
                        farProbeBase + int2(farX, farY),
                        int2(0, 0),
                        _FarCascadeProbeSize - 1);
                    int farIndex = _FarCascadeOffset +
                        (farProbe.y * _FarCascadeProbeSize.x + farProbe.x) *
                        _FarCascadeDirectionCount + (int)farDirection;
                    float probeWeight =
                        (farX == 0 ? 1.0 - farProbeBlend.x : farProbeBlend.x) *
                        (farY == 0 ? 1.0 - farProbeBlend.y : farProbeBlend.y) /
                        float(directionBranchCount);

                    if (probeWeight <= 0.0)
                    {
                        continue;
                    }

                    if (_LightingCountersEnabled != 0)
                    {
                        InterlockedAdd(_LightingCounters[2], 1u);
                    }

                    uint3 farPackedInterval = _RadianceAtlas[farIndex];
                    float3 sampledFarRadiance =
                        UnpackRadiance(farPackedInterval.xy);
                    float3 sampledFarTransmittance =
                        UnpackTransmittance(farPackedInterval);

                    farRadiance += sampledFarRadiance * probeWeight;
                    farTransmittance += sampledFarTransmittance * probeWeight;
                }
            }
        }

        radiance += transmittance * farRadiance;
        transmittance *= farTransmittance;
    }
    else
    {
        float3 fixedRadiance = 0.0;
        float3 fixedTransmittance = 0.0;

        [loop]
        for (uint farDirectionBranch = 0u; farDirectionBranch < directionBranchCount;
            farDirectionBranch++)
        {
            uint farDirection = (farDirectionBase + farDirectionBranch) %
                (uint)_FarCascadeDirectionCount;

            [unroll]
            for (int farY = 0; farY < 2; farY++)
            {
                [unroll]
                for (int farX = 0; farX < 2; farX++)
                {
                    int2 farProbe = clamp(
                        farProbeBase + int2(farX, farY),
                        int2(0, 0),
                        _FarCascadeProbeSize - 1);
                    int farIndex = _FarCascadeOffset +
                        (farProbe.y * _FarCascadeProbeSize.x + farProbe.x) *
                        _FarCascadeDirectionCount + (int)farDirection;
                    float probeWeight =
                        (farX == 0 ? 1.0 - farProbeBlend.x : farProbeBlend.x) *
                        (farY == 0 ? 1.0 - farProbeBlend.y : farProbeBlend.y) /
                        float(directionBranchCount);
                    // Угол с нулевым билинейным весом не входит в сумму
                    // ничем. Его выборка из атласа и, главное, его марш —
                    // работа целиком в пустоту. На краю каскада clamp
                    // схлопывает соседние углы в один и тот же зонд, и тогда
                    // нулевых весов становится половина.
                    if (probeWeight <= 0.0)
                    {
                        continue;
                    }

                    if (_LightingCountersEnabled != 0)
                    {
                        InterlockedAdd(_LightingCounters[2], 1u);
                    }

                    uint3 farPackedInterval = _RadianceAtlas[farIndex];
                    float3 sampledFarRadiance =
                        UnpackRadiance(farPackedInterval.xy);
                    float3 sampledFarTransmittance =
                        UnpackTransmittance(farPackedInterval);
                    float farAngle = (float(farDirection) + 0.5) * PI2 /
                        float(_FarCascadeDirectionCount);
                    float farSine;
                    float farCosine;
                    sincos(farAngle, farSine, farCosine);
                    float2 farDirectionVector = float2(farCosine, farSine);
                    float2 farOrigin =
                        (float2(farProbe) + 0.5) * _FarCascadeProbeSpacing;
                    float2 childIntervalStart = farOrigin +
                        farDirectionVector * _FarCascadeInterval.x;
                    float3 childNearRadiance;
                    float3 childNearTransmittance;
                    TraceRadianceSegment(
                        intervalStart,
                        childIntervalStart,
                        true,
                        childNearRadiance,
                        childNearTransmittance);
                    fixedRadiance += (childNearRadiance +
                        childNearTransmittance * sampledFarRadiance) * probeWeight;
                    fixedTransmittance +=
                        childNearTransmittance * sampledFarTransmittance * probeWeight;
                }
            }
        }

        radiance = fixedRadiance;
        transmittance = fixedTransmittance;
    }
}

    uint3 packedInterval = PackInterval(
        radiance,
        transmittance);
    _RadianceAtlas[atlasIndex] = packedInterval;
    if (_CascadeMaskEnabled != 0)
    {
        _CascadeChangedMask[atlasIndex] =
            any(previousPackedInterval != packedInterval) ? 1u : 0u;
    }
}

[numthreads(64, 1, 1)]
void ScrollRadianceAtlas(uint3 dispatchId : SV_DispatchThreadID)
{
    uint localIndex = dispatchId.x + dispatchId.y * (uint)_CascadeDispatchRowWidth;
    if (localIndex >= (uint)_ScrollCascadeEntryCount)
    {
        return;
    }

    uint directionIndex = localIndex % (uint)_ScrollDirectionCount;
    uint probeIndex = localIndex / (uint)_ScrollDirectionCount;
    int2 newProbe = int2(
        probeIndex % (uint)_ScrollProbeSize.x,
        probeIndex / (uint)_ScrollProbeSize.x);
    int2 oldProbe = newProbe + _ScrollDeltaProbes;
    int outputIndex = _ScrollCascadeOffset + (int)localIndex;
    if (any(oldProbe < int2(0, 0)) ||
        any(oldProbe >= _ScrollProbeSize))
    {
        _RadianceAtlasOutput[outputIndex] = uint3(0, 0, 0);
        return;
    }

    int inputIndex = _ScrollCascadeOffset +
        (oldProbe.y * _ScrollProbeSize.x + oldProbe.x) * _ScrollDirectionCount +
        (int)directionIndex;
    _RadianceAtlasOutput[outputIndex] = _RadianceAtlasInput[inputIndex];
}

// Debug-only transport pass. Keeping this kernel beside CascadeTrace makes the
// ownership of geometry traversal explicit: CascadeResolve remains an atlas
// lookup even when the transmission debug view is selected.
[numthreads(8, 8, 1)]
void ResolveTransmissionDebug(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    float2 origin = float2(pixel) + 0.5;
    float2 regionCellCount = _WorldRect.zw / _CellSize;
    float3 localTransmission = 0.0;

    [loop]
    for (int debugDirection = 0; debugDirection < 32; debugDirection++)
    {
        float debugAngle = (float(debugDirection) + 0.5) * PI2 / 32.0;
        float debugSine;
        float debugCosine;
        sincos(debugAngle, debugSine, debugCosine);
        float2 debugRay = float2(debugCosine, debugSine);
        float pixelsPerCell = length(float2(_FieldSize) / regionCellCount * debugRay);
        float3 unusedRadiance;
        float3 transmission;
        TraceRadianceSegment(
            origin,
            origin + debugRay * (_TransmittanceDebugDistanceCells * pixelsPerCell),
            false,
            unusedRadiance,
            transmission);
        localTransmission += transmission;
    }

    _DirectTexture[pixel] = float4(localTransmission / 32.0, 1.0);
}

#endif // KERN_CASCADE_TRACE_HLSL
