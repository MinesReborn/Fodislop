#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;
public readonly struct DynamicLightGpuData
{
    public readonly Vector4 PositionRadius;
    public readonly Vector4 ColorIntensity;

    public DynamicLightGpuData(
        Vector2 position,
        Color color,
        float intensity)
    {
        PositionRadius = new Vector4(position.x, position.y, 0f, 0f);
        ColorIntensity = new Vector4(color.r, color.g, color.b, intensity);
    }
}

public readonly record struct DynamicLightSource(
    Vector2 Position,
    Color Color,
    float Intensity);

public sealed class DynamicLightManager
{
    private readonly SortedDictionary<int, DynamicLightSource> _externalLights = new();
    private readonly List<int> _lastDroppedDynamicLightIDs = new();
    private DynamicLightGpuData[] _dynamicLights = new DynamicLightGpuData[1];
    private int _lastDynamicLightCount;
    private int _lastDroppedDynamicLightCount;
    private bool _externalLightsDirty;
    private uint _dynamicLightGeneration;

    public int Count => _externalLights.Count;
    public uint Generation => _dynamicLightGeneration;
    public int UploadedCount => _lastDynamicLightCount;

    // Same dynamic lights, in the same order, as the last GPU upload.
    public System.ReadOnlySpan<DynamicLightGpuData> UploadedLights =>
        new(_dynamicLights, 0, _lastDynamicLightCount);

    // Caller ids of UploadedLights, index for index.
    public System.ReadOnlySpan<int> UploadedLightIDs =>
        new(_uploadedLightIDs, 0, _lastDynamicLightCount);

    private int[] _uploadedLightIDs = new int[1];
    public int DroppedCount => _lastDroppedDynamicLightCount;
    public IReadOnlyList<int> DroppedLightIDs => _lastDroppedDynamicLightIDs;
    public bool IsDirty => _externalLightsDirty;

    public void ClearDirty() => _externalLightsDirty = false;

    public void MarkDirty() => _externalLightsDirty = true;

    public void IncrementGeneration() => _dynamicLightGeneration++;

    public void SetDynamicLight(
        int id,
        Vector2 position,
        Color color,
        float intensity)
    {
        if (Mathf.Max(0f, intensity) <= 0f)
        {
            RemoveDynamicLight(id);
            return;
        }

        var source = new DynamicLightSource(position, color, intensity);
        if (_externalLights.TryGetValue(id, out DynamicLightSource previous) &&
            previous == source)
        {
            return;
        }

        _externalLights[id] = source;
        _externalLightsDirty = true;
    }

    public void RemoveDynamicLight(int id)
    {
        if (_externalLights.Remove(id))
        {
            _externalLightsDirty = true;
        }
    }

    public void ClearDynamicLights()
    {
        if (_externalLights.Count == 0)
        {
            return;
        }

        _externalLights.Clear();
        _externalLightsDirty = true;
        _dynamicLightGeneration++;
    }

    public void EnsureCapacity(int capacity)
    {
        if (_dynamicLights.Length != capacity)
        {
            _dynamicLights = new DynamicLightGpuData[capacity];
            _uploadedLightIDs = new int[capacity];
        }
    }

    public void ResetUploadState()
    {
        _lastDynamicLightCount = 0;
        _lastDroppedDynamicLightCount = 0;
        _lastDroppedDynamicLightIDs.Clear();
    }

    public int UploadDynamicLights(
        CommandBuffer commandBuffer,
        ComputeBuffer? dynamicLightBuffer,
        Vector4 worldRect,
        float cellSize,
        out bool uploadedLightsChanged)
    {
        int maximumLightCount = _dynamicLights.Length;
        int dynamicLightCount = 0;
        int previousDynamicLightCount = _lastDynamicLightCount;
        uploadedLightsChanged = false;
        _lastDroppedDynamicLightIDs.Clear();

        foreach (KeyValuePair<int, DynamicLightSource> pair in _externalLights)
        {
            DynamicLightSource source = pair.Value;
            if (dynamicLightCount >= maximumLightCount)
            {
                _lastDroppedDynamicLightIDs.Add(pair.Key);
                continue;
            }

            if (source.Intensity <= 0f)
            {
                _lastDroppedDynamicLightIDs.Add(pair.Key);
                continue;
            }

            if (!IntersectsReach(source, worldRect, cellSize))
            {
                _lastDroppedDynamicLightIDs.Add(pair.Key);
                continue;
            }

            DynamicLightGpuData dynamicLight = new(
                source.Position * cellSize,
                source.Color,
                source.Intensity);

            if (dynamicLightCount >= previousDynamicLightCount ||
                !DynamicLightEquals(_dynamicLights[dynamicLightCount], dynamicLight))
            {
                uploadedLightsChanged = true;
            }

            _uploadedLightIDs[dynamicLightCount] = pair.Key;
            _dynamicLights[dynamicLightCount++] = dynamicLight;
        }

        if (dynamicLightCount != previousDynamicLightCount)
        {
            uploadedLightsChanged = true;
        }

        _lastDynamicLightCount = dynamicLightCount;
        _lastDroppedDynamicLightCount = _lastDroppedDynamicLightIDs.Count;

        if (uploadedLightsChanged && dynamicLightCount > 0 && dynamicLightBuffer != null)
        {
            commandBuffer.SetBufferData(
                dynamicLightBuffer,
                _dynamicLights,
                0,
                0,
                dynamicLightCount);
        }

        return dynamicLightCount;
    }

    private static bool DynamicLightEquals(
        DynamicLightGpuData left,
        DynamicLightGpuData right)
    {
        return left.PositionRadius == right.PositionRadius &&
            left.ColorIntensity == right.ColorIntensity;
    }

    // A source is relevant while its Beer-Lambert reach touches the field,
    // not while its center is near it: a bright source lights dozens of
    // cells past its own position. Mirrors the rect math in
    // DynamicLightingSolver so culling never drops a visible contribution.
    private static bool IntersectsReach(
        DynamicLightSource source,
        Vector4 worldRect,
        float cellSize)
    {
        float brightest = Mathf.Max(
            0f,
            Mathf.Max(source.Color.r, Mathf.Max(source.Color.g, source.Color.b)) * source.Intensity) *
            LightingConfigHolder.EmissionScale;
        if (brightest <= 0f)
        {
            return false;
        }

        float minimumExtinction = LightingComputeBinder.ResolveMinimumExtinction();
        if (minimumExtinction <= 0f)
        {
            return true;
        }

        float reachCells = Mathf.Max(
            0f,
            Mathf.Log(brightest * 1.5f / LightingComputeBinder.InvisibleDynamicRadiance) /
                minimumExtinction);
        return IntersectsWorldRect(source.Position, 0.5f + reachCells, worldRect, cellSize);
    }

    private static bool IntersectsWorldRect(
        Vector2 position,
        float radius,
        Vector4 worldRect,
        float cellSize)
    {
        float worldRadius = radius * cellSize;
        float worldPositionX = position.x * cellSize;
        float worldPositionY = position.y * cellSize;

        return worldPositionX + worldRadius >= worldRect.x &&
            worldPositionX - worldRadius <= worldRect.x + worldRect.z &&
            worldPositionY + worldRadius >= worldRect.y &&
            worldPositionY - worldRadius <= worldRect.y + worldRect.w;
    }
}
