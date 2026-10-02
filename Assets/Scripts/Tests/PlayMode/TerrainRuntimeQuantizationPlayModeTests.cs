#nullable enable

using System.Collections;
using Kern.Core;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
        yield return PlayModeHarness.StartAtGateway();
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
        Scene game = PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame);
        TerrainRenderer terrain = PlayModeHarness.FindComponentInScene<TerrainRenderer>(game)
            ?? throw new AssertionException("MainGame has no TerrainRenderer.");

        yield return PlayModeHarness.WaitUntil(
            () => terrain.IsReadyForGameplay,
            PlayModeHarness.WorldTimeoutSeconds,
            "The live TerrainRenderer never became ready.");

        MeshFilter meshFilter = terrain.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = terrain.GetComponent<MeshRenderer>();
        Assert.That(meshFilter.sharedMesh, Is.Not.Null);
        Assert.That(meshFilter.sharedMesh!.name, Is.EqualTo("TerrainCellIdMesh"),
            "Gameplay must use the address mesh consumed by TerrainCellData.hlsl.");
        Assert.That(meshRenderer.sharedMaterials, Has.Length.EqualTo(1));

        Material cellMaterial = meshRenderer.sharedMaterials[0];
        Assert.That(cellMaterial, Is.Not.Null);
        Assert.That(cellMaterial.shader.name,
            Is.EqualTo(ProjectRuntimeContracts.ShaderNames.Terrain));
        Assert.That(cellMaterial.IsKeywordEnabled("KERN_TERRAIN_CELLS"), Is.True,
            "The live terrain renderer is drawing without the cell-data shader path.");
        Assert.That(terrain.BypassCPUMeshRebuild, Is.False,
            "The production test must observe the real builder, not a debug bypass.");
        Assert.That(terrain.LastFullBuildAnchoredForegroundCellCount, Is.GreaterThan(0),
            "The real map produced no anchored foreground geometry; a hand-filled GPU test would miss this.");
    }
}
