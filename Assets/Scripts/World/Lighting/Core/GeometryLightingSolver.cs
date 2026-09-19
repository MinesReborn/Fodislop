#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using Kern.World.Terrain;

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
        TerrainRenderer terrainRenderer,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        commandBuffer.BeginSample("Kern.Lighting.MaterialField");
        terrainRenderer.RenderLightingMaterialFields(
            commandBuffer,
            _resources.MaterialField!,
            _resources.StaticEmissionField!,
            worldRect);
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderLightingFields(
                commandBuffer,
                _resources.MaterialField!,
                _resources.StaticEmissionField!,
                worldRect,
                clearFields: false);
        }

        commandBuffer.GenerateMips(_resources.MaterialField!);
        commandBuffer.EndSample("Kern.Lighting.MaterialField");
    }

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        ComputeBuffer bounceTaps = _resources.BounceTaps!;
        ComputeBuffer bounceFilterWeights = _resources.BounceFilterWeights!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;
        int buildTapsKernel = _resources.BuildBounceTapsKernel;
        int buildFilterKernel = _resources.BuildBounceFilterKernel;

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
        commandBuffer.SetComputeTextureParam(
            compute,
            buildTapsKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildFilterKernel,
            LightingComputeBinder.CellSolidMaskID,
            cellSolidMask);
        commandBuffer.SetComputeBufferParam(
            compute,
            buildTapsKernel,
            LightingComputeBinder.BounceTapsID,
            bounceTaps);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveDiffuseBounceKernel,
            LightingComputeBinder.BounceTapsID,
            bounceTaps);
        commandBuffer.SetComputeBufferParam(
            compute,
            buildFilterKernel,
            LightingComputeBinder.BounceFilterWeightsID,
            bounceFilterWeights);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.CompositeLightingKernel,
            LightingComputeBinder.BounceFilterWeightsID,
            bounceFilterWeights);

        if (!materialFieldRebuilt && _resources.GeometryCachesValid)
        {
            return;
        }

        commandBuffer.BeginSample("Kern.Lighting.GeometryCaches");
        BindFieldTextures(commandBuffer, compute, buildMaskKernel);
        BindFieldTextures(commandBuffer, compute, buildTapsKernel);
        BindFieldTextures(commandBuffer, compute, buildFilterKernel);
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
        if (LightingConfigHolder.BounceEnabled)
        {
            commandBuffer.DispatchCompute(
                compute,
                buildTapsKernel,
                LightingComputeBinder.DispatchGroups(_resources.BounceWidth),
                LightingComputeBinder.DispatchGroups(_resources.BounceHeight),
                1);
            commandBuffer.DispatchCompute(
                compute,
                buildFilterKernel,
                LightingComputeBinder.DispatchGroups(_resources.FieldWidth),
                LightingComputeBinder.DispatchGroups(_resources.FieldHeight),
                1);
        }

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
