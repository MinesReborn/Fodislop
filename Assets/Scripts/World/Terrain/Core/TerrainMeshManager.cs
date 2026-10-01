#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

public sealed class TerrainMeshManager
{
    private static readonly int _geometryCarrierPaddingWorldID =
        Shader.PropertyToID("_TerrainGeometryCarrierPaddingWorld");

    // Раскладка вершины осталась только у накладки дверей: сам террейн
    // рисуется мешем идентификаторов по текстурам данных клетки.
    internal static readonly VertexAttributeDescriptor[] VertexLayout =
    [
        new(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.Color,     VertexAttributeFormat.UNorm8,  4),
        new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2), // quad UV          16 → 4 bytes
        new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4), // atlasRect        16 → 8 bytes
        new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4), // tileSizeVec      16 → 8 bytes
        new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4), // worldPos: stays float32 (coords > 2048)
        new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float16, 4), // animData         16 → 8 bytes
        new(VertexAttribute.TexCoord5, VertexAttributeFormat.Float16, 4), // anchorData       16 → 8 bytes
        new(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4), // glowVec: stays float32 (packed RGB > 65504)
    ];

    private readonly RenderTargetIdentifier[] _lightingFieldTargets = new RenderTargetIdentifier[2];
    private readonly RenderTargetIdentifier[] _ambientOcclusionTarget = new RenderTargetIdentifier[1];
    private readonly RenderBufferLoadAction[] _lightingFieldLoads =
        [RenderBufferLoadAction.DontCare, RenderBufferLoadAction.DontCare];
    private readonly RenderBufferStoreAction[] _lightingFieldStores =
        [RenderBufferStoreAction.Store, RenderBufferStoreAction.Store];
    private readonly RenderBufferLoadAction[] _ambientOcclusionLoads = [RenderBufferLoadAction.DontCare];
    private readonly RenderBufferStoreAction[] _ambientOcclusionStores = [RenderBufferStoreAction.Store];

    public void RenderLightingMaterialFields(
        CommandBuffer commandBuffer,
        RenderTexture materialField,
        RenderTexture emissionField,
        Vector4 worldRect,
        Matrix4x4 localToWorldMatrix,
        Material[] materials,
        Mesh? mesh,
        Vector4 screenViewOffset)
    {
        if (mesh == null || materials.Length == 0 ||
            !materialField.IsCreated() || !emissionField.IsCreated())
        {
            throw new InvalidOperationException(
                "Terrain material fields cannot be rendered before the terrain mesh and targets are ready.");
        }

        _lightingFieldTargets[0] = new RenderTargetIdentifier(materialField);
        _lightingFieldTargets[1] = new RenderTargetIdentifier(emissionField);
        commandBuffer.DisableScissorRect();
        // Anchor the attachment extent to this offscreen target. Builtin None
        // leaves the raster pass dependent on the preceding camera target.
        // The field has no depth storage; no extra depth texture is allocated.
        commandBuffer.SetRenderTarget(new RenderTargetBinding(
            _lightingFieldTargets, _lightingFieldLoads, _lightingFieldStores,
            new RenderTargetIdentifier(materialField),
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare));
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
        commandBuffer.SetViewport(new Rect(0f, 0f, materialField.width, materialField.height));
        commandBuffer.EnableScissorRect(new Rect(0f, 0f, materialField.width, materialField.height));

        DrawLightingField(
            commandBuffer,
            "Kern.Terrain.RenderMaterialFields",
            ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField,
            worldRect,
            localToWorldMatrix,
            materials,
            mesh,
            screenViewOffset,
            Vector2.zero);
    }

    public void RenderLightingAmbientOcclusionField(
        CommandBuffer commandBuffer,
        RenderTexture ambientOcclusionField,
        Vector4 worldRect,
        Matrix4x4 localToWorldMatrix,
        Material[] materials,
        Mesh? mesh,
        Vector4 screenViewOffset)
    {
        if (mesh == null || materials.Length == 0 || !ambientOcclusionField.IsCreated())
        {
            throw new InvalidOperationException(
                "Terrain AO field cannot be rendered before the terrain mesh and target are ready.");
        }

        _ambientOcclusionTarget[0] = new RenderTargetIdentifier(ambientOcclusionField);
        commandBuffer.DisableScissorRect();
        commandBuffer.SetRenderTarget(new RenderTargetBinding(
            _ambientOcclusionTarget, _ambientOcclusionLoads, _ambientOcclusionStores,
            new RenderTargetIdentifier(ambientOcclusionField),
            RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare));
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
        commandBuffer.SetViewport(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));
        commandBuffer.EnableScissorRect(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));

        DrawLightingField(
            commandBuffer,
            "Kern.Terrain.RenderAmbientOcclusionField",
            ProjectRuntimeContracts.ShaderPassNames.LightingAmbientOcclusionField,
            worldRect,
            localToWorldMatrix,
            materials,
            mesh,
            screenViewOffset,
            new Vector2(
                ProjectRuntimeContracts.World.CellSize * 0.5f +
                    worldRect.z / ambientOcclusionField.width * 0.5f,
                ProjectRuntimeContracts.World.CellSize * 0.5f +
                    worldRect.w / ambientOcclusionField.height * 0.5f));
    }

    private static void DrawLightingField(
        CommandBuffer commandBuffer,
        string sampleName,
        string shaderPassName,
        Vector4 worldRect,
        Matrix4x4 localToWorldMatrix,
        Material[] materials,
        Mesh mesh,
        Vector4 screenViewOffset,
        Vector2 carrierPaddingWorld)
    {

        // Field layout in texture memory has one owner shared with every
        // reader. Camera matrices are not touched: field vertices read only
        // these explicit globals, so no implicit API conversion is involved.
        LightingFieldOrientation.BindRaster(commandBuffer, worldRect, localToWorldMatrix);

        int shaderPass = materials[0].FindPass(shaderPassName);
        if (shaderPass < 0)
        {
            throw new InvalidOperationException(
                $"Terrain material '{materials[0].name}' is missing the '{shaderPassName}' pass.");
        }

        commandBuffer.BeginSample(sampleName);

        // Каждый вызов рисует свою целевую семантику отдельным проходом одного
        // материала; AO-проход читает альфа атласа, material-проход — его RGB.
        //
        // Частичная перерисовка по прямоугольникам отсюда убрана: очистка под
        // ножницами на Metal чистит ЦЕЛЬ, а не прямоугольник, поэтому каждый
        // патч стирал поле полностью и дорисовывал только свой кусок. В игре это
        // выглядело так, что свет пропадал, появлялся частями и уезжал при
        // движении. Возвращать эту оптимизацию можно только вместе со способом
        // чистить прямоугольник, которому можно доверять на всех бэкендах.
        // Поля покрывают всю сетку со смещением ноль; экранное смещение
        // возвращается сразу после, чтобы кадр камеры не съехал.
        commandBuffer.SetGlobalVector(TerrainCellDataTextures.ViewOffsetID, Vector4.zero);
        commandBuffer.SetGlobalVector(
            _geometryCarrierPaddingWorldID,
            new Vector4(carrierPaddingWorld.x, carrierPaddingWorld.y, 0f, 0f));
        commandBuffer.DrawMesh(mesh, localToWorldMatrix, materials[0], 0, shaderPass);
        commandBuffer.SetGlobalVector(TerrainCellDataTextures.ViewOffsetID, screenViewOffset);
        commandBuffer.SetGlobalVector(_geometryCarrierPaddingWorldID, Vector4.zero);

        commandBuffer.EndSample(sampleName);
        commandBuffer.DisableScissorRect();
    }
}
