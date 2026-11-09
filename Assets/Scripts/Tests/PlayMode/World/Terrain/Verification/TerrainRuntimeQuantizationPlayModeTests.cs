#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

using Kern.Bootstrap.Scenes;

namespace Kern.Tests.PlayMode;

// This test enters the real world scene. It deliberately does not construct a
// replacement material, mesh, or cell-data texture: those tests can pass while
// TerrainRenderer still presents a legacy rectangular mesh in gameplay.
[TestFixture]
[Category("GPU")]
public sealed class TerrainRuntimeQuantizationPlayModeTests
{
    private const string TestDummyToken = "playmode-terrain-quantization-token";
    private DummyAuthenticationScope _authentication = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtMainMenu();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator MainGame_UsesProductionQuantizedTerrainPath()
    {
        Scene game = PlayModeHarness.Scene(SceneNames.MainGame);
        TerrainRenderer terrain = PlayModeHarness.FindComponentInScene<TerrainRenderer>(game)
            ?? throw new AssertionException("MainGame has no TerrainRenderer.");

        yield return PlayModeHarness.WaitUntil(
            () => terrain.IsReadyForGameplay,
            PlayModeHarness.WorldTimeoutSeconds,
            "The live TerrainRenderer never became ready.");

        yield return PlayModeHarness.WaitUntil(
            () => terrain.transform.Find("TerrainBlockOverlay") != null,
            PlayModeHarness.WorldTimeoutSeconds,
            "The terrain block overlay renderer was never created.");

        MeshFilter meshFilter = terrain.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = terrain.GetComponent<MeshRenderer>();
        Assert.That(meshFilter.sharedMesh, Is.Not.Null);
        Assert.That(meshFilter.sharedMesh!.name, Is.EqualTo("TerrainVolumeMesh"),
            "Gameplay must use the address mesh consumed by TerrainCellData.hlsl.");
        Assert.That(meshRenderer.sharedMaterials, Has.Length.EqualTo(1),
            "The visible background is a single layer; blocks are drawn by their own renderer.");
        Assert.That(meshFilter.sharedMesh.subMeshCount, Is.EqualTo(1));
        Assert.That(meshFilter.sharedMesh.GetVertexAttributeFormat(VertexAttribute.Position),
            Is.EqualTo(VertexAttributeFormat.Float32));
        Assert.That(meshFilter.sharedMesh.GetVertexAttributeDimension(VertexAttribute.Position), Is.EqualTo(4));
        Assert.That(meshFilter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0), Is.False);

        Transform blockOverlay = terrain.transform.Find("TerrainBlockOverlay");
        MeshFilter blockFilter = blockOverlay.GetComponent<MeshFilter>();
        MeshRenderer blockRenderer = blockOverlay.GetComponent<MeshRenderer>();
        Assert.That(blockFilter.sharedMesh, Is.Not.Null);
        Assert.That(blockFilter.sharedMesh!.name, Is.EqualTo("TerrainBlockMesh"));
        Assert.That(blockFilter.sharedMesh.subMeshCount, Is.EqualTo(2),
            "Walls and foreground faces share one block renderer.");
        Assert.That(blockFilter.sharedMesh.GetIndexCount(0), Is.GreaterThan(0),
            "The production mesh must contain actual side triangles.");
        Assert.That(blockRenderer.sortingOrder, Is.GreaterThan(WorldRenderLayers.WorldEntitySortingOrder),
            "Blocks must cover robots and tails, not sit under them.");
        Assert.That(blockRenderer.sharedMaterial, Is.SameAs(meshRenderer.sharedMaterial));

