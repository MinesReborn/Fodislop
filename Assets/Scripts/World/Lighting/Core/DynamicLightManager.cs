#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting
{
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

    /// <summary>
    /// Manages registration, spatial filtering, and GPU upload for dynamic light sources.
    /// </summary>
    public sealed class DynamicLightManager
    {
        private const float DynamicLightPositionEpsilon = 0.00390625f;
        private const float MaximumDynamicLightPositionEpsilon = 0.0625f;

        private readonly SortedDictionary<int, DynamicLightSource> _externalLights = new();
        private readonly List<int> _lastDroppedDynamicLightIds = new();
        private DynamicLightGpuData[] _dynamicLights = new DynamicLightGpuData[1];
        private int _lastDynamicLightCount;
        private int _lastDroppedDynamicLightCount;
        private bool _externalLightsDirty;
        private uint _dynamicLightGeneration;

        public int Count => _externalLights.Count;
        public uint Generation => _dynamicLightGeneration;
        public int UploadedCount => _lastDynamicLightCount;
        public int DroppedCount => _lastDroppedDynamicLightCount;
        public IReadOnlyList<int> DroppedLightIds => _lastDroppedDynamicLightIds;
        public bool IsDirty => _externalLightsDirty;

        public void ClearDirty() => _externalLightsDirty = false;

        public void MarkDirty() => _externalLightsDirty = true;

        public void IncrementGeneration() => _dynamicLightGeneration++;

        public void SetDynamicLight(
            int id,
            Vector2 position,
            Color color,
            float intensity,
            float effectivePixelsPerCell)
        {
            if (Mathf.Max(0f, intensity) <= 0f)
            {
                RemoveDynamicLight(id);
                return;
            }

            var source = new DynamicLightSource(position, color, intensity);
            if (_externalLights.TryGetValue(id, out DynamicLightSource previous) &&
                DynamicLightSourceApproximatelyEquals(previous, source, effectivePixelsPerCell))
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
            }
        }

        public void ResetUploadState()
        {
            _lastDynamicLightCount = 0;
            _lastDroppedDynamicLightCount = 0;
            _lastDroppedDynamicLightIds.Clear();
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
            _lastDroppedDynamicLightIds.Clear();

            foreach (KeyValuePair<int, DynamicLightSource> pair in _externalLights)
            {
                DynamicLightSource source = pair.Value;
                if (dynamicLightCount >= maximumLightCount)
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
                    continue;
                }

                if (source.Intensity <= 0f)
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
                    continue;
                }

                if (!IntersectsWorldRect(source.Position, 32f, worldRect, cellSize))
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
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

                _dynamicLights[dynamicLightCount++] = dynamicLight;
            }

            if (dynamicLightCount != previousDynamicLightCount)
            {
                uploadedLightsChanged = true;
            }

            _lastDynamicLightCount = dynamicLightCount;
            _lastDroppedDynamicLightCount = _lastDroppedDynamicLightIds.Count;

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

        private static bool DynamicLightSourceApproximatelyEquals(
            DynamicLightSource left,
            DynamicLightSource right,
            float effectivePixelsPerCell)
        {
            float epsilon = effectivePixelsPerCell > 0f
                ? Mathf.Min(0.5f / effectivePixelsPerCell, MaximumDynamicLightPositionEpsilon)
                : DynamicLightPositionEpsilon;

            return (left.Position - right.Position).sqrMagnitude <= epsilon * epsilon &&
                left.Color == right.Color &&
                Mathf.Approximately(left.Intensity, right.Intensity);
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
}
