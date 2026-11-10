#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.World;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World.Common.Rendering;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using CellType = MinesServer.Data.CellType;

using Kern.AssetPipeline.Loading;
using Kern.Bootstrap.WorldLighting;

using Kern.Diagnostics.World;
using MinesServer.Data;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed partial class TerrainTextureAnimationPlayModeTests
{
    private const string TerrainShaderName = "Universal Render Pipeline/Custom/Terrain";
    private const float AnimationCycleSeconds = 2.5f;
    private readonly List<Object> _ownedObjects = new();
    private readonly int[] _vectorGlobalIds =
    new[] {

        TerrainCellBuffers.GridSizeId,
        TerrainCellBuffers.OriginId,
        TerrainCellBuffers.ViewOffsetId,

    };
    private static readonly int s_terrainDebugViewId = Shader.PropertyToID("_TerrainDebugView");
    private static readonly int s_worldLightDebugViewId = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int s_pixelArtFilteringId = Shader.PropertyToID("_PixelArtFiltering");
    private Vector4[] _previousGlobalVectors = null!;
    private int _previousTerrainDebugView;
    private int _previousWorldLightDebugView;
    private float _previousPixelArtFiltering;
    private TerrainCellBuffers _cellData = null!;
    private float _previousTimeScale;
    private bool _worldLightingKeywordWasEnabled;
    private Texture2D _readback = null!;
#if UNITY_EDITOR
    private bool _previousAllowAsyncCompilation;
#endif
    private Camera? _renderCamera;
    private MeshFilter? _renderMesh;
    private MeshRenderer? _renderRenderer;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
#if UNITY_EDITOR
        _previousAllowAsyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
        UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
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
#if UNITY_EDITOR
        UnityEditor.ShaderUtil.allowAsyncCompilation = _previousAllowAsyncCompilation;
#endif
        Time.timeScale = _previousTimeScale;
        if (_worldLightingKeywordWasEnabled)
        {
            Shader.EnableKeyword("KERN_WORLD_LIGHTING");
        }
        else
        {
            Shader.DisableKeyword("KERN_WORLD_LIGHTING");
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

    [Test]
    public void LightingSurfacePublicationPreservesTerrainRimSetting()
    {
        int rimId = Shader.PropertyToID("_TerrainRimEnabled");
        float previousRim = Shader.GetGlobalFloat(rimId);
        try
        {
            foreach (float enabled in new[] { 0f, 1f })
            {
                Shader.SetGlobalFloat(rimId, enabled);
                for (int frame = 0; frame < 3; frame++)
                {
                    TerrainSurfaceShaderGlobals.ApplyShaderGlobals();
                    Assert.That(Shader.GetGlobalFloat(rimId), Is.EqualTo(enabled),
                        "Publishing lighting surface constants must preserve the terrain rim setting.");
                }
            }
        }
        finally
        {
            Shader.SetGlobalFloat(rimId, previousRim);
        }
    }

    [UnityTest]
    public IEnumerator ProductionTerrainRim_FollowsWorldNeighborsAndHonorsFalloff()
    {
        Material material = Own(new Material(Shader.Find(TerrainShaderName)));
        material.SetTexture("_TerrainAtlas0", CreateSolidTexture(Color.white, 32));
        material.SetFloat("_AlphaCutoff", 0.01f);
        material.SetFloat("_RoundableCornerRadius", 0.51f);
        material.SetFloat("_RimDistanceScale", 4f);
        material.SetFloat("_RimFalloff", 0.5f);
        material.SetFloat("_RimQuantizationEnabled", 1f);
        int stageId = Shader.PropertyToID("_KernTerrainBenchmarkStage");
        int rimId = Shader.PropertyToID("_TerrainRimEnabled");
        int previousStage = Shader.GetGlobalInteger(stageId);
        float previousRim = Shader.GetGlobalFloat(rimId);
        material.SetFloat(rimId, 1f);
        Mesh mesh = Own(CreateTerrainAnimationQuad());
        mesh.SetVertexBufferParams(4, TerrainVolumeMesh.VertexLayout);
        mesh.SetVertexBufferData(new[]
        {
            new TerrainVolumeVertex(1, 1, 1, 0, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(1, 1, 1, 1, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(1, 1, 1, 2, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(1, 1, 1, 3, Vector2.zero, Vector2.zero),
        }, 0, 0, 4);
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, calculateBounds: false);
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(3, 3);
        _cellData.SetType(0, TerrainCellData.PackType(new TerrainTypeFields(
            Type: 0, Block: Block(CellAnimationType.None, 0f),
            Slot: 0, AtlasRect: Vector4.zero, TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: false, OpaqueAny: false,
            HasTileGroup: false, TileGroupId: 0)));
        var type = (MinesServer.Data.CellType)1;
        _cellData.SetType(type, TerrainCellData.PackType(new TerrainTypeFields(
            Type: type,
            Block: new BlockDefinition(
                Passable: false,
                MovementCooldownMs: 100,
                DrawLayer: CellDrawLayer.Foreground,
                Outline: CellOutline.Round,
                TextureName: "",
                TextureAnchor: CellTextureAnchor.Cell,
                TileGroup: null,
                DecalTextureName: null,
                ReliefGroup: 1,
                AnimationType: CellAnimationType.None,
                AnimationPalette: 0,
                FrameAnimationSpeed: 0f,
                Emission: false,
                MapColor: default),
            Slot: 0, AtlasRect: new Vector4(0, 0, 1, 1), TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: false, OpaqueAny: false,
            HasTileGroup: false, TileGroupId: 0)));
        RenderTexture target = Own(new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBHalf));
        Assert.That(target.Create(), Is.True);
        Texture2D pixels = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(32, 32,
            "RimProductionReadback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp));
        try
        {
            Shader.SetGlobalInteger(stageId, 4);
            // A conflicting global must not disable the material-owned switch.
            Shader.SetGlobalFloat(rimId, 0f);
            // Four directed connections, a cross with four concave corners,
            // and an isolated rounded cell.
            for (int side = 0; side < 6; side++)
            {
                for (int y = 0; y < 3; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        bool neighbor = side == 4 ? x == 1 || y == 1
                            : side == 5 ? false : side == 0 ? x == 2 && y == 1
                            : side == 1 ? x == 0 && y == 1
                            : side == 2 ? x == 1 && y == 2 : x == 1 && y == 0;
                        _cellData.SetCell(x, y, TerrainCellData.PackCell(x == 1 && y == 1 || neighbor ? type : 0));
                    }
                }

                _cellData.MarkAllDirty();
                _cellData.Apply();
                _cellData.BindGlobals(2f, 0, 0, 3, 3, distortionEnabled: false);
                Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, Vector4.zero);
                using (var commands = new CommandBuffer())
                {
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(2, 4, 2, 4, -10, 10));
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(true, true, Color.clear);
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, material.FindPass("Universal2D"));
                    Graphics.ExecuteCommandBuffer(commands);
                }

                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                    pixels.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath,
                    $"kern-rim-side-{side}.png"), pixels.EncodeToPNG());

                for (int y = 2; y <= 29; y += 27)
                {
                    for (int x = 2; x <= 29; x += 27)
                    {
                        bool connected = side == 4 ? true : side == 5 ? false
                            : side == 0 ? x > 16 : side == 1 ? x < 16
                            : side == 2 ? y > 16 : y < 16;
                        Assert.That(pixels.GetPixel(x, y).a, connected ? Is.GreaterThan(0.9f) : Is.LessThan(0.1f),
                            $"World-oriented corner mismatch: side={side}, pixel={x},{y}.");
                    }
                }

                float darkestSurface = 1f;
                foreach (Color pixel in pixels.GetPixels())
                {
                    if (pixel.a > 0.9f)
                    {
                        darkestSurface = Mathf.Min(darkestSurface, pixel.r);
                        Assert.That(pixel.r, Is.GreaterThanOrEqualTo(0.49f),
                            "A 50% bevel must not compound into a darker corner.");
                    }
                }

                Assert.That(darkestSurface, Is.LessThan(0.85f),
                    $"The production rim must actually darken an exposed edge: pattern={side}.");

            }
        }
        finally
        {
            Shader.SetGlobalInteger(stageId, previousStage);
            Shader.SetGlobalFloat(rimId, previousRim);
        }

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
        material.SetTexture("_TerrainAtlas0", atlas);
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        // Атлас 64×64: клетка 32×32 (тайл — полатласа, как выводит шейдер из
        // 32 текселей на клетку), два кадра по 32 строки.
        UploadSingleCell(
            new TerrainTypeFields(
                Type: (MinesServer.Data.CellType)1,
                Block: Block(CellAnimationType.None, 1f),
                Slot: 0,
                AtlasRect: new Vector4(0f, 0f, 0.5f, 0.5f),
                TileSize: 0.5f,
                FrameCount: 2,
                FrameHeightTiles: 1f,
                OpaqueOwn: false,
                OpaqueAny: false,
                HasTileGroup: false,
                TileGroupId: 0),
            originX: 0,
            worldHeight: 1);
        RenderTexture target = Own(new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32)
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

        // Establish the production camera/pass before timing animation. Shader
        // initialization must not consume the entire observation interval.
        Color firstFrame = RenderAndReadCenter(mesh, material, pass, target);
        var observedRedFrame = firstFrame.r > firstFrame.b * 2f && firstFrame.r > 0.2f;
        var observedBlueFrame = firstFrame.b > firstFrame.r * 2f && firstFrame.b > 0.2f;
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
        Texture2D lavaAtlas = CreateSolidTexture(new Color(0.9f, 0.12f, 0.015f, 1f), 32);
        material.SetTexture("_TerrainAtlas0", lavaAtlas);
        material.SetTexture("_FlowMap", CreateSolidTexture(Color.gray));
        material.SetTexture("_PrismaticFlowMap", CreateSolidTexture(Color.gray));
        material.SetFloat("_AlphaCutoff", 0.01f);

        Mesh mesh = Own(CreateTerrainAnimationQuad());
        // Лава: поверхность расплава, мировая клетка (4, 4) — фаза потока от неё.
        UploadSingleCell(
            new TerrainTypeFields(
                Type: (MinesServer.Data.CellType)1,
                Block: Block(CellAnimationType.Molten, 10f),
                Slot: 0,
                AtlasRect: new Vector4(0f, 0f, 1f, 1f),
                TileSize: 1f,
                FrameCount: 1,
                FrameHeightTiles: 1f,
                OpaqueOwn: false,
                OpaqueAny: false,
                HasTileGroup: false,
                TileGroupId: 0),
            originX: 4,
            worldHeight: 5);

        RenderTexture target = Own(new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32)
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

    [UnityTest]
    public IEnumerator ProductionBackgroundSheet_KeepsAllTilesAcrossOriginChanges()
    {
        const int SheetCells = 10;
        const int TilePixels = 32;
        const int GridCells = SheetCells + 2;
        const int WorldHeight = 65535;
        const int TargetPixels = SheetCells * TilePixels;
        Shader shader = Shader.Find(TerrainShaderName);
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.isSupported, Is.True);
        Material material = Own(new Material(shader));
        Texture2D atlas = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            TargetPixels, TargetPixels, "BackgroundAddressOracle", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));
        var atlasPixels = new Color32[TargetPixels * TargetPixels];
        for (int y = 0; y < TargetPixels; y++)
        {
            for (int x = 0; x < TargetPixels; x++)
            {
                atlasPixels[y * TargetPixels + x] = new Color32(
                    (byte)(16 + x / TilePixels * 20), (byte)(16 + y / TilePixels * 20), 128, 255);
            }
        }

        atlas.SetPixels32(atlasPixels);
        atlas.Apply(false, false);
        material.SetTexture("_TerrainAtlas0", atlas);
        material.SetFloat("_TerrainRimEnabled", 0f);
        var type = (MinesServer.Data.CellType)32;
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(GridCells, GridCells);
        _cellData.SetType(type, TerrainCellData.PackType(new TerrainTypeFields(
            Type: type, Block: Block(CellAnimationType.None, 0f), Slot: 0,
            AtlasRect: new Vector4(0f, 0f, 1f, 1f), TileSize: 1f / SheetCells,
            FrameCount: 1, FrameHeightTiles: SheetCells, OpaqueOwn: true, OpaqueAny: true,
            HasTileGroup: false, TileGroupId: 0)));
        using var carrier = new TerrainVolumeMesh();
        carrier.EnsureSize(GridCells, GridCells, 2f, GridCells, GridCells, new TerrainVolumeTopology());
        Mesh mesh = carrier.Mesh!;
        mesh.bounds = new Bounds(new Vector3(12f, 12f, 0f), new Vector3(24f, 24f, 2f));
        GameObject meshObject = Own(new GameObject("Background sheet production mesh"));
        meshObject.layer = 31;
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        GameObject cameraObject = Own(new GameObject("Background sheet production camera"));
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = SheetCells;
        camera.aspect = 1f;
        camera.transform.position = new Vector3(12f, 12f, -10f);
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        RenderTexture target = Own(new RenderTexture(TargetPixels, TargetPixels, 24, RenderTextureFormat.ARGBHalf));
        Assert.That(target.Create(), Is.True);
        camera.targetTexture = target;
        Texture2D readback = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(
            TargetPixels, TargetPixels, "BackgroundSheetReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));
        foreach (Vector2Int origin in new[]
        {
            Vector2Int.zero, new Vector2Int(1184, 902), new Vector2Int(40000, 40000),
            new Vector2Int(65520, 60000), new Vector2Int(1184, 902),
        })
        {
            for (int y = 0; y < GridCells; y++)
            {
                for (int x = 0; x < GridCells; x++)
                {
                    _cellData.SetCell(origin.x + x, origin.y + y, TerrainCellData.PackCell(type));
                }
            }

            _cellData.MarkAllDirty();
            _cellData.Apply();
            _cellData.BindGlobals(2f, origin.x, origin.y, 65535, WorldHeight, distortionEnabled: false);
            Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, Vector4.zero);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, TargetPixels, TargetPixels), 0, 0);
                readback.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath,
                $"kern-background-origin-{origin.x}-{origin.y}.png"), readback.EncodeToPNG());
            // Oracle uses integer world coordinates and authored atlas bytes, never shader helpers.
            for (int y = 4; y < TargetPixels; y += 8)
            {
                for (int x = 4; x < TargetPixels; x += 8)
                {
                    int column = (origin.x + 1 + x / TilePixels) % SheetCells;
                    int serverY = WorldHeight - 1 - (origin.y + 1 + y / TilePixels);
                    int row = SheetCells - 1 - serverY % SheetCells;
                    Color sample = readback.GetPixel(x, y);
                    Assert.That(sample.b, Is.GreaterThan(0.1f), $"Background missing at {origin}, pixel {x},{y}.");
                    Assert.That(sample.r / sample.b, Is.EqualTo((16 + column * 20) / 128f).Within(0.025f),
                        $"Wrong atlas column at {origin}, pixel {x},{y}.");
                    Assert.That(sample.g / sample.b, Is.EqualTo((16 + row * 20) / 128f).Within(0.025f),
                        $"Wrong atlas row at {origin}, pixel {x},{y}.");
                }
            }

            Shader.SetGlobalInteger(s_terrainDebugViewId, (int)TerrainDebugView.BackgroundTileIdentity);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, TargetPixels, TargetPixels), 0, 0);
                readback.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath,
                $"kern-background-debug-{origin.x}-{origin.y}.png"), readback.EncodeToPNG());
            for (int y = 0; y < SheetCells; y++)
            {
                for (int x = 0; x < SheetCells; x++)
                {
                    int column = (origin.x + 1 + x) % SheetCells;
                    int serverY = WorldHeight - 1 - (origin.y + 1 + y);
                    int row = SheetCells - 1 - serverY % SheetCells;
                    int identity = (row << 7) + column + 1;
                    Color sample = readback.GetPixel(x * TilePixels + 16, y * TilePixels + 16);
                    Assert.That(sample.r, Is.EqualTo((identity & 63) / 63f).Within(0.002f),
                        $"Diagnostic red channel mismatch at {origin}, cell {x},{y}.");
                    Assert.That(sample.g, Is.EqualTo(((identity >> 6) & 63) / 63f).Within(0.002f));
                    Assert.That(sample.b, Is.EqualTo(((identity >> 12) & 63) / 63f).Within(0.002f));
                }
            }

            Shader.SetGlobalInteger(s_terrainDebugViewId, 0);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ProductionAmbientOcclusion_UsesReducedContactRadius()
    {
        Material material = Own(new Material(Shader.Find(TerrainShaderName)));
        material.SetTexture("_TerrainAtlas0", CreateSolidTexture(Color.white, 32));
        var type = (MinesServer.Data.CellType)1;
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(3, 3);
        _cellData.SetType(type, TerrainCellData.PackType(new TerrainTypeFields(
            Type: type,
            Block: new BlockDefinition(
                Passable: false,
                MovementCooldownMs: 100,
                DrawLayer: CellDrawLayer.Foreground,
                Outline: CellOutline.Rigid,
                TextureName: "",
                TextureAnchor: CellTextureAnchor.Cell,
                TileGroup: null,
                DecalTextureName: null,
                ReliefGroup: 1,
                AnimationType: CellAnimationType.None,
                AnimationPalette: 0,
                FrameAnimationSpeed: 0f,
                Emission: false,
                MapColor: default),
            Slot: 0, AtlasRect: new Vector4(0, 0, 1, 1), TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: true, OpaqueAny: true,
            HasTileGroup: false, TileGroupId: 0)));
        _cellData.SetCell(1, 1, TerrainCellData.PackCell(type));
        _cellData.MarkAllDirty();
        _cellData.Apply();
        _cellData.BindGlobals(1f, 0, 0, 3, 3, distortionEnabled: false);
        using var carrier = new TerrainCellIdMesh();
        carrier.EnsureSize(3, 3, 1f);
        RenderTexture target = Own(new RenderTexture(96, 96, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear));
        Assert.That(target.Create(), Is.True);
        Texture2D readback = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            96, 96, "ReducedContactRadius", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));
        var manager = new TerrainMeshManager();
        using (var commands = new CommandBuffer())
        {
            TerrainSurfaceShaderGlobals.ApplyShaderGlobals();
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(false, true, Color.clear);
            manager.RenderLightingAmbientOcclusionField(commands, target, new Vector4(0, 0, 3, 3),
                Matrix4x4.identity, new[] { material }, carrier.Mesh, Vector4.zero);
            Graphics.ExecuteCommandBuffer(commands);
        }

        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 96, 96), 0, 0);
            readback.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
        }

        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath,
            "kern-ao-radius-060.png"), readback.EncodeToPNG());
        // Independent authored radius and cubic smoothstep oracle, in cell units.
        for (int x = 64; x < 96; x++)
        {
            float distance = (x + 0.5f) / 32f - 2f;
            float fraction = Mathf.Clamp01(distance / 0.60f);
            float expected = 1f - fraction * fraction * (3f - 2f * fraction);
            Assert.That(readback.GetPixel(x, 48).r, Is.EqualTo(expected).Within(2f / 255f),
                $"Contact radius mismatch at pixel {x}.");
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator ProductionEmission_PreservesSmallGreenDetailsBeforeFieldReduction()
    {
        Material material = Own(new Material(Shader.Find(TerrainShaderName)));
        Texture2D source = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            32, 32, "SparseGreenEmission", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color32[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                bool crystal = x % 16 >= 4 && x % 16 < 8 && y % 16 >= 4 && y % 16 < 8;
                pixels[y * 32 + x] = crystal ? new Color32(0, 255, 0, 255) : new Color32(0, 0, 32, 255);
            }
        }

        source.SetPixels32(pixels);
        source.Apply(false, false);
        material.SetTexture("_TerrainAtlas0", source);
        material.SetFloat("_EmissionBrightnessStart", 0.25f);
        material.SetFloat("_EmissionBrightnessEnd", 0.60f);
        material.SetFloat("_SurfaceEmissionStrength", 2f);
        TerrainTypeFields EmissionType(CellAnimationType effect) => new(
            Type: (MinesServer.Data.CellType)1,
            Block: new BlockDefinition(
                Passable: false,
                MovementCooldownMs: 100,
                DrawLayer: CellDrawLayer.Background,
                Outline: CellOutline.Pliant,
                TextureName: "",
                TextureAnchor: CellTextureAnchor.Cell,
                TileGroup: null,
                DecalTextureName: null,
                ReliefGroup: 0,
                AnimationType: effect,
                AnimationPalette: 0,
                FrameAnimationSpeed: 0f,
                Emission: true,
                MapColor: default),
            Slot: 0, AtlasRect: new Vector4(0, 0, 1, 1), TileSize: 1f,
            FrameCount: 1, FrameHeightTiles: 1f, OpaqueOwn: true, OpaqueAny: true,
            HasTileGroup: false, TileGroupId: 0);
        UploadSingleCell(EmissionType(CellAnimationType.Faceted), 0, 1);
        Mesh mesh = Own(CreateTerrainAnimationQuad());
        RenderTexture materialField = Own(new RenderTexture(2, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        RenderTexture glowField = Own(new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
        Assert.That(materialField.Create(), Is.True);
        Assert.That(glowField.Create(), Is.True);
        using (var commands = new CommandBuffer())
        {
            new TerrainMeshManager().RenderLightingMaterialFields(commands, materialField, glowField,
                new Vector4(0, 0, 2, 2), Matrix4x4.identity, new[] { material }, mesh, Vector4.zero);
            Graphics.ExecuteCommandBuffer(commands);
        }

        Texture2D fieldPixels = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(
            2, 2, "SparseGreenEmissionFieldReadback", RuntimeTextureColorSpace.Linear,
            FilterMode.Point, TextureWrapMode.Clamp));
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = glowField;
            fieldPixels.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
            fieldPixels.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previous;
        }

        foreach (Color sample in fieldPixels.GetPixels())
        {
            // Each 16x16 footprint contains sixteen authored green texels,
            // deliberately away from its centre. The blue host is below the
            // linear brightness threshold: averaging source color first fails.
            Assert.That(sample.g, Is.EqualTo(16f / 256f).Within(0.002f));
            Assert.That(sample.r, Is.LessThan(0.002f));
            Assert.That(sample.b, Is.LessThan(0.002f));
        }

        // All saturated primaries, RGB mixtures and white use the same mask;
        // no channel or material-name whitelist is permitted.
        Color32[] details =
        {
            new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255),
            new(255, 255, 0, 255), new(0, 255, 255, 255), new(255, 0, 255, 255),
            new(255, 255, 255, 255),
        };
        foreach (CellAnimationType effect in new[]
        {
            CellAnimationType.None, CellAnimationType.Molten, CellAnimationType.Faceted, CellAnimationType.Prismatic,
        })
        {
            _cellData.SetType((CellType)1, TerrainCellData.PackType(EmissionType(effect)));
            _cellData.Apply();
            foreach (Color32 detail in details)
            {
                FillDetails(detail);
                ReadEmissionField();
                foreach (Color sample in fieldPixels.GetPixels())
                {
                    Assert.That(sample.r, Is.EqualTo(detail.r / 255f / 16f).Within(0.002f));
                    Assert.That(sample.g, Is.EqualTo(detail.g / 255f / 16f).Within(0.002f));
                    Assert.That(sample.b, Is.EqualTo(detail.b / 255f / 16f).Within(0.002f));
                }
            }
        }
        _cellData.SetType((CellType)1, TerrainCellData.PackType(EmissionType(CellAnimationType.Faceted)));
        _cellData.Apply();
        FillDetails(new Color32(0, 255, 0, 255));

        void FillDetails(Color32 detail)
        {
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    bool crystal = x % 16 >= 4 && x % 16 < 8 && y % 16 >= 4 && y % 16 < 8;
                    pixels[y * 32 + x] = crystal ? detail : new Color32(0, 0, 32, 255);
                }
            }
            source.SetPixels32(pixels);
            source.Apply(false, false);
        }

        void ReadEmissionField()
        {
            using var commands = new CommandBuffer();
            new TerrainMeshManager().RenderLightingMaterialFields(commands, materialField, glowField,
                new Vector4(0, 0, 2, 2), Matrix4x4.identity, new[] { material }, mesh, Vector4.zero);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture saved = RenderTexture.active;
            try
            {
                RenderTexture.active = glowField;
                fieldPixels.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
                fieldPixels.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = saved;
            }
        }

        int scaleId = Shader.PropertyToID("_WorldGlowScale");
        float previousScale = Shader.GetGlobalFloat(scaleId);
        string[] globals = { "_WorldLightTexture", "_WorldAmbientOcclusionTexture" };
        Texture?[] previousTextures = { Shader.GetGlobalTexture(globals[0]), Shader.GetGlobalTexture(globals[1]) };
        Vector4 previousRect = Shader.GetGlobalVector("_WorldLightRect");
        Vector4 previousSize = Shader.GetGlobalVector("_WorldLightTextureSize");
        try
        {
            Shader.SetGlobalFloat(scaleId, 12f);
            Shader.SetGlobalTexture(globals[0], Texture2D.blackTexture);
            Shader.SetGlobalTexture(globals[1], Texture2D.blackTexture);
            Shader.SetGlobalVector("_WorldLightRect", new Vector4(0, 0, 2, 2));
            Shader.SetGlobalVector("_WorldLightTextureSize", Vector4.one);
            Shader.EnableKeyword("KERN_WORLD_LIGHTING");
            RenderTexture target = Own(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear));
            Assert.That(target.Create(), Is.True);
            _readback = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(
                1, 1, "EmissionCentre", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp));
            RenderAndReadCenter(mesh, material, material.FindPass("Universal2D"), target);
            Texture2D screenPixels = Own(RuntimeTextureFactory.CreateRGBAHalfNoMip(
                64, 64, "SparseGreenSelfEmission", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp));
            previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                screenPixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                screenPixels.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            // Source texel (6,6) lies inside the authored 4..7 detail patch.
            // The 2x camera scale maps its center to pixel (13,13).
            Color green = screenPixels.GetPixel(13, 13);
            Color blue = screenPixels.GetPixel(30, 30);
            Assert.That(green.g, Is.EqualTo(2f).Within(0.02f),
                "Visible emission must use its surface radiance, independently of transport gain 12.");
            Assert.That(green.b, Is.LessThan(0.01f));
            Assert.That(blue.maxColorComponent, Is.LessThan(0.01f), "The dark blue host must not self-emit.");
        }
        finally
        {
            Shader.SetGlobalFloat(scaleId, previousScale);
            Shader.SetGlobalTexture(globals[0], previousTextures[0]);
            Shader.SetGlobalTexture(globals[1], previousTextures[1]);
            Shader.SetGlobalVector("_WorldLightRect", previousRect);
            Shader.SetGlobalVector("_WorldLightTextureSize", previousSize);
            Shader.DisableKeyword("KERN_WORLD_LIGHTING");
        }

        yield return null;
    }

    // Одна клетка переднего плана типа 1 через рабочий формат и рабочую
    // выгрузку: строка типа, клетка (кайма вокруг пуста), глобальные адреса окна.
    // Тип 1 — квадрат на земле: передний план, без каймы и свечения.
    private static BlockDefinition Block(CellAnimationType animationType, float frameAnimationSpeed) => new(
        Passable: false,
        MovementCooldownMs: 100,
        DrawLayer: CellDrawLayer.Background,
        Outline: CellOutline.Pliant,
        TextureName: "",
        TextureAnchor: CellTextureAnchor.Cell,
        TileGroup: null,
        DecalTextureName: null,
        ReliefGroup: 0,
        AnimationType: animationType,
        AnimationPalette: 0,
        FrameAnimationSpeed: frameAnimationSpeed,
        Emission: false,
        MapColor: default);

    private void UploadSingleCell(TerrainTypeFields surface, int originX, int worldHeight)
    {
        var type = surface.Type;
        _cellData = new TerrainCellBuffers();
        _cellData.EnsureCapacity(1, 1);
        _cellData.SetType(type, TerrainCellData.PackType(surface));
        _cellData.SetCell(
            originX,
            0,
            TerrainCellData.PackCell(type));
        _cellData.MarkAllDirty();
        _cellData.Apply();
        _cellData.BindGlobals(cellSize: 2f, originX, originY: 0, worldWidth: originX + 1, worldHeight, distortionEnabled: false);
        Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, Vector4.zero);
    }

    private Texture2D CreateTwoFrameAtlas()
    {
        var atlas = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            64, 64, "TerrainAnimationRegressionAtlas", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color32[64 * 64];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = index < 32 * 64
                ? new Color32(255, 0, 0, 255)
                : new Color32(0, 0, 255, 255);
        }

        atlas.SetPixels32(pixels);
        atlas.Apply(false, false);
        return atlas;
    }

    private Texture2D CreateSolidTexture(Color color, int size = 1)
    {
        var texture = Own(RuntimeTextureFactory.CreateRGBA32NoMip(
            size, size, "TerrainAnimationSolid", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp));
        var pixels = new Color[size * size];
        System.Array.Fill(pixels, color);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private Mesh CreateTerrainAnimationQuad()
    {
        var mesh = new Mesh { name = "TerrainAnimationRegressionMesh" };
        mesh.SetVertexBufferParams(4, TerrainVolumeMesh.VertexLayout);
        mesh.SetVertexBufferData(new[]
        {
            new TerrainVolumeVertex(0, 0, 0, 0, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(0, 0, 0, 1, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(0, 0, 0, 2, Vector2.zero, Vector2.zero),
            new TerrainVolumeVertex(0, 0, 0, 3, Vector2.zero, Vector2.zero),
        }, 0, 0, 4);
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, calculateBounds: false);
        // POSITION carries half-precision cell addresses, not Float3 geometry.
        mesh.bounds = new Bounds(new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 1f));
        return mesh;
    }

    private Color RenderAndReadCenter(Mesh mesh, Material material, int pass, RenderTexture target)
    {
        if (_renderCamera == null)
        {
            GameObject cameraObject = Own(new GameObject("Terrain animation production camera"));
            _renderCamera = cameraObject.AddComponent<Camera>();
            _renderCamera.enabled = false;
            _renderCamera.orthographic = true;
            _renderCamera.orthographicSize = 1f;
            _renderCamera.aspect = 1f;
            _renderCamera.transform.position = new Vector3(1f, 1f, -10f);
            _renderCamera.cullingMask = 1 << 31;
            _renderCamera.clearFlags = CameraClearFlags.SolidColor;
            _renderCamera.backgroundColor = Color.black;
            _renderCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            GameObject meshObject = Own(new GameObject("Terrain animation production mesh"));
            meshObject.layer = 31;
            _renderMesh = meshObject.AddComponent<MeshFilter>();
            _renderRenderer = meshObject.AddComponent<MeshRenderer>();
        }
        _renderMesh!.sharedMesh = mesh;
        _renderRenderer!.sharedMaterial = material;
        // GPU-generated positions cover the cell even though the carrier stores addresses.
        mesh.bounds = new Bounds(new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 1f));
        _renderCamera.targetTexture = target;
#if UNITY_EDITOR
        // Numerical readback requires the selected production variant, never
        // an asynchronous compiler placeholder on the first rendered frame.
        UnityEditor.ShaderUtil.CompilePass(material, pass, forceSync: true);
        Assert.That(UnityEditor.ShaderUtil.IsPassCompiled(material, pass), Is.True);
        Assert.That(UnityEditor.ShaderUtil.ShaderHasError(material.shader), Is.False);
#endif
        RenderPipeline.SubmitRenderRequest(_renderCamera, new UniversalRenderPipeline.SingleCameraRequest
        {
            destination = target,
        });

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

    private T Own<T>(T value)
        where T : Object
    {
        if (value is Material material && material.shader.name == TerrainShaderName)
        {
            // These fixtures isolate texture animation, atlas addressing and
            // field sampling. Raised geometry has its own MainGame capture test.
            material.SetFloat("_TerrainDepthPixels", 0f);
        }
        _ownedObjects.Add(value);
        return value;
    }
}
