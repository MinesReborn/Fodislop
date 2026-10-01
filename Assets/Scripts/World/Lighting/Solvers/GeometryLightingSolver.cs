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
        // End the raster attachments before the cache kernels sample them.
        // Persistent field contents must be stored before compute consumption.
        commandBuffer.SetRenderTarget(BuiltinRenderTextureType.None);
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
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CleanCellPrefixID,
            _resources.CleanCellPrefix!);
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

        // Clean-medium summed-area tables: let the cascade merge prove a
        // probe's child paths are pure air or pure stone with four loads
        // (CascadeTrace.hlsl). The stone proof reads the cell's occupancy.
        int rowsKernel = _resources.BuildCleanCellRowsKernel;
        int columnsKernel = _resources.BuildCleanCellColumnsKernel;
        BindFieldTextures(commandBuffer, compute, rowsKernel);
        commandBuffer.SetComputeTextureParam(compute, rowsKernel, LightingComputeBinder.CellSolidMaskID, cellSolidMask);
        commandBuffer.SetComputeBufferParam(compute, rowsKernel, LightingComputeBinder.CleanCellRowsOutputID,
            _resources.CleanCellRows!);
        commandBuffer.DispatchCompute(compute, rowsKernel, (_resources.CellGridHeight + 63) / 64, 1, 1);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellRowsID,
            _resources.CleanCellRows!);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellPrefixOutputID,
            _resources.CleanCellPrefix!);
        commandBuffer.DispatchCompute(compute, columnsKernel, (_resources.CellGridWidth + 63) / 64, 1, 1);

        int buildSurfaceAirKernel = _resources.BuildSurfaceAirCacheKernel;
        BindFieldTextures(commandBuffer, compute, buildSurfaceAirKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.SurfaceAirCacheOutputID,
            _resources.SurfaceAirCache!);
        commandBuffer.DispatchCompute(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.DispatchGroups(_resources.LightWidth),
            LightingComputeBinder.DispatchGroups(_resources.LightHeight),
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
