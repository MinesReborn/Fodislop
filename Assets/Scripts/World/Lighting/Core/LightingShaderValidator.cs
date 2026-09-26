#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal static class LightingShaderValidator
{
    public readonly record struct LoadedLightingCompute(
        ComputeShader Compute,
        int SolveCascadeKernel,
        int ScrollRadianceAtlasKernel,
        int ClearCascadeChangedMaskKernel,
        int SolveDynamicLightingKernel,
        int ComposeDynamicLightingKernel,
        int TraceDynamicPolarKernel,
        int ClearDynamicDirectKernel,
        int ResolveDirectKernel,
        int ResolveTransmissionDebugKernel,
        int CompositeLightingKernel,
        int BuildCellSolidMaskKernel,
        int BuildSurfaceAirCacheKernel);

    public static LoadedLightingCompute LoadComputeShader()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            throw new NotSupportedException("Radiance Cascades requires compute shader support.");
        }

        ComputeShader compute = Resources.Load<ComputeShader>(
            ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) ??
            throw new InvalidOperationException(
                "Required compute shader Resources/Shaders/Lighting/WorldLighting.compute is missing.");

        int solveCascadeKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.SolveCascade);
        int scrollRadianceAtlasKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ScrollRadianceAtlas);
        int clearCascadeChangedMaskKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ClearCascadeChangedMask);
        int solveDynamicLightingKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.SolveDynamicLighting);
        int composeDynamicLightingKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ComposeDynamicLighting);
        int traceDynamicPolarKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.TraceDynamicPolar);
        int clearDynamicDirectKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ClearDynamicDirect);
        int resolveDirectKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect);
        int resolveTransmissionDebugKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.ResolveTransmissionDebug);
        int compositeLightingKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting);
        int buildCellSolidMaskKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.BuildCellSolidMask);
        int buildSurfaceAirCacheKernel = FindAndValidateKernel(compute, ProjectRuntimeContracts.ComputeKernelNames.BuildSurfaceAirCache);

        return new LoadedLightingCompute(
            compute,
            solveCascadeKernel,
            scrollRadianceAtlasKernel,
            clearCascadeChangedMaskKernel,
            solveDynamicLightingKernel,
            composeDynamicLightingKernel,
            traceDynamicPolarKernel,
            clearDynamicDirectKernel,
            resolveDirectKernel,
            resolveTransmissionDebugKernel,
            compositeLightingKernel,
            buildCellSolidMaskKernel,
            buildSurfaceAirCacheKernel);
    }

    private static int FindAndValidateKernel(ComputeShader compute, string kernelName)
    {
        if (!compute.HasKernel(kernelName))
        {
            throw new InvalidOperationException(
                $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
        }

        int kernelIndex = compute.FindKernel(kernelName);
        if (!compute.IsSupported(kernelIndex))
        {
            throw new InvalidOperationException(
                $"Radiance Cascades kernel '{kernelName}' failed to compile for {SystemInfo.graphicsDeviceType}.");
        }

        return kernelIndex;
    }

    public static void ValidateGpuRequirements()
    {
        if (SystemInfo.supportedRenderTargetCount < 2 ||
            !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
            !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
            !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
        {
            throw new NotSupportedException(
                "Radiance Cascades requires two MRTs, RGBA8 material, and random-write lighting targets.");
        }
    }

    public static void ValidateTerrainFieldPasses(Action<UnityEngine.Object> destroyObject)
    {
        Shader terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain) ??
            throw new InvalidOperationException("The terrain shader required by lighting is missing.");

        var validationMaterial = new Material(terrainShader);

        try
        {
            if (validationMaterial.FindPass(
                    ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
            {
                throw new InvalidOperationException(
                    "The terrain shader is missing the LightingMaterialField pass.");
            }

            if (validationMaterial.FindPass(
                    ProjectRuntimeContracts.ShaderPassNames.LightingAmbientOcclusionField) < 0)
            {
                throw new InvalidOperationException(
                    "The terrain shader is missing the LightingAmbientOcclusionField pass.");
            }
        }
        finally
        {
            destroyObject(validationMaterial);
        }
    }
}
