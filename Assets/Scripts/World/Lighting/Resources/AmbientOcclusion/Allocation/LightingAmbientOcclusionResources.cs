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

internal sealed class LightingAmbientOcclusionResources
{
    internal RenderTexture? Field => _ambientOcclusionField;

    internal void Allocate(int width, int height)
    {
        _ambientOcclusionField = LightingTexturePool.CreateTexture(width, height,
            RenderTextureFormat.R8, randomWrite: false, FilterMode.Point,
            "_LightingAmbientOcclusionField", useMipMap: false);
    }

    internal void ReleaseField()
    {
        LightingTexturePool.ReleaseTexture(ref _ambientOcclusionField);
        LightingTexturePool.ReleaseTexture(ref _shiftScratch);
    }

    /// <summary>
    /// Буфер для переноса AO-поля при переякоривании светового региона.
    /// Копирование внутри одной текстуры запрещено, поэтому перекрытие
    /// снимается в этот буфер и возвращается уже со сдвигом.
    /// </summary>
    internal RenderTexture EnsureShiftScratch(int width, int height)
    {
        if (_shiftScratch == null || _shiftScratch.width != width || _shiftScratch.height != height)
        {
            LightingTexturePool.ReleaseTexture(ref _shiftScratch);
            _shiftScratch = LightingTexturePool.CreateTexture(width, height,
                RenderTextureFormat.R8, randomWrite: false, FilterMode.Point,
                "_LightingAmbientOcclusionShift", useMipMap: false);
        }

        return _shiftScratch;
    }

    internal void ReleaseMaterial()
    {
        if (_ambientOcclusionClearMaterial != null)
        {
            LightingTexturePool.DestroyLightingObject(_ambientOcclusionClearMaterial);
            _ambientOcclusionClearMaterial = null;
        }
    }

    internal RenderTexture? _ambientOcclusionField;
    private RenderTexture? _shiftScratch;
    internal Material? _ambientOcclusionClearMaterial;
    public Material AmbientOcclusionClearMaterial => _ambientOcclusionClearMaterial ??
        throw new InvalidOperationException("AO rectangle clear material has not been initialized.");

    internal void EnsureAmbientOcclusionClearMaterial()
    {
        if (_ambientOcclusionClearMaterial != null)
        {
            return;
        }

        Shader shader = Resources.Load<Shader>("Shaders/Lighting/Fields/Clearing/LightingFieldRectClear") ??
            throw new InvalidOperationException("Required LightingFieldRectClear shader is missing.");
        if (!shader.isSupported)
        {
            throw new InvalidOperationException("AO rectangle clear shader is unsupported.");
        }

        _ambientOcclusionClearMaterial = new Material(shader)
        {
            name = "Lighting.AmbientOcclusionRectClear",
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    internal static int AmbientOcclusionScale(int gridWidth, int gridHeight)
    {
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
        {
            throw new NotSupportedException(
                $"The active graphics device does not support the required single-channel AO target ({RenderTextureFormat.R8}).");
        }

        const int scale = LightingConfigHolder.AmbientOcclusionPixelsPerCell;
        int width = checked(gridWidth * scale);
        int height = checked(gridHeight * scale);
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException(
                $"AO requires {width}x{height} at {scale} pixels/cell; GPU limit is {SystemInfo.maxTextureSize}.");
        }
        return scale;
    }

}
