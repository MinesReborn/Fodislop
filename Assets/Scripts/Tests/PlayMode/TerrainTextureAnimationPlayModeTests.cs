#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class TerrainTextureAnimationPlayModeTests
{
    private const string TerrainShaderName = "Universal Render Pipeline/Custom/Terrain";
    private const float AnimationCycleSeconds = 2.5f;
    private readonly List<Object> _ownedObjects = [];
    private readonly int[] _textureGlobalIds =
    [
        TerrainCellDataTextures.ColorId,
        TerrainCellDataTextures.MetaId,
        TerrainCellDataTextures.AtlasRectId,
        TerrainCellDataTextures.TileSizeId,
        TerrainCellDataTextures.AnimationId,
        TerrainCellDataTextures.WorldId,
        TerrainCellDataTextures.GlowId,
        TerrainCellDataTextures.GeometryXId,
        TerrainCellDataTextures.GeometryYId,
    ];
    private Texture?[] _previousGlobalTextures = null!;
    private readonly int[] _vectorGlobalIds =
    [
        TerrainCellDataTextures.GridSizeId,
        TerrainCellDataTextures.OriginId,
        TerrainCellDataTextures.ViewOffsetId,
    ];
    private static readonly int s_terrainDebugViewId = Shader.PropertyToID("_TerrainDebugView");
    private static readonly int s_worldLightDebugViewId = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int s_pixelArtFilteringId = Shader.PropertyToID("_PixelArtFiltering");
    private Vector4[] _previousGlobalVectors = null!;
    private int _previousTerrainDebugView;
    private int _previousWorldLightDebugView;
    private float _previousPixelArtFiltering;
    private TerrainCellDataTextures _cellData = null!;
    private float _previousTimeScale;
    private bool _worldLightingKeywordWasEnabled;
    private Texture2D _readback = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _previousGlobalTextures = new Texture?[_textureGlobalIds.Length];
        for (int index = 0; index < _textureGlobalIds.Length; index++)
        {
            _previousGlobalTextures[index] = Shader.GetGlobalTexture(_textureGlobalIds[index]);
        }

        _previousGlobalVectors = new Vector4[_vectorGlobalIds.Length];
        for (int index = 0; index < _vectorGlobalIds.Length; index++)
        {
            _previousGlobalVectors[index] = Shader.GetGlobalVector(_vectorGlobalIds[index]);
        }

        _previousTerrainDebugView = Shader.GetGlobalInteger(s_terrainDebugViewId);
        _previousWorldLightDebugView = Shader.GetGlobalInteger(s_worldLightDebugViewId);
        _previousPixelArtFiltering = Shader.GetGlobalFloat(s_pixelArtFilteringId);
        Shader.SetGlobalInteger(s_terrainDebugViewId, 0);
        Shader.SetGlobalInteger(s_worldLightDebugViewId, 0);
        Shader.SetGlobalFloat(s_pixelArtFilteringId, 0f);
        _previousTimeScale = Time.timeScale;
        _worldLightingKeywordWasEnabled = Shader.IsKeywordEnabled("KERN_WORLD_LIGHTING");
        Shader.DisableKeyword("KERN_WORLD_LIGHTING");
        Time.timeScale = 1f;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Time.timeScale = _previousTimeScale;
        if (_worldLightingKeywordWasEnabled)
        {
            Shader.EnableKeyword("KERN_WORLD_LIGHTING");
        }
        else
        {
            Shader.DisableKeyword("KERN_WORLD_LIGHTING");
        }

        for (int index = 0; index < _textureGlobalIds.Length; index++)
        {
            Shader.SetGlobalTexture(_textureGlobalIds[index], _previousGlobalTextures[index]);
        }

        for (int index = 0; index < _vectorGlobalIds.Length; index++)
        {
            Shader.SetGlobalVector(_vectorGlobalIds[index], _previousGlobalVectors[index]);
        }

        Shader.SetGlobalInteger(s_terrainDebugViewId, _previousTerrainDebugView);
        Shader.SetGlobalInteger(s_worldLightDebugViewId, _previousWorldLightDebugView);
        Shader.SetGlobalFloat(s_pixelArtFilteringId, _previousPixelArtFiltering);
        _cellData?.Dispose();
        foreach (Object ownedObject in _ownedObjects)
        {
            if (ownedObject != null)
            {
                Object.Destroy(ownedObject);
            }
        }

        _ownedObjects.Clear();
        yield return null;
    }

    [UnityTest]
    [Timeout(30_000)]
    public IEnumerator ProductionTerrainPass_AdvancesTextureAtlasFrames()
    {
        Shader shader = Shader.Find(TerrainShaderName);
        Assert.That(shader, Is.Not.Null, $"Production terrain shader '{TerrainShaderName}' was not found.");
        Assert.That(shader!.isSupported, Is.True, "Production terrain shader is unsupported on this graphics device.");

        Material material = Own(new Material(shader));
        Texture2D atlas = CreateTwoFrameAtlas();
        material.SetTexture("_BaseMap", atlas);
        material.SetTexture("_TerrainAtlas0", atlas);
        material.SetTexture("_TerrainDecalAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_TerrainDecalStoneAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_GroundDecalStrength", 0f);
        material.SetFloat("_StoneDecalStrength", 0f);
        material.EnableKeyword("KERN_TERRAIN_CELLS");

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        _cellData = new TerrainCellDataTextures();
        _cellData.EnsureCapacity(1, 1);
        _cellData.SetCell(
            0,
            0,
            TerrainCellDataPacker.ForegroundLayer,
            new TerrainCellTexels(
                Color.white,
                new Color32(1, 0xB4, 0, 0),
                new TerrainHalfTexel(H(0f), H(0f), H(0.5f), H(0.5f)),
                new TerrainHalfTexel(H(0.5f), H(0.5f), H(2f), H(1f)),
                new TerrainHalfTexel(H(0f), H(1f), H(0f), H(0f)),
                Vector4.zero,
                Vector4.zero,
                default,
                default));
        _cellData.MarkAllDirty();
        _cellData.Apply();
        _cellData.BindGlobals(cellSize: 2f, originX: 0, originY: 0);
        Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetId, Vector4.zero);
        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32)
        {
            name = "TerrainAnimationRegressionTarget",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        });
        Assert.That(target.Create(), Is.True, "Could not allocate the terrain animation render target.");
        _readback = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            1, 1, "TerrainAnimationReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));

        int pass = material.FindPass("Universal2D");
        Assert.That(pass, Is.GreaterThanOrEqualTo(0), "Production terrain shader has no Universal2D pass.");

        var observedRedFrame = false;
        var observedBlueFrame = false;
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(AnimationCycleSeconds * System.Diagnostics.Stopwatch.Frequency);

        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline &&
            !(observedRedFrame && observedBlueFrame))
        {
            Color sample = RenderAndReadCenter(mesh, material, pass, target);
            observedRedFrame |= sample.r > sample.b * 2f && sample.r > 0.2f;
            observedBlueFrame |= sample.b > sample.r * 2f && sample.b > 0.2f;
            yield return null;
        }

        Assert.That(observedRedFrame, Is.True,
            "The production terrain pass never rendered atlas frame 0 (red). " +
            "Check the mesh animation attributes, atlas binding, UV resolution, and shader pass.");
        Assert.That(observedBlueFrame, Is.True,
            "The production terrain pass never advanced to atlas frame 1 (blue). " +
            "Check animation frame count/speed propagation and the _Time-based UV offset.");
    }

    [UnityTest]
    [Timeout(30_000)]
    public IEnumerator ProductionTerrainPass_AnimatesMoltenLavaSurface()
    {
        Shader shader = Shader.Find(TerrainShaderName);
        Assert.That(shader, Is.Not.Null, $"Production terrain shader '{TerrainShaderName}' was not found.");
        Assert.That(shader!.isSupported, Is.True, "Production terrain shader is unsupported on this graphics device.");

        Material material = Own(new Material(shader));
        Texture2D lavaAtlas = CreateSolidTexture(new Color(0.9f, 0.12f, 0.015f, 1f));
        material.SetTexture("_BaseMap", lavaAtlas);
        material.SetTexture("_TerrainAtlas0", lavaAtlas);
        material.SetTexture("_TerrainDecalAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_TerrainDecalStoneAtlas", CreateSolidTexture(Color.clear));
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_GroundDecalStrength", 0f);
        material.SetFloat("_StoneDecalStrength", 0f);
        material.EnableKeyword("KERN_TERRAIN_CELLS");

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        _cellData = new TerrainCellDataTextures();
        _cellData.EnsureCapacity(1, 1);
        _cellData.SetCell(
            0,
            0,
            TerrainCellDataPacker.ForegroundLayer,
            new TerrainCellTexels(
                Color.white,
                new Color32(1, 0xB4, 0, 0),
                new TerrainHalfTexel(H(0f), H(0f), H(1f), H(1f)),
                new TerrainHalfTexel(H(1f), H(1f), H(1f), H(1f)),
                new TerrainHalfTexel(H(0f), H(10f), H(0f), H(2f)),
                new Vector4(4f, 4f, 0f, 0f),
                Vector4.zero,
                default,
                default));
        _cellData.MarkAllDirty();
        _cellData.Apply();
        _cellData.BindGlobals(cellSize: 2f, originX: 0, originY: 0);
        Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetId, Vector4.zero);

        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGB32)
        {
            name = "TerrainMoltenAnimationRegressionTarget",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        });
        Assert.That(target.Create(), Is.True, "Could not allocate the molten terrain render target.");
        _readback = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            1, 1, "TerrainMoltenReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));

        int pass = material.FindPass("Universal2D");
        Assert.That(pass, Is.GreaterThanOrEqualTo(0), "Production terrain shader has no Universal2D pass.");

        Color initial = RenderAndReadCenter(mesh, material, pass, target);
        bool changed = false;
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
            (long)(AnimationCycleSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline && !changed)
        {
            yield return null;
            Color current = RenderAndReadCenter(mesh, material, pass, target);
            changed = Mathf.Abs(current.r - initial.r) +
                Mathf.Abs(current.g - initial.g) +
                Mathf.Abs(current.b - initial.b) > 0.04f;
        }

        Assert.That(changed, Is.True,
            "Lava's production terrain pass stayed static. Check the molten profile in mesh animation data and its time-based shader branch.");
    }

    private Texture2D CreateTwoFrameAtlas()
    {
        var atlas = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            4, 4, "TerrainAnimationRegressionAtlas", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color32[16];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = index < 8
                ? new Color32(255, 0, 0, 255)
                : new Color32(0, 0, 255, 255);
        }

        atlas.SetPixels32(pixels);
        atlas.Apply(false, false);
        return atlas;
    }

    private Texture2D CreateSolidTexture(Color color)
    {
        var texture = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            1, 1, "TerrainAnimationSolid", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        texture.SetPixel(0, 0, color);
        texture.Apply(false, false);
        return texture;
    }

    private Mesh CreateTerrainAnimationQuad()
    {
        var mesh = new Mesh { name = "TerrainAnimationRegressionMesh" };
        mesh.vertices =
        [
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
            new Vector3(0f, 0f, 1f),
        ];
        mesh.uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up];
        mesh.triangles = [0, 1, 2, 0, 2, 3];
        mesh.RecalculateBounds();
        return mesh;
    }

    private Color RenderAndReadCenter(Mesh mesh, Material material, int pass, RenderTexture target)
    {
        var commandBuffer = new CommandBuffer { name = "Terrain animation regression capture" };
        commandBuffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        commandBuffer.SetRenderTarget(target);
        commandBuffer.ClearRenderTarget(true, true, Color.black);
        commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
        Graphics.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Release();

        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            _readback.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0, false);
            _readback.Apply(false, false);
            return _readback.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private static ushort H(float value) => TerrainVertex.H(value);

    private T Own<T>(T value)
        where T : Object
    {
        _ownedObjects.Add(value);
        return value;
    }
}
