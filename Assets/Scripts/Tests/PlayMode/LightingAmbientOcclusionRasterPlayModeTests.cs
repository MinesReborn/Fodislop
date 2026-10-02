#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

/// <summary>Production surface pass and AO recorder; terrain digging still needs a game capture.</summary>
[TestFixture]
[Category("GPU")]
public sealed class LightingAmbientOcclusionRasterPlayModeTests
{
    [UnityTest]
    public IEnumerator RemovedUpperCell_IsErasedWithoutTouchingLowerCell_AndMatchesFullRaster()
    {
        Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True);
        var resources = new LightingResourceManager();
        var registry = new LightingGeometryRegistry();
        var solver = new GeometryLightingSolver(resources);
        using var commands = new CommandBuffer { name = "AO regional regression" };
        var mesh = new Mesh { name = "AO production surface cells" };
        Shader shader = Shader.Find("Kern/World Surface");
        Assert.That(shader, Is.Not.Null);
        var material = new Material(shader);
        Texture2D white = RuntimeTextureFactory.CreateRGBA32NoMip(1, 1, "AO solid atlas",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        white.SetPixel(0, 0, Color.white);
        white.Apply(false);
        material.EnableKeyword("KERN_SURFACE_TRANSIT");
        material.SetTexture("_BaseMap", white);
        material.SetVector("_BaseMapTileCount", Vector4.one);
        material.SetVector("_WorldSize", Vector4.one);
        material.SetFloat("_Occupancy", 1f);
        var contributor = new SurfaceCells(mesh, material);
        float previousThreshold = Shader.GetGlobalFloat("_SurfaceFieldThreshold");
        commands.SetGlobalFloat("_SurfaceFieldThreshold", 0.5f);
        Vector4 worldRect = new(-8f, -8f, 16f, 16f);
        // Deliberately asymmetric in Y, with a retained cell far from the patch.
        RectInt patch = new(64, 352, 32, 32);
        try
        {
            resources.EnsureAmbientOcclusionOnlyResources(16, 16);
            RenderTexture field = resources.AmbientOcclusionField!;
            SetCells(mesh, includeUpper: true);
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            Color32[]? before = null;
            yield return Read(field, values => before = values);
            int upperIndex = LightingFieldOrientation.MemoryRow(368, field.height) * field.width + 80;
            int lowerIndex = LightingFieldOrientation.MemoryRow(80, field.height) * field.width + 368;
            Assert.That(before![upperIndex].a, Is.EqualTo(255), "Known upper solid cell must occupy its world texel.");
            Assert.That(before[lowerIndex].a, Is.EqualTo(255), "Known lower solid cell must occupy its world texel.");

            SetCells(mesh, includeUpper: false);
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect, patch);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            Color32[]? partial = null;
            yield return Read(field, values => partial = values);
            Assert.That(partial![upperIndex].a, Is.Zero, "Removed geometry must not survive Max blending.");
            Assert.That(partial[lowerIndex].a, Is.EqualTo(255), "Partial clearing must preserve distant geometry.");
            int changedOutside = 0;
            for (int y = 0; y < field.height; y++)
            {
                int row = LightingFieldOrientation.MemoryRow(y, field.height) * field.width;
                for (int x = 0; x < field.width; x++)
                {
                    if ((x < patch.xMin || x >= patch.xMax || y < patch.yMin || y >= patch.yMax) &&
                        before[row + x].a != partial[row + x].a)
                    {
                        changedOutside++;
                    }
                }
            }

            Assert.That(changedOutside, Is.Zero, "Every alpha texel outside the patch must remain unchanged.");

            // Independent update oracle: discard the retained field and redraw
            // the same production geometry in full, with no regional policy.
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect);
            Graphics.ExecuteCommandBuffer(commands);
            Color32[]? full = null;
            yield return Read(field, values => full = values);
            Assert.That(partial, Is.EqualTo(full), "Partial output must equal a fresh full production raster.");
        }
        finally
        {
            resources.ReleaseResources();
            resources.LightingCommandBuffer?.Release();
            Shader.SetGlobalFloat("_SurfaceFieldThreshold", previousThreshold);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(white);
        }
    }

    private static IEnumerator Read(RenderTexture field, System.Action<Color32[]> receive)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(field, 0);
        float started = Time.realtimeSinceStartup;
        while (!request.done)
        {
            Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(10f), "AO readback timed out.");
            yield return null;
        }

        Assert.That(request.hasError, Is.False);
        receive(request.GetData<Color32>().ToArray());
    }

    private static void SetCells(Mesh mesh, bool includeUpper)
    {
        mesh.Clear();
        mesh.vertices =
        [
            new(3f, -6f), new(4f, -6f), new(4f, -5f), new(3f, -5f),
            new(-6f, 3f), new(-5f, 3f), new(-5f, 4f), new(-6f, 4f),
        ];
        mesh.uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
            Vector2.zero, Vector2.right, Vector2.one, Vector2.up];
        mesh.uv2 = new Vector2[8];
        mesh.triangles = includeUpper ? [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7] : [0, 1, 2, 0, 2, 3];
    }

    private sealed class SurfaceCells(Mesh mesh, Material material) : ILightingGeometryContributor
    {
        public ulong LightingGeometryRevision => 1;

        public void RenderMaterialEmissionFields(CommandBuffer commands, in LightingMaterialEmissionContext context)
        {
        }

        public void RenderAmbientOcclusionField(CommandBuffer commands, in LightingAmbientOcclusionContext context)
        {
            LightingFieldOrientation.BindRaster(commands, context.WorldRect, Matrix4x4.identity);
            int pass = material.FindPass("LightingAmbientOcclusionField");
            Assert.That(pass, Is.GreaterThanOrEqualTo(0));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
        }
    }
}
