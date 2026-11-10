#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Diagnostics.Memory;

namespace Kern.World.Lighting;

internal sealed class LightingDynamicDistanceResources
{
    internal RenderTexture? Field => _dynamicDistanceField;
    internal ComputeBuffer? SeedsA => _dynamicDistanceSeedsA;
    internal ComputeBuffer? SeedsB => _dynamicDistanceSeedsB;

    internal RenderTexture? _dynamicDistanceField;
    internal ComputeBuffer? _dynamicDistanceSeedsA;
    internal ComputeBuffer? _dynamicDistanceSeedsB;
    public bool DynamicDistanceFieldValid { get; set; }

    public void EnsureDynamicDistanceField(int width, int height)
    {
        if (_dynamicDistanceField != null &&
            _dynamicDistanceField.width == width &&
            _dynamicDistanceField.height == height &&
            _dynamicDistanceSeedsA != null &&
            _dynamicDistanceSeedsB != null)
        {
            return;
        }

        if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat))
        {
            throw new NotSupportedException("JFA sphere tracing requires random-write RFloat support.");
        }

        ValidateDynamicDistanceFieldRequest(width, height);

        ReleaseDynamicDistanceField();
        int texelCount = checked(width * height);
        long allocationBytes = checked((long)texelCount * (sizeof(float) + sizeof(uint) * 2));
        MemoryAllocationGuard.Require("Dynamic JFA distance field", allocationBytes);
        try
        {
            _dynamicDistanceField = LightingTexturePool.CreateTexture(
                width,
                height,
                RenderTextureFormat.RFloat,
                randomWrite: true,
                FilterMode.Point,
                "_DynamicDistanceField",
                useMipMap: false);
            _dynamicDistanceSeedsA = new ComputeBuffer(texelCount, sizeof(uint), ComputeBufferType.Structured);
            _dynamicDistanceSeedsB = new ComputeBuffer(texelCount, sizeof(uint), ComputeBufferType.Structured);
            DynamicDistanceFieldValid = false;
        }
        catch
        {
            ReleaseDynamicDistanceField();
            throw;
        }
    }

    public static void ValidateDynamicDistanceFieldRequest(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "JFA requires a non-empty transport field.");
        }
    }

    public void ReleaseDynamicDistanceField()
    {
        LightingTexturePool.ReleaseTexture(ref _dynamicDistanceField);
        _dynamicDistanceSeedsA?.Release();
        _dynamicDistanceSeedsA = null;
        _dynamicDistanceSeedsB?.Release();
        _dynamicDistanceSeedsB = null;
        DynamicDistanceFieldValid = false;
    }

}
