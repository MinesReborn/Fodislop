#nullable enable

using System;
using Kern.Core;
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
        commandBuffer.SetRenderTarget(
            _lightingFieldTargets,
            new RenderTargetIdentifier(BuiltinRenderTextureType.None));
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);

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
        commandBuffer.SetRenderTarget(
            _ambientOcclusionTarget,
            new RenderTargetIdentifier(BuiltinRenderTextureType.None));
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);

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

        Matrix4x4 projection = Matrix4x4.Ortho(
            worldRect.x,
            worldRect.x + worldRect.z,
            worldRect.y,
            worldRect.y + worldRect.w,
            -100f,
            100f);
        commandBuffer.SetViewProjectionMatrices(
            Matrix4x4.identity,
            GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));

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
    }
}
