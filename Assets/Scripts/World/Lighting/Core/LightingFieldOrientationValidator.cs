#nullable enable

using System;
using Kern.Core.Interfaces.WorldLighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Proves once per domain that the field raster and its compute readers agree
/// on row order. A mismatch mirrors every lighting/AO field around its region
/// centre; that is an error, never a silently mirrored image.
/// </summary>
internal static class LightingFieldOrientationValidator
{
    private const int ProbeSize = 4;
    private const string ProbeShaderPath = "Shaders/Lighting/LightingFieldOrientationProbe";

    private static readonly int _probeFieldID = Shader.PropertyToID("_ProbeField");
    private static readonly int _probeRowsID = Shader.PropertyToID("_ProbeRows");
    private static readonly int _probeRowCountID = Shader.PropertyToID("_ProbeRowCount");

    private static bool _validated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload() => _validated = false;

    public static void EnsureValidated()
    {
        if (_validated)
        {
            return;
        }

        Shader shader = Resources.Load<Shader>(ProbeShaderPath) ??
            throw new InvalidOperationException($"Required shader Resources/{ProbeShaderPath}.shader is missing.");
        ComputeShader compute = Resources.Load<ComputeShader>(ProbeShaderPath) ??
            throw new InvalidOperationException($"Required compute shader Resources/{ProbeShaderPath}.compute is missing.");
        if (!shader.isSupported)
        {
            throw new InvalidOperationException($"Lighting field orientation probe is unsupported on {SystemInfo.graphicsDeviceType}.");
        }

        // World rect ProbeSize×ProbeSize; the quad covers exactly its top half.
        var worldRect = new Vector4(0f, 0f, ProbeSize, ProbeSize);
        var mesh = new Mesh { name = "LightingFieldOrientationProbe" };
        mesh.SetVertices(new[]
        {
            new Vector3(0f, ProbeSize * 0.5f, 0f),
            new Vector3(ProbeSize, ProbeSize * 0.5f, 0f),
            new Vector3(ProbeSize, ProbeSize, 0f),
            new Vector3(0f, ProbeSize, 0f),
        });
        mesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0);
        var material = new Material(shader) { name = "LightingFieldOrientationProbe" };
        var target = new RenderTexture(ProbeSize, ProbeSize, 0, RenderTextureFormat.ARGB32)
        {
            name = "LightingFieldOrientationProbe",
            filterMode = FilterMode.Point,
        };
        var rows = new ComputeBuffer(ProbeSize, sizeof(uint));
        var commandBuffer = new CommandBuffer { name = "Kern Lighting Field Orientation Probe" };
        try
        {
            if (!target.Create())
            {
                throw new InvalidOperationException("Failed to create the lighting field orientation probe target.");
            }

            commandBuffer.SetRenderTarget(target);
            commandBuffer.ClearRenderTarget(clearDepth: false, clearColor: true, backgroundColor: Color.clear);
            commandBuffer.SetViewport(new Rect(0f, 0f, ProbeSize, ProbeSize));
            LightingFieldOrientation.BindRaster(commandBuffer, worldRect, Matrix4x4.identity);
            commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);

            int kernel = compute.FindKernel("ReadProbeRows");
            commandBuffer.SetComputeTextureParam(compute, kernel, _probeFieldID, target);
            commandBuffer.SetComputeBufferParam(compute, kernel, _probeRowsID, rows);
            commandBuffer.SetComputeIntParam(compute, _probeRowCountID, ProbeSize);
            commandBuffer.DispatchCompute(compute, kernel, 1, 1, 1);
            Graphics.ExecuteCommandBuffer(commandBuffer);

            // One synchronous read at pipeline start; never on a frame path.
            var covered = new uint[ProbeSize];
            rows.GetData(covered);
            bool topDown = LightingFieldOrientation.RowsTopDown;
            for (int row = 0; row < ProbeSize; row++)
            {
                bool upperHalf = topDown ? row < ProbeSize / 2 : row >= ProbeSize / 2;
                if ((covered[row] == 1u) != upperHalf)
                {
                    throw new InvalidOperationException(
                        $"Lighting field raster row order disagrees with its readers on {SystemInfo.graphicsDeviceType}: " +
                        $"rows top-down={topDown}, covered=[{string.Join(",", covered)}]. Every field would be mirrored.");
                }
            }

            _validated = true;
        }
        finally
        {
            commandBuffer.Release();
            rows.Release();
            RenderTexture? probeTarget = target;
            LightingTexturePool.ReleaseTexture(ref probeTarget);
            LightingTexturePool.DestroyLightingObject(material);
            LightingTexturePool.DestroyLightingObject(mesh);
        }
    }
}
