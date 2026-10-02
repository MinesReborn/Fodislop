#nullable enable

using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class GeometryLightingSolver
{
    private static readonly ProfilerMarker s_aoRecordMarker = new("Kern.Lighting.AmbientOcclusion.Record.CPU");
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
        Vector4 worldRect,
        RectInt? rasterRect = null)
    {
        using var marker = s_aoRecordMarker.Auto();
        RenderTexture ambientOcclusionField = _resources.AmbientOcclusionField!;
        RectInt rect = rasterRect ?? new RectInt(0, 0, ambientOcclusionField.width, ambientOcclusionField.height);
        if (rect.width == 0 && rect.height == 0)
        {
            return;
        }

        if (rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0 ||
            rect.xMax > ambientOcclusionField.width || rect.yMax > ambientOcclusionField.height)
        {
            throw new System.ArgumentOutOfRangeException(nameof(rasterRect), "AO raster rectangle is outside its target.");
        }

        commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField");
        commandBuffer.BeginSample(rasterRect.HasValue
            ? "Kern.Lighting.AmbientOcclusionField.Partial"
            : "Kern.Lighting.AmbientOcclusionField.Full");
        commandBuffer.DisableScissorRect();
        commandBuffer.SetRenderTarget(new RenderTargetIdentifier(ambientOcclusionField),
            rasterRect.HasValue ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store);
        if (rasterRect.HasValue)
        {
            // Clear by rasterization: ClearRenderTarget ignores scissor on Metal.
            // The loaded attachment preserves every pixel outside this viewport.
            commandBuffer.SetViewport(new Rect(rect.x, rect.y, rect.width, rect.height));
            commandBuffer.DrawProcedural(Matrix4x4.identity, _resources.AmbientOcclusionClearMaterial,
                0, MeshTopology.Triangles, 3);
        }
        else
        {
            commandBuffer.ClearRenderTarget(false, true, Color.clear);
            // A full field precedes every regional update. Exercise the
            // regional clear on its real attachment here too: an older state
            // collection may not contain this new shader. Deferring its first
            // draw to digging builds a new graphics pipeline during that action.
            // This pixel is already transparent, so the field is unchanged.
            // Format, sample count, vertex inputs and render state match the
            // regional draw; there is no scratch target or synthetic probe pass.
            commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField.PrimeClearPipeline");
            commandBuffer.SetViewport(new Rect(0f, 0f, 1f, 1f));
            commandBuffer.DrawProcedural(Matrix4x4.identity, _resources.AmbientOcclusionClearMaterial,
                0, MeshTopology.Triangles, 3);
            commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField.PrimeClearPipeline");
        }

        commandBuffer.SetViewport(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));
        commandBuffer.EnableScissorRect(new Rect(rect.x, rect.y, rect.width, rect.height));
        terrainGeometry.RenderAmbientOcclusionField(
            commandBuffer,
            new Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext(
                ambientOcclusionField,
                worldRect)
            {
                RasterRect = rasterRect,
            });
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderAmbientOcclusionField(
                commandBuffer,
                ambientOcclusionField,
                worldRect,
                clearField: false,
                rasterRect: rasterRect);
        }

        // Visible terrain samples mip zero around the transformed receiver.
        // Keeping exact displaced occupancy avoids carrier-shaped mip halos.
        commandBuffer.DisableScissorRect();
        commandBuffer.SetViewport(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));
        commandBuffer.EndSample(rasterRect.HasValue
            ? "Kern.Lighting.AmbientOcclusionField.Partial"
            : "Kern.Lighting.AmbientOcclusionField.Full");
        commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField");
        FrameEventLog.Record($"AO: {(rasterRect.HasValue ? "частично" : "целиком")} " +
            $"{rect.width}×{rect.height} ({(long)rect.width * rect.height} пикселей), " +
            $"поле {ambientOcclusionField.width}×{ambientOcclusionField.height}, " +
            $"clear: 1 draw, {(rasterRect.HasValue ? (long)rect.width * rect.height : 1L)} пикселей");
    }

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;

        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CellGridSizeId,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CleanCellPrefixId,
            _resources.CleanCellPrefix!);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveDynamicLightingKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.TraceDynamicPolarKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.ResolveTransmissionDebugKernel,
            LightingComputeBinder.CellSolidMaskId,
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
            LightingComputeBinder.CellSolidMaskOutputId,
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
        commandBuffer.SetComputeTextureParam(compute, rowsKernel, LightingComputeBinder.CellSolidMaskId, cellSolidMask);
        commandBuffer.SetComputeBufferParam(compute, rowsKernel, LightingComputeBinder.CleanCellRowsOutputId,
            _resources.CleanCellRows!);
        commandBuffer.DispatchCompute(compute, rowsKernel, (_resources.CellGridHeight + 63) / 64, 1, 1);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellRowsId,
            _resources.CleanCellRows!);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellPrefixOutputId,
            _resources.CleanCellPrefix!);
        commandBuffer.DispatchCompute(compute, columnsKernel, (_resources.CellGridWidth + 63) / 64, 1, 1);

        int buildSurfaceAirKernel = _resources.BuildSurfaceAirCacheKernel;
        BindFieldTextures(commandBuffer, compute, buildSurfaceAirKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.SurfaceAirCacheOutputId,
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
