#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Terrain;

/// <summary>
/// Manages the terrain mesh lifecycle, vertex upload, material field passes and mesh bounds.
/// </summary>
public sealed class TerrainMeshManager
{
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

    private const MeshUpdateFlags UploadFlags =
        MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

    private readonly RenderTargetIdentifier[] _lightingFieldTargets = new RenderTargetIdentifier[2];
    private Mesh? _mesh;

    public Mesh? Mesh => _mesh;

    public void EnsureMesh(ref MeshFilter? meshFilter)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "TerrainMesh", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = _mesh;
            }
        }
        else if (meshFilter != null && meshFilter.sharedMesh != _mesh)
        {
            meshFilter.sharedMesh = _mesh;
        }
    }

    public void UploadVertexBuffer(
        TerrainMeshBuilder meshBuilder,
        int atlasCount,
        IFrameTelemetry telemetry)
    {
        if (_mesh == null)
        {
            return;
        }

        if (_mesh.vertexCount != meshBuilder.VertexBuffer.Length || _mesh.subMeshCount != atlasCount)
        {
            telemetry.TerrainMeshClearCount++;
            _mesh.Clear();
            _mesh.subMeshCount = atlasCount;
            _mesh.SetVertexBufferParams(meshBuilder.VertexBuffer.Length, VertexLayout);
        }

        _mesh.SetVertexBufferData(
            meshBuilder.VertexBuffer,
            0,
            0,
            meshBuilder.VertexBuffer.Length,
            0,
            UploadFlags);
    }

    public void UploadDirectVertexBuffer(TerrainMeshBuilder meshBuilder)
    {
        if (_mesh == null)
        {
            return;
        }

        _mesh.SetVertexBufferData(
            meshBuilder.VertexBuffer,
            0,
            0,
            meshBuilder.VertexBuffer.Length,
            0,
            UploadFlags);
    }

    public void UpdateMeshBounds(int meshWidth, int meshHeight, float cellSize)
    {
        if (_mesh == null)
        {
            return;
        }

        _mesh.bounds = new Bounds(
            new Vector3(meshWidth * cellSize * 0.5f, meshHeight * cellSize * 0.5f, 0f),
            new Vector3(
                (meshWidth * cellSize) + (cellSize * 2f),
                (meshHeight * cellSize) + (cellSize * 2f),
                2f));
    }

    public void RenderLightingMaterialFields(
        CommandBuffer commandBuffer,
        RenderTexture materialField,
        RenderTexture emissionField,
        Vector4 worldRect,
        Matrix4x4 localToWorldMatrix,
        Material[] materials)
    {
        if (_mesh == null || materials.Length == 0 ||
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

        int subMeshCount = Mathf.Min(_mesh.subMeshCount, materials.Length);
        int materialFieldPass = materials[0].FindPass(
            ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField);
        if (materialFieldPass < 0)
        {
            throw new InvalidOperationException(
                $"Terrain material '{materials[0].name}' is missing the LightingMaterialField pass.");
        }

        commandBuffer.BeginSample("Fodinae.Terrain.RenderMaterialFields");
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            Material material = materials[subMeshIndex];
            commandBuffer.DrawMesh(
                _mesh,
                localToWorldMatrix,
                material,
                subMeshIndex,
                materialFieldPass);
        }

        commandBuffer.EndSample("Fodinae.Terrain.RenderMaterialFields");
    }

    public void DestroyMesh()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_mesh, allowDestroyingAssets: true);
            }

            _mesh = null;
        }
    }
}