        Material cellMaterial = meshRenderer.sharedMaterials[0];
        Assert.That(cellMaterial, Is.Not.Null);
        Assert.That(cellMaterial.shader.name,
            Is.EqualTo(ShaderNames.Terrain));
        Assert.That(terrain.BypassCPUMeshRebuild, Is.False,
            "The production test must observe the real builder, not a debug bypass.");
        Assert.That(terrain.LastFullBuildAnchoredForegroundCellCount, Is.GreaterThan(0),
            "The real map produced no anchored foreground geometry; a hand-filled GPU test would miss this.");
    }

    [UnityTest]
    public IEnumerator MainGame_DepthChangesProductionCameraImage()
    {
        TerrainRenderer terrain = PlayModeHarness.FindComponentInScene<TerrainRenderer>(
            PlayModeHarness.Scene(SceneNames.MainGame))
            ?? throw new AssertionException("MainGame has no TerrainRenderer.");
        yield return PlayModeHarness.WaitUntil(
            () => terrain.IsReadyForGameplay, PlayModeHarness.WorldTimeoutSeconds,
            "Terrain never became ready for depth capture.");
        Camera camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        Material material = terrain.GetComponent<MeshRenderer>().sharedMaterials[0];
        Vector3 originalPosition = camera.transform.position;
        float originalZoom = camera.orthographicSize;
        float originalDepth = material.GetFloat("_TerrainDepthPixels");
        float originalDarkening = material.GetFloat("_TerrainDepthDarkening");
        try
        {
            // Render requests are synchronous: the scene, lighting, texture
            // animation and camera inputs stay identical within each comparison.
            foreach (float zoomScale in new[] { 0.75f, 1f })
            {
                foreach (Vector2 shift in new[] { Vector2.zero, new Vector2(-0.5f, 0.25f), new Vector2(0.5f, -0.25f) })
                {
                    camera.transform.position = originalPosition + new Vector3(shift.x, shift.y, 0f);
                    camera.orthographicSize = originalZoom * zoomScale;
                    material.SetFloat("_TerrainDepthPixels", 0f);
                    Color[] flat = CaptureProductionCamera(camera);
                    material.SetFloat("_TerrainDepthPixels", 16f);
                    material.SetFloat("_TerrainDepthDarkening", 0f);
                    Color[] brightWalls = CaptureProductionCamera(camera);
                    material.SetFloat("_TerrainDepthDarkening", 0.35f);
                    Color[] shadedWalls = CaptureProductionCamera(camera);
                    int wallPixels = 0;
                    int shadedPixels = 0;
                    for (int pixel = 0; pixel < flat.Length; pixel++)
                    {
                        if (ColorDistance(flat[pixel], brightWalls[pixel]) > 0.0001f)
                        {
                            wallPixels++;
                        }
                        if (ColorDistance(brightWalls[pixel], shadedWalls[pixel]) > 0.0001f)
                        {
                            shadedPixels++;
                        }
                    }
                    Assert.That(wallPixels, Is.GreaterThan(0),
                        $"Production walls invisible at zoom {zoomScale}, camera shift {shift}.");
                    Assert.That(shadedPixels, Is.GreaterThan(0),
                        "Wall darkening must reach the production camera image.");
                    Debug.Log($"[TerrainDepthCapture] zoom={zoomScale}, shift={shift}, " +
                        $"wallPixels={wallPixels}, shadedPixels={shadedPixels}, " +
                        $"submittedDepthCarrierQuads={terrain.SubmittedDepthCarrierQuads}");
                }
            }
        }
        finally
        {
            material.SetFloat("_TerrainDepthPixels", originalDepth);
            material.SetFloat("_TerrainDepthDarkening", originalDarkening);
            camera.transform.position = originalPosition;
            camera.orthographicSize = originalZoom;
        }
    }

    private static float ColorDistance(Color left, Color right) =>
        Mathf.Abs(left.r - right.r) + Mathf.Abs(left.g - right.g) + Mathf.Abs(left.b - right.b);

    private static Color[] CaptureProductionCamera(Camera camera)
    {
        RenderTexture target = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight,
            24, RenderTextureFormat.ARGBHalf);
        Camera? previousCamera = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        RenderTexture previousTarget = RenderTexture.active;
        var readback = new Texture2D(target.width, target.height, TextureFormat.RGBAHalf, false, true);
        try
        {
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = target,
            });
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            return readback.GetPixels();
        }
        finally
        {
            RenderTexture.active = previousTarget;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = previousCamera;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(readback);
        }
    }

}
