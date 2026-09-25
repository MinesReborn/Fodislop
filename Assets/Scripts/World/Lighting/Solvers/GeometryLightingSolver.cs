#nullable enable

using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class GeometryLightingSolver
{
    private readonly LightingResourceManager _resources;

    public GeometryLightingSolver(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public void RecordMaterialField(
        CommandBuffer commandBuffer,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        commandBuffer.BeginSample("Kern.Lighting.MaterialField");
        terrainGeometry.RenderMaterialEmissionFields(
            commandBuffer,
            new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                _resources.MaterialField!,
                _resources.StaticEmissionField!,
                worldRect));
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderMaterialEmissionFields(
                commandBuffer,
                _resources.MaterialField!,
                _resources.StaticEmissionField!,
                worldRect,
                clearFields: false);
        }

        commandBuffer.EndSample("Kern.Lighting.MaterialField");
    }

    public void RecordAmbientOcclusionField(
        CommandBuffer commandBuffer,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        RenderTexture ambientOcclusionField = _resources.AmbientOcclusionField!;

        commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField");
        terrainGeometry.RenderAmbientOcclusionField(
            commandBuffer,
            new Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext(
                ambientOcclusionField,
                worldRect));
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderAmbientOcclusionField(
                commandBuffer,
                ambientOcclusionField,
                worldRect,
                clearField: false);
        }

        // Visible terrain samples mip zero around the transformed receiver.
        // Keeping exact displaced occupancy avoids carrier-shaped mip halos.
        commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField");
    }

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;

        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CellGridSizeID,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveDynamicLightingKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.TraceDynamicPolarKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.ResolveTransmissionDebugKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);

        if (!materialFieldRebuilt && _resources.GeometryCachesValid)
        {
            return;
        }

        commandBuffer.BeginSample("Kern.Lighting.GeometryCaches");
        BindFieldTextures(commandBuffer, compute, buildMaskKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildMaskKernel,
            LightingComputeBinder.CellSolidMaskOutputID,
            cellSolidMask);

        commandBuffer.DispatchCompute(
            compute,
            buildMaskKernel,
            LightingComputeBinder.DispatchGroups(_resources.CellGridWidth),
            LightingComputeBinder.DispatchGroups(_resources.CellGridHeight),
            1);

        int buildSurfaceAirKernel = _resources.BuildSurfaceAirCacheKernel;
        BindFieldTextures(commandBuffer, compute, buildSurfaceAirKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.SurfaceAirCacheOutputID,
            _resources.SurfaceAirCache!);
        commandBuffer.DispatchCompute(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.DispatchGroups(_resources.FieldWidth),
            LightingComputeBinder.DispatchGroups(_resources.FieldHeight),
            1);

        commandBuffer.EndSample("Kern.Lighting.GeometryCaches");
        _resources.GeometryCachesValid = true;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            compute,
            kernel,
            _resources.MaterialField!,
            _resources.StaticEmissionField!,
            _resources.LightingCounters);
    }
}
