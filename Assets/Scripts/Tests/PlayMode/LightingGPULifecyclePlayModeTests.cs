#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game;
using Kern.Player;
using Kern.Rendering;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// Жизненный цикл GPU-ресурсов освещения в настоящей игре: создание при входе
// в мир, пересоздание при смене качества без накопления текстур и полное
// освобождение при выходе. Нужна видеокарта с compute-шейдерами.
[TestFixture]
[Category("GPU")]
public sealed class LightingGPULifecyclePlayModeTests
{
    private const string TestDummyToken = "playmode-lighting-gpu-token";
    private const int WalkingProbeLightId = int.MinValue + 317;
    private static int _dynamicLightUpdateSequence;

    // Имена целей освещения из LightingResourceManager.CreateTexture.
    private static readonly HashSet<string> _LightingTargetNames =
    [
        "_LightingMaterialField",
        "_StaticEmissionField",
        "_RadianceDirect",
        "_RadianceDirectStatic",
        "_WorldLightTexture",
        "_LightingCellSolidMask",
        "_LightingSurfaceAirCache",
        "_DynamicLightTiles",
        "_DynamicRayDepth",
        "_LightingAmbientOcclusionField",
        "Lighting.ReanchorMaterial",
        "Lighting.ReanchorEmission",
    ];

    private FrameBenchmarkPlayModeTests.NativeBenchmarkResolution? _subcellResolution;
    private FrameBenchmarkPlayModeTests.DiagnosticCameraScope? _batchLightingCamera;
    private RenderTexture? _batchLightingTarget;
    private Robot[]? _subcellRobots;
    private bool[]? _subcellRobotEnabled;

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private GraphicsPreset _originalPreset;
    private LightingQualityTuning _originalTuning;
    private bool _originalVectorPolarReference;
    private LightingEngine? _walkingProbeLighting;
    private Camera? _walkingProbeCamera;
    private CameraFollow? _walkingProbeFollow;
    private Vector3 _walkingProbeCameraPosition;
    private float _walkingProbeOrthographicSize;
    private float? _walkingProbeAspect;
    private Rect? _walkingProbeViewport;
    private bool _walkingProbeFollowWasEnabled;
    private LightingEngine.DebugView _walkingProbeDebugView;
    private bool _walkingProbeLightRegistered;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Assume.That(SystemInfo.supportsComputeShaders, Is.True, "Lighting needs compute shader support.");
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _config = _bootstrap.Container.Resolve<IClientConfigManager>();
        _originalPreset = _config.Config.GraphicsPreset;
        _originalTuning = LightingQualityTuningController.Current;
        _originalVectorPolarReference = LightingComputeBinder.DiagnosticVectorPolarReference;
        if (TestContext.CurrentContext.Test.Name is nameof(SubcellSource_IntegratesContinuousEmitterBounds)
            or nameof(StationaryRobotSprite_KeepsReceivingDynamicWorldLighting)
            or nameof(CyclingPresets_RecreatesTargetsWithoutLeakingThem)
            or nameof(VisualTuning_RebuildsFieldsAndRetainsStationarySources)
            or nameof(StationarySource_ReanchorMatchesFreshTransport)
            or nameof(OutsideFieldSource_KeepsCompleteEmitter)
            or nameof(PolarDepth_EqualExtinctionPreservesColoredHdrRadiance))
        {
#if UNITY_EDITOR
            // An interactive Editor owns a Game View window. Batch mode uses
            // the native camera dimensions and has no window to resize.
            if (!Application.isBatchMode)
            {
                _subcellResolution = new FrameBenchmarkPlayModeTests.NativeBenchmarkResolution(512, 512);
            }
#endif
            // Load the scene with AO only, then enable transport at the
            // test's actual smaller coverage. Raster density stays 32.
            _config.SelectGraphicsPreset(GraphicsPreset.Standard);
        }
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        TerrainRenderer terrain = PlayModeHarness.RequireInGame<TerrainRenderer>();
        foreach (var record in PlayModeHarness.RequireInGame<LightingEngine>().Journal.GetRecent(64))
        {
            TestContext.WriteLine($"lightingFrame={record.FrameIndex}; triggers={record.Triggers}; " +
                $"stages={string.Join(",", record.ExecutedPasses.Take(record.ExecutedCount))}");
        }
        yield return PlayModeHarness.WaitUntil(() => terrain.IsReadyForGameplay,
            PlayModeHarness.WorldTimeoutSeconds, "Production terrain geometry and textures did not become ready.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        LightingQualityTuningController.Apply(_originalTuning);
        LightingComputeBinder.DiagnosticVectorPolarReference = _originalVectorPolarReference;
        LightingFrameExecutor.DiagnosticMaterialReadback = null;
        if (_subcellRobots != null)
        {
            for (int i = 0; i < _subcellRobots.Length; i++)
            {
                if (_subcellRobots[i] != null)
                {
                    _subcellRobots[i].enabled = _subcellRobotEnabled![i];
                }
            }
            _subcellRobots = null;
            _subcellRobotEnabled = null;
        }
        if (_batchLightingCamera != null)
        {
            _batchLightingCamera.Camera.enabled = false;
            _batchLightingCamera.Camera.targetTexture = null;
            _batchLightingCamera.Dispose();
            _batchLightingCamera = null;
        }
        if (_batchLightingTarget != null)
        {
            _batchLightingTarget.Release();
            Object.Destroy(_batchLightingTarget);
            _batchLightingTarget = null;
        }
        if (_walkingProbeLightRegistered && _walkingProbeLighting != null)
        {
            _walkingProbeLighting.RemoveDynamicLight(WalkingProbeLightId);
            _walkingProbeLightRegistered = false;
        }

        if (_walkingProbeLighting != null &&
            _walkingProbeLighting.IsInitialized &&
            _walkingProbeLighting.ActiveDebugView != _walkingProbeDebugView)
        {
            _walkingProbeLighting.SetDebugView(_walkingProbeDebugView);
        }

        if (_walkingProbeCamera != null)
        {
            _walkingProbeCamera.transform.position = _walkingProbeCameraPosition;
            _walkingProbeCamera.orthographicSize = _walkingProbeOrthographicSize;
            if (_walkingProbeViewport is Rect viewport)
            {
                _walkingProbeCamera.rect = viewport;
                _walkingProbeViewport = null;
            }
            if (_walkingProbeAspect is float aspect)
            {
                _walkingProbeCamera.aspect = aspect;
                _walkingProbeAspect = null;
            }
        }

        if (_walkingProbeFollow != null)
        {
            _walkingProbeFollow.enabled = _walkingProbeFollowWasEnabled;
        }

        GraphicsSettingsController? graphics = PlayModeHarness.ResolveInGame<GraphicsSettingsController>();
        if (graphics != null)
        {
            graphics.SelectPreset(_originalPreset);
        }
        else
        {
            _config.SelectGraphicsPreset(_originalPreset);
        }

        yield return PlayModeHarness.Shutdown();
        _subcellResolution?.Dispose();
        _subcellResolution = null;
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator EnteringWorld_CreatesPipelineThatUpdatesDynamicLighting()
    {
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        yield return SelectAndSettle(GraphicsPreset.Overdrive);

        Assert.That(lighting.IsInitialized, Is.True);
        Assert.That(lighting.IsGPUPipelineInitialized, Is.True);
        Assert.That(lighting.GPUResources.Output.Lightmap, Is.Not.Null);
        Assert.That(lighting.GPUResources.Output.Lightmap!.IsCreated(), Is.True);
        Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.True);

        yield return AssertUpdatesAfterDynamicLightChange(lighting, "Lighting did not update after a dynamic-light change.");
    }

    [UnityTest]
    public IEnumerator CyclingPresets_RecreatesTargetsWithoutLeakingThem()
    {
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = ProjectRuntimeContracts.Camera.MinimumOrthographicSize;
        yield return PlayModeHarness.Frames(5);
        GraphicsPreset[] presets =
        [
            GraphicsPreset.Standard,
            GraphicsPreset.Overdrive,
        ];

        for (int round = 0; round < 2; round++)
        {
            foreach (GraphicsPreset preset in presets)
            {
                yield return SelectAndSettle(preset);

                Dictionary<string, int> live = LiveLightingTargets();
                string duplicates = string.Join(", ", live.Where(entry => entry.Value > 1).Select(entry => $"{entry.Key}×{entry.Value}"));
                Assert.That(duplicates, Is.Empty, $"Preset {preset} (round {round}) left stale lighting targets alive.");
                RenderTexture ao = lighting.GPUResources.Geometry.AmbientOcclusion!;
                Assert.That(ao, Is.Not.Null);
                Vector4 aoRect = Shader.GetGlobalVector("_WorldLightRect");
                Assert.That(ao.width, Is.EqualTo(Mathf.RoundToInt(aoRect.z * 32)),
                    "AO must be calculated at 32 samples/cell, independently of transport limits.");
                Assert.That(ao.height, Is.EqualTo(Mathf.RoundToInt(aoRect.w * 32)));
                Assert.That(ao.filterMode, Is.EqualTo(FilterMode.Point));

                if (preset == GraphicsPreset.Standard)
                {
                    Assert.That(lighting.GPUResources.Geometry.AmbientOcclusion, Is.Not.Null);
                    Assert.That(lighting.GPUResources.Geometry.AmbientOcclusion!.IsCreated(), Is.True);
                    Assert.That(lighting.GPUResources.Geometry.Material, Is.Null);
                    Assert.That(lighting.GPUResources.Cascade.Atlas, Is.Null);
                    Assert.That(lighting.GPUResources.Output.Lightmap, Is.Null);
                }
                else if (lighting.IsGPUPipelineInitialized)
                {
                    foreach (RenderTexture field in new[]
                    {
                        lighting.GPUResources.Geometry.Material!,
                        lighting.GPUResources.Geometry.StaticEmission!,
                        lighting.GPUResources.Direct.Static!,
                        lighting.GPUResources.Direct.Dynamic!,
                        lighting.GPUResources.Output.Lightmap!,
                    })
                    {
                        Assert.That(field.width, Is.EqualTo(Mathf.RoundToInt(aoRect.z * 32)), field.name);
                        Assert.That(field.height, Is.EqualTo(Mathf.RoundToInt(aoRect.w * 32)), field.name);
                    }
                    yield return AssertUpdatesAfterDynamicLightChange(lighting, $"Lighting did not update after switching to {preset}.");
                }
                else
                {
                    Assert.That(live, Is.Empty, $"Preset {preset} disabled lighting but kept its targets.");
                    Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.False);
                }
            }
        }
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator StationarySource_ReanchorMatchesFreshTransport()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeDebugView = _walkingProbeLighting.ActiveDebugView;
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = 8f;
        yield return PlayModeHarness.Frames(30);
        _subcellRobots = Object.FindObjectsByType<Robot>();
        _subcellRobotEnabled = _subcellRobots.Select(robot => robot.enabled).ToArray();
        foreach (Robot robot in _subcellRobots) { robot.enabled = false; }
        LightingEngine lighting = _walkingProbeLighting;
        lighting.ClearDynamicLights();
        // The source stays at an exact subcell pose throughout. Receivers in
        // the moved camera coverage lie beyond the near DDA zone, exercising
        // the retained polar transport rather than only the direct near path.
        Vector3 origin = _walkingProbeCameraPosition;
        Vector2 source = new(origin.x + 24.375f, origin.y + 0.1875f);
        _walkingProbeLightRegistered = true;
        lighting.SetDynamicLight(WalkingProbeLightId, source, Color.red, 8f);
        // Exercise polar reuse on visible receivers close to the parked
        // source even when the real terrain there is an opaque wall. The
        // fixture names this near-zone setting explicitly; teardown restores it.
        LightingQualityTuning tuning = LightingQualityTuningController.Current;
        LightingQualityTuningController.Apply(new LightingQualityTuning(tuning.FieldPixelsPerCell,
            tuning.LightPixelsPerCell, tuning.CascadeProbePixelsPerCell, tuning.MaximumStaticCascadeDirections, 0.5f,
            tuning.DynamicAngularSampleCount, tuning.DynamicEmitterPointsPerAxis, tuning.DynamicPolarDirectionCount));
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        Vector4 previousRect = lighting.WorldRect;
        Vector4 originalFieldRect = previousRect;
        source = new Vector2(previousRect.x + previousRect.z - 8f + 0.375f,
            origin.y + 0.1875f);
        lighting.SetDynamicLight(WalkingProbeLightId, source, Color.red, 8f);
        yield return PlayModeHarness.Frames(3);
        int reanchors = 0;
        IFrameTelemetry telemetry = PlayModeHarness.RequireInGame<IFrameTelemetry>();
        float reanchoredCameraX = previousRect.x + previousRect.z - _walkingProbeCamera.orthographicSize + 1f;
        float[] cameraShifts = [1f, -1f, 1f, -1f];
        foreach (float shift in cameraShifts)
        {
            // Cross the original containment edge, then return. The fixed
            // source remains inside the overlap of both transport fields.
            // Advancing to each new field edge would eventually move the
            // source outside that field and test a different dependency.
            float cameraX = shift > 0f ? reanchoredCameraX : origin.x;
            _walkingProbeCamera.transform.position = new Vector3(cameraX, origin.y, origin.z);
            long polarWork = 0;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
                polarWork = System.Math.Max(polarWork, telemetry.LightingPolarRayWorkUnits);
            }
            Vector4 rect = lighting.WorldRect;
            Assert.That(rect, Is.Not.EqualTo(previousRect), "Fixture did not reanchor the lighting field.");
            previousRect = rect;
            reanchors++;
            Assert.That(polarWork, Is.GreaterThan(0),
                "Field-origin invalidation did not retrace the parked source's optical depth.");
            Assert.That(lighting.UploadedDynamicLightCount, Is.EqualTo(1),
                "A stationary source disappeared on a field reanchor.");
            RenderTexture dynamic = lighting.GPUResources.Direct.Dynamic!;
            const int PatchSize = 128;
            const int PatchWidth = 32;
            Vector3 receiver = _walkingProbeCamera.transform.position;
            // The right-hand strip lies beyond the original field boundary.
            // Its medium was previously outside-domain air; newly revealed
            // production terrain must participate in the source's rays now.
            float sampleX = shift > 0f ? originalFieldRect.x + originalFieldRect.z + 0.5f : receiver.x;
            int x = Mathf.FloorToInt((sampleX - rect.x) / rect.z * dynamic.width) - PatchWidth / 2;
            int y = Mathf.FloorToInt((receiver.y - rect.y) / rect.w * dynamic.height) - PatchSize / 2;
            ushort[] retained = [];
            ushort[] fresh = [];
            yield return ReadHalfRegion(dynamic, x, y, PatchWidth, PatchSize, values => retained = values);
            // Rebuild only this source's rays at the identical final pose and
            // intensity. This reference has no retained optical-depth state;
            // it uses the real material field, compute shader and publication.
            lighting.SetDynamicLight(WalkingProbeLightId, source, Color.red, 9f);
            yield return PlayModeHarness.Frames(2);
            lighting.SetDynamicLight(WalkingProbeLightId, source, Color.red, 8f);
            yield return PlayModeHarness.Frames(2);
            Assert.That(lighting.WorldRect, Is.EqualTo(rect), "Reference inputs moved during comparison.");
            yield return ReadHalfRegion(dynamic, x, y, PatchWidth, PatchSize, values => fresh = values);
            float error = 0f;
            float peak = 0f;
            for (int pixel = 0; pixel < retained.Length; pixel += 4)
            {
                float actual = Mathf.HalfToFloat(retained[pixel]);
                float expected = Mathf.HalfToFloat(fresh[pixel]);
                Assert.That(float.IsFinite(actual), Is.True);
                error = Mathf.Max(error, Mathf.Abs(actual - expected));
                peak = Mathf.Max(peak, expected);
                Assert.That(retained[pixel + 1], Is.Zero, "A red source changed RGB channels.");
                Assert.That(retained[pixel + 2], Is.Zero);
            }
            TestContext.WriteLine($"stationaryReanchor={shift}; rect={rect}; camera={receiver}; " +
                $"patch={x},{y}; receiverRect={lighting.DynamicReceiverRect}; comparedPixels={PatchWidth * PatchSize}; " +
                $"source={source}; peak={peak:R}; retainedVsFreshError={error:R}; polarWork={polarWork}; " +
                $"tuning={LightingQualityTuningController.Current}");
            if (shift > 0f)
            {
                Assert.That(peak, Is.GreaterThan(0f), "The reference observed no source radiance.");
            }
            Assert.That(error, Is.Zero,
                $"Stationary source used stale optical depth after reanchor {shift}: {error:R}.");
            TestContext.WriteLine($"stationaryReanchor={shift}; rect={rect}; comparedPixels={PatchWidth * PatchSize}; " +
                $"source={source}; peak={peak:R}; retainedVsFreshError={error:R}");
        }
        Assert.That(reanchors, Is.EqualTo(4));
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator PolarDepth_EqualExtinctionPreservesColoredHdrRadiance()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeDebugView = _walkingProbeLighting.ActiveDebugView;
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = 8f;
        yield return PlayModeHarness.Frames(5);
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        _subcellRobots = Object.FindObjectsByType<Robot>();
        _subcellRobotEnabled = _subcellRobots.Select(robot => robot.enabled).ToArray();
        foreach (Robot robot in _subcellRobots) { robot.enabled = false; }
        _walkingProbeLighting.ClearDynamicLights();
        LightingEngine lighting = _walkingProbeLighting;
        Vector2 source = (Vector2)_walkingProbeCamera.transform.position + new Vector2(0.375f, 0.1875f);
        Color sourceColor = new(1f, 0.5f, 0.25f, 1f);
        _walkingProbeLightRegistered = true;
        var outputs = new ushort[2][];
        RenderTexture material = lighting.GPUResources.Geometry.Material!;
        RenderTexture staticDirect = lighting.GPUResources.Direct.Static!;
        for (int reference = 0; reference < 2; reference++)
        {
            LightingComputeBinder.DiagnosticVectorPolarReference = reference != 0;
            lighting.SetDynamicLight(WalkingProbeLightId, source, sourceColor, 9f);
            yield return PlayModeHarness.Frames(2);
            lighting.SetDynamicLight(WalkingProbeLightId, source, sourceColor, 8f);
            yield return PlayModeHarness.Frames(2);
            RenderTexture polar = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Single(texture => texture.name == "_DynamicRayDepth");
            Assert.That(polar.format, Is.EqualTo(reference == 0
                ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBFloat));
            Assert.That(lighting.GPUResources.Geometry.Material, Is.SameAs(material));
            Assert.That(lighting.GPUResources.Direct.Static, Is.SameAs(staticDirect));
            RenderTexture direct = lighting.GPUResources.Direct.Dynamic!;
            Vector4 rect = lighting.WorldRect;
            int px = Mathf.FloorToInt((source.x - rect.x) / rect.z * direct.width);
            int py = Mathf.FloorToInt((source.y - rect.y) / rect.w * direct.height);
            int captured = reference;
            yield return ReadHalfRegion(direct, px - 224, py - 224, 448, 448,
                data => outputs[captured] = data);
            TestContext.WriteLine($"opticalDepth={polar.format}; polar={polar.width}x{polar.height}x{polar.volumeDepth}; " +
                $"sources={lighting.UploadedDynamicLightCount}; receiverPixels={448 * 448}");
        }
        float maximum = 0f;
        float error = 0f;
        int colored = 0;
        for (int index = 0; index < outputs[0].Length; index += 4)
        {
            float red = Mathf.HalfToFloat(outputs[0][index]);
            maximum = Mathf.Max(maximum, red);
            if (red > 0.1f)
            {
                colored++;
                Assert.That(Mathf.HalfToFloat(outputs[0][index + 1]), Is.EqualTo(red * 0.5f).Within(0.01f));
                Assert.That(Mathf.HalfToFloat(outputs[0][index + 2]), Is.EqualTo(red * 0.25f).Within(0.01f));
            }
            for (int channel = 0; channel < 3; channel++)
            {
                error = Mathf.Max(error, Mathf.Abs(Mathf.HalfToFloat(outputs[0][index + channel]) -
                    Mathf.HalfToFloat(outputs[1][index + channel])));
            }
        }
        Assert.That(maximum, Is.GreaterThan(1f), "HDR radiance was clipped or the production solve did not run.");
        Assert.That(colored, Is.GreaterThan(1024), "The distribution oracle observed too few lit receivers.");
        Assert.That(error, Is.Zero, "Scalar optical depth changed the real RGB lighting distribution.");
        TestContext.WriteLine($"scalarVsRgb: maximumRadiance={maximum:R}; coloredPixels={colored}; maximumError={error:R}");
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator OutsideFieldSource_KeepsCompleteEmitter()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = 8f;
        _subcellRobots = Object.FindObjectsByType<Robot>();
        _subcellRobotEnabled = _subcellRobots.Select(robot => robot.enabled).ToArray();
        foreach (Robot robot in _subcellRobots) { robot.enabled = false; }
        LightingEngine lighting = _walkingProbeLighting;
        lighting.ClearDynamicLights();
        yield return PlayModeHarness.Frames(5);

        // Actual production surface pass, mesh attributes and field projection,
        // with a known uniform-air scene. No probe shader or transport helper.
        LightingGeometryRegistry registry = PlayModeHarness.RequireInGame<LightingGeometryRegistry>();
        GameObject fixture = PlayModeHarness.RequireInGame<ISceneObjectFactory>().Create("OutsideEmitterProductionAir");
        Texture2D white = RuntimeTextureFactory.CreateRGBA32NoMip(1, 1, "OutsideEmitterAlbedo",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        white.SetPixel(0, 0, Color.white);
        white.Apply(false);
        var surface = new Material(Shader.Find("Kern/World Surface"));
        surface.EnableKeyword("KERN_SURFACE_TRANSIT");
        surface.SetTexture("_BaseMap", white);
        surface.SetVector("_BaseMapTileCount", Vector4.one);
        surface.SetVector("_WorldSize", Vector4.one);
        surface.SetColor("_EmissionColor", Color.black);
        surface.SetFloat("_EmissionStrength", 0f);
        surface.SetFloat("_Occupancy", 0f);
        var mesh = new Mesh
        {
            name = "OutsideEmitterProductionAirQuad",
            vertices = [new(-256f, -256f), new(256f, -256f), new(256f, 256f), new(-256f, 256f)],
            uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up],
            uv2 = [Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero],
            triangles = [0, 1, 2, 0, 2, 3],
        };
        fixture.transform.position = _walkingProbeCamera.transform.position;
        fixture.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = fixture.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = surface;
        renderer.sortingOrder = 300;
        var contributor = new UniformAirContributor(mesh, surface, fixture.transform);
        registry.Register(contributor);
        int[] additionalIds = [WalkingProbeLightId + 1, WalkingProbeLightId + 2, WalkingProbeLightId + 3];
        try
        {
            yield return SelectAndSettle(GraphicsPreset.Overdrive);
            Vector4 rect = lighting.WorldRect;
            RenderTexture direct = lighting.GPUResources.Direct.Dynamic!;
            float density = direct.width * lighting.CellSize / rect.z;
            Assert.That(density, Is.EqualTo(32f));
            int px = Mathf.FloorToInt((_walkingProbeCamera.transform.position.x - rect.x) / rect.z * direct.width);
            int py = Mathf.FloorToInt((_walkingProbeCamera.transform.position.y - rect.y) / rect.w * direct.height);
            Vector2 receiver = new(rect.x / lighting.CellSize + (px + 0.5f) / density,
                rect.y / lighting.CellSize + (py + 0.5f) / density);
            Color32[]? materialPixel = null;
            RenderTexture material = lighting.GPUResources.Geometry.Material!;
            int materialY = lighting.MaterialYFlip != 0 ? material.height - 1 - py : py;
            AsyncGPUReadback.Request(material, 0, px, 1, materialY, 1, 0, 1,
                request => { Assert.That(request.hasError, Is.False); materialPixel = request.GetData<Color32>().ToArray(); });
            yield return PlayModeHarness.WaitUntil(() => materialPixel != null, 10f, "Production material did not arrive.");
            Assert.That(materialPixel![0].r, Is.EqualTo(255));
            Assert.That(materialPixel[0].a, Is.Zero, "Known-air production mesh did not replace terrain occupancy.");
            Color extinction = LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;
            _walkingProbeLightRegistered = true;
            foreach (float offset in new[] { -0.75f, -0.25f, 0f, 0.25f, 0.75f })
            {
                Vector2 source = new(rect.x / lighting.CellSize + offset, receiver.y);
                lighting.SetDynamicLight(WalkingProbeLightId, source, new Color(1f, 0.5f, 0.25f), 8f);
                yield return PlayModeHarness.Frames(4);
                Assert.That(lighting.WorldRect, Is.EqualTo(rect));
                Assert.That(lighting.DroppedDynamicLightIDs, Has.No.Member(WalkingProbeLightId));
                ushort[] values = null!;
                yield return ReadHalfRegion(direct, px, py, 1, 1, data => values = data);
                for (int channel = 0; channel < 3; channel++)
                {
                    double expected = AirSquareRadiance(source.x, receiver.x, extinction[channel],
                        8.0 * LightingConfigHolder.EmissionScale * (channel == 0 ? 1.0 : channel == 1 ? 0.5 : 0.25));
                    float measured = Mathf.HalfToFloat(values[channel]);
                    Assert.That(expected, Is.GreaterThan(0.000001), "The oracle observed an invisible source.");
                    Assert.That(measured, Is.EqualTo(expected).Within(expected * 0.03 + 0.0000001),
                        $"Full emitter at field offset {offset}, channel {channel} changed with the transport boundary.");
                    TestContext.WriteLine($"outsideEmitter: offset={offset}; channel={channel}; measured={measured:R}; expected={expected:R}");
                }
            }
            lighting.RemoveDynamicLight(WalkingProbeLightId);
            _walkingProbeLightRegistered = false;
            Vector2[] positions =
            [
                receiver - new Vector2(1.25f, 0f),
                receiver - new Vector2(2.5f, 0f),
                receiver - new Vector2(4.25f, 0f),
            ];
            Color[] colors = [new(1f, 0.25f, 0.125f), new(0f, 0.5f, 1f), new(0.3f, 1f, 0f)];
            float[] intensities = [8f, 4f, 16f];
            for (int reference = 0; reference < 2; reference++)
            {
                LightingComputeBinder.DiagnosticVectorPolarReference = reference != 0;
                for (int source = 0; source < additionalIds.Length; source++)
                {
                    lighting.SetDynamicLight(additionalIds[source], positions[source], colors[source], intensities[source] + 1f);
                }
                yield return PlayModeHarness.Frames(2);
                for (int source = 0; source < additionalIds.Length; source++)
                {
                    lighting.SetDynamicLight(additionalIds[source], positions[source], colors[source], intensities[source]);
                }
                yield return PlayModeHarness.Frames(2);
                RenderTexture tiles = Resources.FindObjectsOfTypeAll<RenderTexture>()
                    .Single(texture => texture.name == "_DynamicLightTiles");
                Assert.That(tiles.format, Is.EqualTo(reference == 0 ? RenderTextureFormat.RFloat : RenderTextureFormat.ARGBHalf));
                Assert.That(tiles.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                Assert.That(tiles.volumeDepth, Is.EqualTo(4), "Three sources require four persistent cache slots.");
                Assert.That(tiles.width, Is.LessThanOrEqualTo(direct.width + 63));
                Assert.That(tiles.height, Is.LessThanOrEqualTo(direct.height + 63));
                Assert.That(lighting.UploadedDynamicLightCount, Is.EqualTo(3));
                Assert.That(direct, Is.SameAs(lighting.GPUResources.Direct.Dynamic));
                // Also reorder uploaded IDs while retaining the same physical
                // sources and cache slots: reachIndex must not mean list index.
                int comparisons = reference == 0 ? 2 : 1;
                for (int comparison = 0; comparison < comparisons; comparison++)
                {
                    if (comparison > 0)
                    {
                        lighting.RemoveDynamicLight(additionalIds[0]);
                        additionalIds[0] = WalkingProbeLightId + 4;
                        lighting.SetDynamicLight(additionalIds[0], positions[0], colors[0], intensities[0]);
                        yield return PlayModeHarness.Frames(4);
                    }
                    ushort[] values = null!;
                    yield return ReadHalfRegion(direct, px, py, 1, 1, data => values = data);
                    for (int channel = 0; channel < 3; channel++)
                    {
                        double expected = 0.0;
                        for (int source = 0; source < positions.Length; source++)
                        {
                            expected += AirSquareRadiance(positions[source].x, receiver.x, extinction[channel],
                                intensities[source] * LightingConfigHolder.EmissionScale * colors[source][channel]);
                        }
                        float measured = Mathf.HalfToFloat(values[channel]);
                        Assert.That(expected, Is.GreaterThan(1.0), "The multi-source oracle must exercise HDR radiance.");
                        Assert.That(measured, Is.EqualTo(expected).Within(expected * 0.002 + 0.00001),
                            "Neutral-medium cache changed the independent colored-source HDR integral.");
                        TestContext.WriteLine($"coloredSourceSum: format={tiles.format}; reordered={comparison > 0}; " +
                            $"channel={channel}; measured={measured:R}; expected={expected:R}");
                    }
                }
            }
        }
        finally
        {
            foreach (int id in additionalIds) { lighting.RemoveDynamicLight(id); }
            registry.Unregister(contributor);
            Object.Destroy(fixture);
            Object.Destroy(mesh);
            Object.Destroy(surface);
            Object.Destroy(white);
        }
    }

    [UnityTest]
    [Timeout(180_000)]
    public IEnumerator FieldRaster_KeepsAsymmetricSolidAtItsWorldTexelsAcrossRegionShifts()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeDebugView = _walkingProbeLighting.ActiveDebugView;
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = 8f;
        LightingEngine lighting = _walkingProbeLighting;
        lighting.ClearDynamicLights();
        yield return SelectAndSettle(GraphicsPreset.Overdrive);

        // One solid cell in the upper quarter of the lighting region. The field
        // is cleared to air first, so the oracle is the cell's world position
        // alone: a mirrored field shows air there and solid at the mirror row.
        Vector4 initialRect = lighting.WorldRect;
        float cellSize = lighting.CellSize;
        var wallCell = new Vector2Int(
            Mathf.FloorToInt((initialRect.x + initialRect.z * 0.5f) / cellSize) + 2,
            Mathf.FloorToInt((initialRect.y + initialRect.w * 0.75f) / cellSize));
        LightingGeometryRegistry registry = PlayModeHarness.RequireInGame<LightingGeometryRegistry>();
        GameObject fixture = PlayModeHarness.RequireInGame<ISceneObjectFactory>().Create("AsymmetricSolidCell");
        Texture2D white = RuntimeTextureFactory.CreateRGBA32NoMip(1, 1, "AsymmetricSolidAlbedo",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        white.SetPixel(0, 0, Color.white);
        white.Apply(false);
        var surface = new Material(Shader.Find("Kern/World Surface"));
        surface.EnableKeyword("KERN_SURFACE_TRANSIT");
        surface.SetTexture("_BaseMap", white);
        surface.SetVector("_BaseMapTileCount", Vector4.one);
        surface.SetVector("_WorldSize", Vector4.one);
        surface.SetColor("_EmissionColor", Color.black);
        surface.SetFloat("_EmissionStrength", 0f);
        surface.SetFloat("_Occupancy", 1f);
        var mesh = new Mesh
        {
            name = "AsymmetricSolidCellQuad",
            vertices = [Vector3.zero, new(cellSize, 0f), new(cellSize, cellSize), new(0f, cellSize)],
            uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up],
            uv2 = [Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero],
            triangles = [0, 1, 2, 0, 2, 3],
        };
        fixture.transform.position = new Vector3(wallCell.x * cellSize, wallCell.y * cellSize, 0f);
        var contributor = new SolidCellContributor(mesh, surface, fixture.transform);
        registry.Register(contributor);
        try
        {
            yield return PlayModeHarness.Frames(10);
            yield return AssertSolidCellAtWorldTexels(lighting, wallCell, "initial region");

            // Reanchor along X, then along Y (camera down keeps the cell in the
            // upper half). Each shift must move the cell's texels by exactly the
            // region delta; a mirrored raster moves them the opposite way in Y.
            foreach (Vector3 step in new[] { new Vector3(cellSize, 0f, 0f), new Vector3(0f, -cellSize, 0f) })
            {
                Vector4 before = lighting.WorldRect;
                int moved = 0;
                while (lighting.WorldRect == before)
                {
                    Assert.That(moved, Is.LessThan(48), $"Lighting region never reanchored for camera step {step}.");
                    _walkingProbeCamera.transform.position += step;
                    moved++;
                    yield return PlayModeHarness.Frames(3);
                }
                yield return PlayModeHarness.Frames(5);
                yield return AssertSolidCellAtWorldTexels(lighting, wallCell,
                    $"after {moved} camera steps of {step}: region {before} -> {lighting.WorldRect}");
            }
        }
        finally
        {
            registry.Unregister(contributor);
            Object.Destroy(fixture);
            Object.Destroy(mesh);
            Object.Destroy(surface);
            Object.Destroy(white);
        }
    }

    private static IEnumerator AssertSolidCellAtWorldTexels(LightingEngine lighting, Vector2Int wallCell, string stage)
    {
        Vector4 rect = lighting.WorldRect;
        float cellSize = lighting.CellSize;
        foreach ((RenderTexture field, string name) in new[]
        {
            (lighting.GPUResources.Geometry.Material!, "material"),
            (lighting.GPUResources.Geometry.AmbientOcclusion!, "ambient occlusion"),
        })
        {
            // Bottom-up texel of the cell centre, from world coordinates only.
            int x = Mathf.FloorToInt(((wallCell.x + 0.5f) * cellSize - rect.x) / rect.z * field.width);
            int y = Mathf.FloorToInt(((wallCell.y + 0.5f) * cellSize - rect.y) / rect.w * field.height);
            int mirroredY = field.height - 1 - y;
            Assert.That(x, Is.InRange(0, field.width - 1), $"{stage}: cell left the {name} field horizontally.");
            Assert.That(y, Is.InRange(field.height / 2 + 1, field.height - 1),
                $"{stage}: cell must stay in the upper half of the {name} field to be asymmetric.");
            Color32[]? wall = null;
            Color32[]? mirror = null;
            AsyncGPUReadback.Request(field, 0, x, 1, Kern.Core.Interfaces.WorldLighting.LightingFieldOrientation.MemoryRow(y, field.height), 1, 0, 1,
                request => { Assert.That(request.hasError, Is.False); wall = request.GetData<Color32>().ToArray(); });
            AsyncGPUReadback.Request(field, 0, x, 1, Kern.Core.Interfaces.WorldLighting.LightingFieldOrientation.MemoryRow(mirroredY, field.height), 1, 0, 1,
                request => { Assert.That(request.hasError, Is.False); mirror = request.GetData<Color32>().ToArray(); });
            yield return PlayModeHarness.WaitUntil(() => wall != null && mirror != null, 10f,
                $"{stage}: {name} field readback did not finish.");
            Assert.That(wall![0].a, Is.EqualTo(255), $"{stage}: the solid cell is missing at its world texel in the {name} field.");
            Assert.That(mirror![0].a, Is.Zero, $"{stage}: the {name} field holds the solid cell at its Y-mirrored texel.");
            TestContext.WriteLine($"fieldOrientation: stage={stage}; field={name}; rect={rect}; texel=({x},{y}); " +
                $"wall={wall[0].a}; mirror={mirror[0].a}");
        }
    }

    private sealed class SolidCellContributor(Mesh mesh, Material material, Transform transform)
        : Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        public ulong LightingGeometryRevision => 1;

        public void RenderMaterialEmissionFields(CommandBuffer commands,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext context)
        {
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.DrawMesh(mesh, transform.localToWorldMatrix, material, 0, material.FindPass("LightingMaterialField"));
        }

        public void RenderAmbientOcclusionField(CommandBuffer commands,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context)
        {
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.DrawMesh(mesh, transform.localToWorldMatrix, material,
                0, material.FindPass("LightingAmbientOcclusionField"));
        }
    }

    private static double AirSquareRadiance(double sourceX, double receiverX, double extinction, double radiance)
    {
        double separation = receiverX - sourceX;
        double halfAngle = System.Math.Atan(0.5 / (separation - 0.5));
        double sum = 0.0;
        for (int sample = 0; sample < 8; sample++)
        {
            double angle = (sample + 0.5) * 2.0 * halfAngle / 8.0 - halfAngle;
            double dx = System.Math.Cos(angle);
            double dy = System.Math.Abs(System.Math.Sin(angle));
            double entry = (separation - 0.5) / dx;
            double exit = System.Math.Min((separation + 0.5) / dx, 0.5 / dy);
            if (exit > entry)
            {
                double weight = extinction == 0.0 ? exit - entry :
                    (1.0 - System.Math.Exp(-extinction * (exit - entry))) / (1.0 - System.Math.Exp(-extinction));
                sum += System.Math.Exp(-extinction * entry) * radiance * weight;
            }
        }
        return sum * halfAngle / (8.0 * System.Math.PI);
    }

    private sealed class UniformAirContributor(Mesh mesh, Material material, Transform transform)
        : Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        public ulong LightingGeometryRevision => 1;

        public void RenderMaterialEmissionFields(CommandBuffer commands,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext context)
        {
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.DrawMesh(mesh, transform.localToWorldMatrix, material, 0, material.FindPass("LightingMaterialField"));
        }

        public void RenderAmbientOcclusionField(CommandBuffer commands,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context)
        {
        }
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator VisualTuning_RebuildsFieldsAndRetainsStationarySources()
    {
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        _walkingProbeLighting = lighting;
        _walkingProbeDebugView = lighting.ActiveDebugView;
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow.enabled;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = ProjectRuntimeContracts.Camera.MinimumOrthographicSize;
        yield return PlayModeHarness.Frames(5);
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        _subcellRobots = Object.FindObjectsByType<Robot>();
        _subcellRobotEnabled = _subcellRobots.Select(robot => robot.enabled).ToArray();
        foreach (Robot robot in _subcellRobots)
        {
            robot.enabled = false;
        }
        Vector2 source = _walkingProbeCamera.transform.position;
        lighting.SetDynamicLight(WalkingProbeLightId, source, Color.red, 8f);
        _walkingProbeLightRegistered = true;
        yield return PlayModeHarness.Frames(10);

        foreach (int density in new[] { 16, 8, 32 })
        {
            LightingQualityTuningController.Apply(new LightingQualityTuning(
                density, density, System.Math.Min(4, density), _originalTuning.MaximumStaticCascadeDirections,
                _originalTuning.DynamicNearCells, _originalTuning.DynamicAngularSampleCount,
                _originalTuning.DynamicEmitterPointsPerAxis, _originalTuning.DynamicPolarDirectionCount));
            yield return PlayModeHarness.Frames(5);
            Vector4 rect = lighting.WorldRect;
            Assert.That(lighting.EffectivePixelsPerCell, Is.EqualTo(density));
            Assert.That(lighting.EffectiveCascadeProbesPerCell, Is.EqualTo(System.Math.Min(4, density)),
                "Dense geometry silently reduced the authored static probe density.");
            var cascadeCosts = new List<CascadeCostSample>();
            lighting.CollectCascadeCosts(cascadeCosts);
            for (int level = 0; level < cascadeCosts.Count; level++)
            {
                int requestedDirections = System.Math.Min(4 << (level * 2),
                    _originalTuning.MaximumStaticCascadeDirections);
                Assert.That(cascadeCosts[level].DirectionCount, Is.EqualTo(requestedDirections),
                    $"Cascade {level} silently reduced authored angular quality.");
            }
            foreach (RenderTexture field in new[]
            {
                lighting.GPUResources.Geometry.Material!,
                lighting.GPUResources.Geometry.StaticEmission!,
                lighting.GPUResources.Direct.Static!,
                lighting.GPUResources.Direct.Dynamic!,
                lighting.GPUResources.Output.Lightmap!,
            })
            {
                Assert.That(field.width, Is.EqualTo(Mathf.RoundToInt(rect.z * density)), field.name);
                Assert.That(field.height, Is.EqualTo(Mathf.RoundToInt(rect.w * density)), field.name);
                Assert.That(field.IsCreated(), Is.True, field.name);
            }
            RenderTexture ao = lighting.GPUResources.Geometry.AmbientOcclusion!;
            Assert.That(ao.width, Is.EqualTo(Mathf.RoundToInt(rect.z * 32)));
            Assert.That(ao.height, Is.EqualTo(Mathf.RoundToInt(rect.w * 32)));
            Assert.That(lighting.UploadedDynamicLightCount, Is.GreaterThan(0));
            Assert.That(lighting.DroppedDynamicLightIDs, Has.No.Member(WalkingProbeLightId));
            Assert.That(LiveLightingTargets().Values.All(count => count == 1), Is.True);
            RenderTexture direct = lighting.GPUResources.Direct.Dynamic!;
            int px = Mathf.FloorToInt((source.x - rect.x) / rect.z * direct.width);
            int py = Mathf.FloorToInt((source.y - rect.y) / rect.w * direct.height);
            float radiance = 0f;
            yield return ReadHalfRegion(direct, px, py, 1, 1,
                data => radiance = Mathf.HalfToFloat(data[0]));
            Assert.That(radiance, Is.GreaterThan(1f), "Stationary source lost its HDR radiance after field resize.");
            TestContext.WriteLine($"tuningDensity={density}; field={direct.width}x{direct.height}; " +
                $"AO={ao.width}x{ao.height}; radiance={radiance:R}; sources={lighting.UploadedDynamicLightCount}");
        }

        RenderTexture retainedMaterial = lighting.GPUResources.Geometry.Material!;
        RenderTexture retainedStatic = lighting.GPUResources.Direct.Static!;
        LightingQualityTuning acceptedQuality = LightingQualityTuningController.Current;
        ulong acceptedRevision = LightingQualityTuningController.Revision;
        bool oversizedAccepted = lighting.TryApplyQualityTuning(new LightingQualityTuning(
            32, 32, 16, 64, acceptedQuality.DynamicNearCells, acceptedQuality.DynamicAngularSampleCount,
            acceptedQuality.DynamicEmitterPointsPerAxis, acceptedQuality.DynamicPolarDirectionCount), out string rejection);
        Assert.That(oversizedAccepted, Is.False, "An atlas request above the explicit capacity was accepted.");
        Assert.That(rejection, Does.Contain("Атлас"));
        Assert.That(LightingQualityTuningController.Current, Is.EqualTo(acceptedQuality));
        Assert.That(LightingQualityTuningController.Revision, Is.EqualTo(acceptedRevision));
        yield return PlayModeHarness.Frames(2);
        Assert.That(lighting.GPUResources.Geometry.Material, Is.SameAs(retainedMaterial));
        Assert.That(lighting.GPUResources.Direct.Static, Is.SameAs(retainedStatic));
        ulong solveBeforeDynamicQuality = lighting.SolveCount;
        LightingQualityTuning current = LightingQualityTuningController.Current;
        LightingQualityTuningController.Apply(new LightingQualityTuning(
            current.FieldPixelsPerCell, current.LightPixelsPerCell, current.CascadeProbePixelsPerCell,
            current.MaximumStaticCascadeDirections, 1f, 4, 2, 32));
        yield return PlayModeHarness.Frames(5);
        Assert.That(lighting.SolveCount, Is.GreaterThan(solveBeforeDynamicQuality));
        Assert.That(lighting.GPUResources.Geometry.Material, Is.SameAs(retainedMaterial));
        Assert.That(lighting.GPUResources.Direct.Static, Is.SameAs(retainedStatic));
        foreach (var record in lighting.Journal.GetRecent(64).Where(record => record.FrameIndex >= solveBeforeDynamicQuality))
        {
            Assert.That(record.ExecutedPasses.Take(record.ExecutedCount), Has.No.Member("CascadeTrace"));
        }
        RenderTexture polar = Resources.FindObjectsOfTypeAll<RenderTexture>()
            .Single(texture => texture.name == "_DynamicRayDepth");
        Assert.That(polar.width, Is.EqualTo(34));
        Assert.That(polar.volumeDepth, Is.EqualTo(Mathf.NextPowerOfTwo(lighting.UploadedDynamicLightCount) * 4),
            "Two points per axis require exactly four emitter layers per newly allocated source slot.");
        Assert.That(lighting.UploadedDynamicLightCount, Is.GreaterThan(0));
        Assert.That(lighting.DroppedDynamicLightIDs, Has.No.Member(WalkingProbeLightId));
        ulong stableSolve = lighting.SolveCount;
        LightingQualityTuningController.Apply(LightingQualityTuningController.Current);
        yield return PlayModeHarness.Frames(10);
        Assert.That(lighting.SolveCount, Is.EqualTo(stableSolve), "Unchanged quality resubmitted GPU work.");
        TestContext.WriteLine($"dynamicTuning=near1/samples4/emitters2/angles32; " +
            $"polar={polar.width}x{polar.height}x{polar.volumeDepth}; staticRetained=True; steadyResubmissions=0");
    }

    [UnityTest]
    public IEnumerator LeavingWorld_ReleasesEveryLightingTarget()
    {
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        Assert.That(LiveLightingTargets(), Is.Not.Empty);

        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu),
            PlayModeHarness.UITimeoutSeconds);
        yield return PlayModeHarness.Frames(3);

        Assert.That(LiveLightingTargets(), Is.Empty, "Lighting targets outlived the game scene.");
        Assert.That(Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword), Is.False);

        // Повторный вход создаёт ресурсы заново, а не находит старые.
        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainGame),
            PlayModeHarness.WorldTimeoutSeconds);
        yield return PlayModeHarness.Frames(30);
        Assert.That(LiveLightingTargets().Values.All(count => count == 1), Is.True);
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator StationaryRobotSprite_KeepsReceivingDynamicWorldLighting()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();

        ILocalPlayer player = PlayModeHarness.RequireInGame<ILocalPlayerState>().Current ??
            throw new AssertionException("The local robot was not spawned.");
        yield return PlayModeHarness.WaitUntil(
            () => player.TryGetComponent<Robot>(out Robot robot) && robot.IsVisualsLoaded,
            PlayModeHarness.WorldTimeoutSeconds,
            "The production robot sprite did not load.");

        Robot robot = player.GetComponent<Robot>();
        Assert.That(robot.IsVisualsLoaded, Is.True);
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow != null && _walkingProbeFollow.enabled;
        if (_walkingProbeFollow != null)
        {
            _walkingProbeFollow.enabled = false;
        }
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = ProjectRuntimeContracts.Camera.MinimumOrthographicSize;
        yield return PlayModeHarness.Frames(5);
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        _walkingProbeDebugView = _walkingProbeLighting.ActiveDebugView;
        _walkingProbeLighting.SetDebugView(LightingEngine.DebugView.DynamicDirect);

        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.WorldRect.z > 0f,
            PlayModeHarness.WorldTimeoutSeconds,
            "Production lighting did not publish a world field.");

        float cellSize = _walkingProbeLighting.CellSize;
        Vector3 robotPosition = player.transform.position;
        var robotProbe = new Vector3(robotPosition.x, robotPosition.y, 0f);
        _walkingProbeLighting.SetDynamicLight(
            WalkingProbeLightId,
            new Vector2(robotPosition.x / cellSize, robotPosition.y / cellSize),
            Color.white,
            8f);
        _walkingProbeLightRegistered = true;

        ulong solveCount = _walkingProbeLighting.SolveCount;
        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.SolveCount > solveCount,
            10f,
            "Production lighting did not solve the robot-attached probe source.");
        yield return PlayModeHarness.Frames(5);

        var initial = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, robotProbe, initial, radius: 5);
        Assert.That(initial.Average.r + initial.Average.g + initial.Average.b,
            Is.GreaterThan(0.05f),
            "The real World Entity robot sprite did not receive dynamic world lighting at rest.");

        yield return PlayModeHarness.Frames(30);
        Assert.That(player.transform.position, Is.EqualTo(robotPosition),
            "The local robot moved during the stationary-light fixture.");

        var stationary = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, robotProbe, stationary, radius: 5);
        Vector3 delta = new(
            Mathf.Abs(initial.Average.r - stationary.Average.r),
            Mathf.Abs(initial.Average.g - stationary.Average.g),
            Mathf.Abs(initial.Average.b - stationary.Average.b));
        Assert.That(delta.magnitude, Is.LessThanOrEqualTo(0.03f),
            "The production robot sprite lost or flickered its dynamic lighting while stationary. " +
            $"Position={robotPosition}, before={initial.Average}, after={stationary.Average}, delta={delta}.");

        // The dynamic debug field alone cannot prove that the actual composite
        // and the production World Entity material keep lighting a parked robot.
        _walkingProbeLighting.SetDebugView(LightingEngine.DebugView.FinalLighting);
        yield return PlayModeHarness.Frames(10);
        var finalInitial = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, robotProbe, finalInitial, radius: 5);
        Assert.That(finalInitial.Average.r + finalInitial.Average.g + finalInitial.Average.b,
            Is.GreaterThan(0.05f), "FinalLighting left the stationary production robot unlit.");
        yield return PlayModeHarness.Frames(30);
        Assert.That(player.transform.position, Is.EqualTo(robotPosition));
        Assert.That(_walkingProbeLighting.DroppedDynamicLightIDs, Has.No.Member(WalkingProbeLightId));
        var finalStationary = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, robotProbe, finalStationary, radius: 5);
        Vector3 finalDelta = new(
            Mathf.Abs(finalInitial.Average.r - finalStationary.Average.r),
            Mathf.Abs(finalInitial.Average.g - finalStationary.Average.g),
            Mathf.Abs(finalInitial.Average.b - finalStationary.Average.b));
        Assert.That(finalDelta.magnitude, Is.LessThanOrEqualTo(0.03f),
            $"FinalLighting changed the parked robot: before={finalInitial.Average}, " +
            $"after={finalStationary.Average}, delta={finalDelta}.");
    }

    [UnityTest]
    [Timeout(240_000)]
    public IEnumerator SubcellSource_IntegratesContinuousEmitterBounds()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        yield return SelectAndSettle(GraphicsPreset.Standard);
        _subcellRobots = Object.FindObjectsByType<Robot>();
        _subcellRobotEnabled = _subcellRobots.Select(robot => robot.enabled).ToArray();
        foreach (Robot robot in _subcellRobots)
        {
            robot.enabled = false;
        }
        _walkingProbeLighting.ClearDynamicLights();
        LightingFrameExecutor.DiagnosticMaterialReadback = (stage, request) =>
        {
            Assert.That(request.hasError, Is.False);
            var texels = request.GetData<Color32>();
            int colored = 0;
            int occupied = 0;
            foreach (Color32 texel in texels)
            {
                if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                if (texel.a != 0) { occupied++; }
            }
            TestContext.WriteLine($"productionCheckpoint={stage}; colored={colored}; occupied={occupied}");
        };
        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeFollow = PlayModeHarness.RequireInGame<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow != null && _walkingProbeFollow.enabled;
        if (_walkingProbeFollow != null)
        {
            _walkingProbeFollow.enabled = false;
        }
        _walkingProbeAspect = _walkingProbeCamera.aspect;
        _walkingProbeViewport = _walkingProbeCamera.rect;
        float displayAspect = _walkingProbeCamera.pixelWidth / (float)_walkingProbeCamera.pixelHeight;
        _walkingProbeCamera.rect = new Rect(0f, 0f, Mathf.Min(1f, 1f / displayAspect), Mathf.Min(1f, displayAspect));
        _walkingProbeCamera.aspect = 1f;
        _walkingProbeCamera.orthographicSize = 5f;
        if (Application.isBatchMode)
        {
            // Batch mode has no display camera output. Keep the same actual
            // Renderer2D active through the shared production-image camera.
            _batchLightingCamera = new FrameBenchmarkPlayModeTests.DiagnosticCameraScope(
                _walkingProbeCamera, PlayModeHarness.RequireInGame<ISceneObjectFactory>());
            _batchLightingTarget = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGBHalf)
            {
                name = "ProductionLightingBatchOutput",
                filterMode = FilterMode.Point,
            };
            Assert.That(_batchLightingTarget.Create(), Is.True);
            Camera batchCamera = _batchLightingCamera.Camera;
            batchCamera.rect = new Rect(0f, 0f, 1f, 1f);
            batchCamera.aspect = _walkingProbeCamera.aspect;
            batchCamera.targetTexture = _batchLightingTarget;
            batchCamera.enabled = true;
        }
        yield return PlayModeHarness.Frames(5);
        yield return SelectAndSettle(GraphicsPreset.Overdrive);
        LightingEngine lighting = _walkingProbeLighting;
        _walkingProbeDebugView = lighting.ActiveDebugView;
        lighting.SetDebugView(LightingEngine.DebugView.FinalLighting);
        yield return PlayModeHarness.Frames(30);
        RenderTexture material = lighting.GPUResources.Geometry.Material!;
        RenderTexture direct = lighting.GPUResources.Direct.Dynamic!;
        Vector4 rect = lighting.WorldRect;
        float pixelsPerCell = direct.width * lighting.CellSize / rect.z;
        int radius = Mathf.CeilToInt(pixelsPerCell) + 1;
        const int sampleSize = 1024;
        int sampleWidth = Mathf.Min(sampleSize, material.width);
        int sampleHeight = Mathf.Min(sampleSize, material.height);
        int sampleX = (material.width - sampleWidth) / 2;
        int sampleY = (material.height - sampleHeight) / 2;
        Color32[]? occupancy = null;
        AsyncGPUReadback.Request(material, 0, sampleX, sampleWidth, sampleY, sampleHeight, 0, 1,
            request =>
        {
            Assert.That(request.hasError, Is.False);
            occupancy = request.GetData<Color32>().ToArray();
        });
        yield return PlayModeHarness.WaitUntil(() => occupancy != null, 10f, "Material readback did not finish.");
        int px = -1;
        int py = -1;
        int stride = sampleWidth + 1;
        var solidPrefix = new int[stride * (sampleHeight + 1)];
        for (int y = 0; y < sampleHeight; y++)
        {
            int rowSolid = 0;
            for (int x = 0; x < sampleWidth; x++)
            {
                rowSolid += occupancy![y * sampleWidth + x].a != 0 ? 1 : 0;
                solidPrefix[(y + 1) * stride + x + 1] = solidPrefix[y * stride + x + 1] + rowSolid;
            }
        }
        for (int y = radius; y < sampleHeight - radius && px < 0; y++)
        {
            for (int x = radius; x < sampleWidth - radius && px < 0; x++)
            {
                int left = x - radius;
                int right = x + radius + 1;
                int bottom = y - radius;
                int top = y + radius + 1;
                bool air = solidPrefix[top * stride + right] - solidPrefix[top * stride + left] -
                    solidPrefix[bottom * stride + right] + solidPrefix[bottom * stride + left] == 0;
                if (air)
                {
                    px = sampleX + x;
                    int materialY = sampleY + y;
                    py = lighting.MaterialYFlip != 0 ? direct.height - 1 - materialY : materialY;
                }
            }
        }
        Assert.That(px, Is.GreaterThanOrEqualTo(0), "Production field contains no uniform air neighborhood.");
        float baseline = 0f;
        bool read = false;
        AsyncGPUReadback.Request(direct, 0, px, 1, py, 1, 0, 1, request =>
        {
            Assert.That(request.hasError, Is.False);
            baseline = Mathf.HalfToFloat(request.GetData<ushort>()[0]);
            read = true;
        });
        yield return PlayModeHarness.WaitUntil(() => read, 10f, "Direct readback did not finish.");
        Vector2 receiver = new(rect.x / lighting.CellSize + (px + 0.5f) / pixelsPerCell,
            rect.y / lighting.CellSize + (py + 0.5f) / pixelsPerCell);
        Vector3 airCameraPosition = new(receiver.x * lighting.CellSize,
            receiver.y * lighting.CellSize, _walkingProbeCamera.transform.position.z);
        _walkingProbeCamera.transform.position = airCameraPosition;
        if (_batchLightingCamera != null)
        {
            _batchLightingCamera.Camera.transform.position = airCameraPosition;
        }
        yield return PlayModeHarness.Frames(10);
        Assert.That(lighting.WorldRect, Is.EqualTo(rect), "The source fixture unexpectedly moved the transport region.");
        float[] offsets = [0f, 0.0625f, 0.125f, 0.25f, 0.375f];
        foreach (float offset in offsets)
        {
            ulong before = lighting.SolveCount;
            lighting.SetDynamicLight(WalkingProbeLightId, receiver + new Vector2(offset, 0f), Color.red, 1f);
            _walkingProbeLightRegistered = true;
            yield return PlayModeHarness.WaitUntil(() => lighting.SolveCount > before, 10f, "Subcell source did not solve.");
            yield return PlayModeHarness.Frames(2);
            Assert.That(lighting.GPUResources.Direct.Dynamic, Is.SameAs(direct));
            Assert.That(lighting.DroppedDynamicLightIDs, Has.No.Member(WalkingProbeLightId));
            RenderTexture polar = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Single(texture => texture.name == "_DynamicRayDepth");
            Assert.That(polar.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(polar.volumeDepth, Is.EqualTo(
                LightingQualityTuningController.DynamicEmitterPointsPerAxis * LightingQualityTuningController.DynamicEmitterPointsPerAxis));
            int minimumCornerRadius = Mathf.CeilToInt(Mathf.Sqrt(
                (float)direct.width * direct.width + (float)direct.height * direct.height) * 0.5f);
            Assert.That(polar.height, Is.GreaterThanOrEqualTo(minimumCornerRadius),
                "Stacking emitter rows shortened transport before the field corners.");
            TestContext.WriteLine($"polar={polar.width}x{polar.height}x{polar.volumeDepth}; " +
                $"minimumCornerRadius={minimumCornerRadius}; dimension={polar.dimension}");
            RenderTexture tiles = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Single(texture => texture.name == "_DynamicLightTiles");
            if (lighting.UploadedDynamicLightCount == 1)
            {
                Assert.That(tiles.width, Is.EqualTo(1),
                    "One source must retain radiance in the direct field without a duplicate full-field tile.");
                Assert.That(tiles.height, Is.EqualTo(1));
            }
            var gpuLights = new DynamicLightGpuData[lighting.UploadedDynamicLightCount];
            lighting.GPUResources.Direct.DynamicLightsBuffer!.GetData(gpuLights, 0, 0, gpuLights.Length);
            TestContext.WriteLine($"field={direct.width}x{direct.height}; rect={rect}; receiver={receiver}; pixel={px},{py}; baseline={baseline}; count={gpuLights.Length}");
            foreach (DynamicLightGpuData gpuLight in gpuLights)
            {
                if (gpuLight.ColorIntensity.y == 0f && gpuLight.ColorIntensity.z == 0f)
                {
                    TestContext.WriteLine($"probeGPU={gpuLight.PositionRadius}; color={gpuLight.ColorIntensity}");
                }
            }
            float measured = 0f;
            read = false;
            AsyncGPUReadback.Request(direct, 0, px, 1, py, 1, 0, 1, request =>
            {
                Assert.That(request.hasError, Is.False);
                measured = Mathf.HalfToFloat(request.GetData<ushort>()[0]) - baseline;
                read = true;
            });
            yield return PlayModeHarness.WaitUntil(() => read, 10f, "Subcell radiance readback did not finish.");
            read = false;
            AsyncGPUReadback.Request(direct, 0, px, 1, direct.height - 1 - py, 1, 0, 1,
                request =>
            {
                Assert.That(request.hasError, Is.False);
                TestContext.WriteLine($"oppositeY={Mathf.HalfToFloat(request.GetData<ushort>()[0])}");
                read = true;
            });
            yield return PlayModeHarness.WaitUntil(() => read, 10f, "Opposite row readback did not finish.");
            read = false;
            int patchSize = 64;
            AsyncGPUReadback.Request(direct, 0, px - patchSize / 2, patchSize, 0,
                direct.height, 0, 1, request =>
            {
                Assert.That(request.hasError, Is.False);
                var data = request.GetData<ushort>();
                float maximum = 0f;
                int maximumIndex = -1;
                for (int index = 0; index < patchSize * direct.height; index++)
                {
                    float red = Mathf.HalfToFloat(data[index * 4]);
                    if (red > maximum)
                    {
                        maximum = red;
                        maximumIndex = index;
                    }
                }
                TestContext.WriteLine($"localMaximum={maximum}; at={maximumIndex % patchSize},{maximumIndex / patchSize}");
                read = true;
            });
            yield return PlayModeHarness.WaitUntil(() => read, 10f, "Patch readback did not finish.");
            // Independent slab-intersection/Beer-Lambert oracle in cell units.
            // No production helper or rounded texel rectangle is used here.
            double expected = 0;
            int samples = LightingQualityTuningController.DynamicAngularSampleCount;
            for (int sample = 0; sample < samples; sample++)
            {
                double angle = -System.Math.PI + (sample + 0.5) * 2 * System.Math.PI / samples;
                double dx = System.Math.Cos(angle);
                double dy = System.Math.Sin(angle);
                double exitX = ((dx > 0 ? offset + 0.5 : offset - 0.5) / dx);
                double exitY = 0.5 / System.Math.Abs(dy);
                double distance = System.Math.Min(exitX, exitY);
                double extinction = LightingConfigHolder.EmptyExtinctionMultiplier;
                expected += LightingConfigHolder.EmissionScale *
                    (1 - System.Math.Exp(-extinction * distance)) / (1 - System.Math.Exp(-extinction)) / samples;
            }
            TestContext.WriteLine($"offset={offset:R}; measured={measured:R}; expected={expected:R}; " +
                $"camera={_walkingProbeCamera.transform.position}; receiverRect={lighting.DynamicReceiverRect}");
            Assert.That(lighting.WorldRect, Is.EqualTo(rect));
            Assert.That(measured, Is.EqualTo(expected).Within(0.02), "Source snapped to transport texels.");
            // The retained result must survive settling at this subcell phase.
            yield return PlayModeHarness.Frames(10);
            Assert.That(lighting.GPUResources.Direct.Dynamic, Is.SameAs(direct),
                "Stable frames replaced the dynamic lighting target.");
            read = false;
            float retained = 0f;
            AsyncGPUReadback.Request(direct, 0, px, 1, py, 1, 0, 1, request =>
            {
                Assert.That(request.hasError, Is.False);
                retained = Mathf.HalfToFloat(request.GetData<ushort>()[0]) - baseline;
                read = true;
            });
            yield return PlayModeHarness.WaitUntil(() => read, 10f, "Retained radiance readback did not finish.");
            Assert.That(retained, Is.EqualTo(expected).Within(0.02));
        }

        yield return AssertCompositeWallTexels(lighting);
        _walkingProbeCamera.transform.position = airCameraPosition;
        if (_batchLightingCamera != null)
        {
            _batchLightingCamera.Camera.transform.position = airCameraPosition;
        }
        yield return PlayModeHarness.Frames(5);

        // Exercise the real 1 -> 2 -> 1 source layouts, including the cached
        // single-source result. Pause robot updates only within this fixture;
        // their production renderers/materials and terrain remain present.
        Robot[] robots = Object.FindObjectsByType<Robot>();
        bool[] robotEnabled = robots.Select(robot => robot.enabled).ToArray();
        const int secondProbeId = WalkingProbeLightId - 1;
        try
        {
            foreach (Robot robot in robots)
            {
                robot.enabled = false;
            }
            lighting.ClearDynamicLights();
            lighting.SetDynamicLight(WalkingProbeLightId, receiver, Color.red, 1f);
            double centerExpected = 0;
            for (int sample = 0; sample < LightingQualityTuningController.DynamicAngularSampleCount; sample++)
            {
                double angle = (sample + 0.5) * 2 * System.Math.PI /
                    LightingQualityTuningController.DynamicAngularSampleCount;
                double distance = 0.5 / System.Math.Max(System.Math.Abs(System.Math.Cos(angle)),
                    System.Math.Abs(System.Math.Sin(angle)));
                double extinction = LightingConfigHolder.EmptyExtinctionMultiplier;
                centerExpected += LightingConfigHolder.EmissionScale *
                    (1 - System.Math.Exp(-extinction * distance)) /
                    (1 - System.Math.Exp(-extinction)) / LightingQualityTuningController.DynamicAngularSampleCount;
            }
            for (int phase = 0; phase < 3; phase++)
            {
                if (phase == 1)
                {
                    lighting.SetDynamicLight(secondProbeId, receiver, Color.red, 0.5f);
                }
                else if (phase == 2)
                {
                    lighting.RemoveDynamicLight(secondProbeId);
                }
                yield return PlayModeHarness.Frames(10);
                Assert.That(lighting.UploadedDynamicLightCount, Is.EqualTo(phase == 1 ? 2 : 1));
                RenderTexture tiles = Resources.FindObjectsOfTypeAll<RenderTexture>()
                    .Single(texture => texture.name == "_DynamicLightTiles");
                if (phase != 1)
                {
                    Assert.That(tiles.width, Is.EqualTo(1));
                    Assert.That(tiles.height, Is.EqualTo(1));
                }
                else
                {
                    // Ten visible cells at 32 texels, outward guard and the
                    // half-cell gather support fit one 384-square tile.
                    Assert.That(tiles.width, Is.EqualTo(384));
                    Assert.That(tiles.height, Is.EqualTo(384));
                    Assert.That(tiles.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
                    Assert.That(tiles.volumeDepth, Is.EqualTo(2));
                }
                read = false;
                float actual = 0f;
                AsyncGPUReadback.Request(direct, 0, px, 1, py, 1, 0, 1, request =>
                {
                    Assert.That(request.hasError, Is.False);
                    actual = Mathf.HalfToFloat(request.GetData<ushort>()[0]);
                    read = true;
                });
                yield return PlayModeHarness.WaitUntil(() => read, 10f, "Layout transition readback did not finish.");
                double expected = centerExpected * (phase == 1 ? 1.5 : 1.0);
                Assert.That(actual, Is.EqualTo(expected).Within(0.02),
                    "The retained direct result or additive composition changed during a source-count transition.");
                TestContext.WriteLine($"layoutPhase={phase}; sources={lighting.UploadedDynamicLightCount}; " +
                    $"tiles={tiles.width}x{tiles.height}; measured={actual:R}; expected={expected:R}");
            }

            // Move the actual viewport by one world texel without moving its
            // emitter or the stable field. Receiver coverage changes; cached
            // source rays and static transport must survive that change.
            IFrameTelemetry telemetry = PlayModeHarness.RequireInGame<IFrameTelemetry>();
            ulong beforeCameraMove = lighting.SolveCount;
            Vector4 retainedWorldRect = lighting.WorldRect;
            RectInt previousReceivers = lighting.DynamicReceiverRect;
            RenderTexture polar = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Single(texture => texture.name == "_DynamicRayDepth");
            _walkingProbeCamera.transform.position += new Vector3(lighting.CellSize / 32f, 0f, 0f);
            if (_batchLightingCamera != null)
            {
                _batchLightingCamera.Camera.transform.position = _walkingProbeCamera.transform.position;
            }
            int tracesBeforeMove = telemetry.LightingDynamicTraceCount;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
                Assert.That(telemetry.LightingPolarRayWorkUnits, Is.Zero,
                    "Camera movement retraced unchanged dynamic source rays.");
            }
            int receiverTraces = telemetry.LightingDynamicTraceCount - tracesBeforeMove;
            Assert.That(lighting.SolveCount, Is.GreaterThan(beforeCameraMove));
            Assert.That(lighting.WorldRect, Is.EqualTo(retainedWorldRect));
            Assert.That(lighting.DynamicReceiverRect, Is.Not.EqualTo(previousReceivers));
            Assert.That(receiverTraces, Is.GreaterThan(0));
            Assert.That(Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Single(texture => texture.name == "_DynamicRayDepth"), Is.SameAs(polar));
            foreach (var record in lighting.Journal.GetRecent(10).Where(record => record.FrameIndex >= beforeCameraMove))
            {
                Assert.That(record.Triggers.HasFlag(LightingInvalidationFlags.ReceiverCoverageChanged), Is.True);
                Assert.That(record.ExecutedPasses.Take(record.ExecutedCount), Has.No.Member("MaterialField"));
                Assert.That(record.ExecutedPasses.Take(record.ExecutedCount), Has.No.Member("CascadeTrace"));
                Assert.That(record.ExecutedPasses.Take(record.ExecutedCount), Has.No.Member("CascadeMerge"));
            }
            read = false;
            float cameraMoveRadiance = 0f;
            AsyncGPUReadback.Request(direct, 0, px, 1, py, 1, 0, 1, request =>
            {
                Assert.That(request.hasError, Is.False);
                cameraMoveRadiance = Mathf.HalfToFloat(request.GetData<ushort>()[0]);
                read = true;
            });
            yield return PlayModeHarness.WaitUntil(() => read, 10f, "Moved camera radiance readback did not finish.");
            Assert.That(cameraMoveRadiance, Is.EqualTo(centerExpected).Within(0.02));
            TestContext.WriteLine($"cameraTexelMove: receiverTraces={receiverTraces}; polarWork=0; " +
                $"radiance={cameraMoveRadiance:R}; expected={centerExpected:R}");
        }
        finally
        {
            lighting.RemoveDynamicLight(secondProbeId);
            for (int index = 0; index < robots.Length; index++)
            {
                if (robots[index] != null)
                {
                    robots[index].enabled = robotEnabled[index];
                }
            }
        }
    }

    [UnityTest]
    [Timeout(240_000)]
    public IEnumerator WalkingAcrossLightingRegionBoundary_KeepsDynamicLightAtSameWorldPosition()
    {
        _walkingProbeLighting = PlayModeHarness.RequireInGame<LightingEngine>();
        yield return SelectAndSettle(GraphicsPreset.Overdrive);

        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.IsGPUPipelineInitialized &&
                _walkingProbeLighting.WorldRect.z > 0f,
            PlayModeHarness.WorldTimeoutSeconds,
            "Production lighting did not publish a world field.");

        _walkingProbeCamera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _walkingProbeFollow = _walkingProbeCamera.GetComponent<CameraFollow>() ??
            PlayModeHarness.FindComponentInScene<CameraFollow>(
                PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame));
        Assert.That(_walkingProbeFollow, Is.Not.Null,
            "The production camera follow component is required for this movement regression.");

        _walkingProbeCameraPosition = _walkingProbeCamera.transform.position;
        _walkingProbeOrthographicSize = _walkingProbeCamera.orthographicSize;
        _walkingProbeFollowWasEnabled = _walkingProbeFollow!.enabled;
        _walkingProbeDebugView = _walkingProbeLighting.ActiveDebugView;
        _walkingProbeFollow.enabled = false;
        _walkingProbeCamera.orthographicSize = ProjectRuntimeContracts.Camera.MaximumOrthographicSize;
        yield return PlayModeHarness.Frames(20);
        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.WorldRect.z > 0f,
            PlayModeHarness.WorldTimeoutSeconds,
            "Lighting did not settle at the maximum zoom used by the movement fixture.");
        yield return PlayModeHarness.Frames(5);

        float cellSize = _walkingProbeLighting.CellSize;
        Vector4 originalRect = _walkingProbeLighting.WorldRect;
        Assert.That(originalRect.z, Is.GreaterThan(0f), "Lighting has no published world rectangle.");
        int visibleWidth = Mathf.CeilToInt(
            _walkingProbeCamera.orthographicSize * 2f * _walkingProbeCamera.aspect / cellSize);
        int visibleMinX = Mathf.FloorToInt(_walkingProbeCamera.transform.position.x / cellSize) -
            (visibleWidth / 2);
        int fieldMinX = Mathf.RoundToInt(originalRect.x / cellSize);
        int fieldWidth = Mathf.RoundToInt(originalRect.z / cellSize);
        int offsetInField = visibleMinX - fieldMinX;
        int cameraShiftCells = fieldWidth - (offsetInField + visibleWidth) + 1;
        int sharedViewWidth = visibleWidth - cameraShiftCells;
        Assert.That(cameraShiftCells, Is.GreaterThan(0),
            $"Fixture camera already reaches the lighting edge: rect={originalRect}, viewport={visibleMinX}+{visibleWidth}.");
        Assert.That(sharedViewWidth, Is.GreaterThanOrEqualTo(8),
            $"Need overlapping camera views to compare the same world point: shift={cameraShiftCells}, viewport={visibleWidth}.");

        int sharedMinX = visibleMinX + cameraShiftCells;
        float probeWorldX = (sharedMinX + (sharedViewWidth / 2f) + 0.5f) * cellSize;
        float probeWorldY = (Mathf.Floor(_walkingProbeCamera.transform.position.y / cellSize) + 0.5f) * cellSize;
        var worldProbe = new Vector3(probeWorldX, probeWorldY, 0f);

        _walkingProbeLighting.SetDebugView(LightingEngine.DebugView.DynamicDirect);
        _walkingProbeLighting.SetDynamicLight(
            WalkingProbeLightId,
            new Vector2(probeWorldX / cellSize, probeWorldY / cellSize),
            Color.white,
            8f);
        _walkingProbeLightRegistered = true;
        ulong initialSolveCount = _walkingProbeLighting.SolveCount;
        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.SolveCount > initialSolveCount,
            10f,
            "Production lighting did not render the fixed probe light.");
        yield return PlayModeHarness.Frames(5);

        var beforeMove = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, worldProbe, beforeMove);
        Assert.That(beforeMove.Average.r + beforeMove.Average.g + beforeMove.Average.b,
            Is.GreaterThan(0.05f),
            $"The fixed light was not visible in the real terrain pass before movement: {beforeMove.Average}.");

        // A stationary light must remain in the published world field even
        // though stable frames correctly issue no transport dispatches.
        yield return PlayModeHarness.Frames(30);
        var whileStationary = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, worldProbe, whileStationary);
        Vector3 stationaryDelta = new(
            Mathf.Abs(beforeMove.Average.r - whileStationary.Average.r),
            Mathf.Abs(beforeMove.Average.g - whileStationary.Average.g),
            Mathf.Abs(beforeMove.Average.b - whileStationary.Average.b));
        Assert.That(stationaryDelta.magnitude, Is.LessThanOrEqualTo(0.03f),
            "A stationary world light faded or flickered at the same fixed world point. " +
            $"Probe={worldProbe}, before={beforeMove.Average}, after={whileStationary.Average}, " +
            $"delta={stationaryDelta}.");

        Vector3 movedCameraPosition = _walkingProbeCamera.transform.position +
            new Vector3(cameraShiftCells * cellSize, 0f, 0f);
        ulong solveBeforeMove = _walkingProbeLighting.SolveCount;
        _walkingProbeCamera.transform.position = movedCameraPosition;
        yield return PlayModeHarness.WaitUntil(
            () => _walkingProbeLighting.WorldRect.x != originalRect.x &&
                _walkingProbeLighting.SolveCount > solveBeforeMove,
            20f,
            $"Walking did not reanchor and solve lighting. Old rect={originalRect}, " +
            $"current rect={_walkingProbeLighting.WorldRect}, camera={movedCameraPosition}.");
        yield return PlayModeHarness.Frames(5);

        var afterMove = new ScreenProbe();
        yield return CaptureWorldProbe(_walkingProbeCamera, worldProbe, afterMove);
        Vector3 colorDelta = new(
            Mathf.Abs(beforeMove.Average.r - afterMove.Average.r),
            Mathf.Abs(beforeMove.Average.g - afterMove.Average.g),
            Mathf.Abs(beforeMove.Average.b - afterMove.Average.b));
        Assert.That(colorDelta.magnitude, Is.LessThanOrEqualTo(0.08f),
            "A stationary world light changed its rendered terrain pixels when the camera crossed " +
            $"the lighting-window boundary. Probe={worldProbe}, before={beforeMove.Average}, " +
            $"after={afterMove.Average}, delta={colorDelta}, oldRect={originalRect}, " +
            $"newRect={_walkingProbeLighting.WorldRect}.");
    }

    // Изменение источника света проходит через тот же путь обновления динамического поля,
    // который используется движущимися игровыми объектами.
    private static IEnumerator AssertUpdatesAfterDynamicLightChange(LightingEngine lighting, string failureMessage)
    {
        ulong solves = lighting.SolveCount;
        int sequence = ++_dynamicLightUpdateSequence;
        lighting.SetDynamicLight(
            -2048,
            new Vector2(10f + sequence, 10f),
            Color.white,
            1f);
        yield return PlayModeHarness.WaitUntil(() => lighting.SolveCount > solves, 5f, failureMessage);
    }

    private IEnumerator AssertCompositeWallTexels(LightingEngine lighting)
    {
        foreach (var record in lighting.Journal.GetRecent(64))
        {
            TestContext.WriteLine($"wallLightingFrame={record.FrameIndex}; triggers={record.Triggers}; " +
                $"stages={string.Join(",", record.ExecutedPasses.Take(record.ExecutedCount))}");
        }
        RenderTexture dynamicField = lighting.GPUResources.Direct.Dynamic!;
        RenderTexture materialField = lighting.GPUResources.Geometry.Material!;
        TerrainRenderer terrain = PlayModeHarness.RequireInGame<TerrainRenderer>();
        TestContext.WriteLine($"terrainPosition={terrain.transform.position}; " +
            $"grid={Shader.GetGlobalVector("_TerrainCellGridSize")}; " +
            $"origin={Shader.GetGlobalVector("_TerrainCellOrigin")}; worldRect={lighting.WorldRect}");
        foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>())
        {
            foreach (Material drawMaterial in renderer.sharedMaterials)
            {
                TestContext.WriteLine($"renderer={renderer.name}; material={drawMaterial.name}; " +
                    $"shader={drawMaterial.shader.name}; keywords={string.Join(",", drawMaterial.shaderKeywords)}");
                for (int slot = 0; slot < 8; slot++)
                {
                    string property = $"_TerrainAtlas{slot}";
                    Texture? atlas = drawMaterial.GetTexture(property);
                    TestContext.WriteLine($"{property}={atlas?.name}; size={atlas?.width}x{atlas?.height}; " +
                        $"texelSize={drawMaterial.GetVector(property + "_TexelSize")}");
                }
            }
        }
        Texture? meta = Shader.GetGlobalTexture("_TerrainCellMeta");
        Assert.That(meta, Is.Not.Null);
        bool metaRead = false;
        Vector2 nearestForeground = default;
        AsyncGPUReadback.Request(meta!, 0, request =>
        {
            Assert.That(request.hasError, Is.False);
            var texels = request.GetData<Color32>();
            int foregroundAtlas = 0;
            Vector4 origin = Shader.GetGlobalVector("_TerrainCellOrigin");
            Vector4 grid = Shader.GetGlobalVector("_TerrainCellGridSize");
            Vector3 cameraPosition = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera.transform.position;
            float nearestDistance = float.MaxValue;
            for (int row = 1; row < meta!.height; row += 2)
            {
                for (int x = 0; x < meta.width; x++)
                {
                    if (texels[row * meta.width + x].r > 0)
                    {
                        foregroundAtlas++;
                        int localX = (x - (int)origin.x) % (int)grid.x;
                        int localY = (row / 2 - (int)origin.y) % (int)grid.y;
                        if (localX < 0) { localX += (int)grid.x; }
                        if (localY < 0) { localY += (int)grid.y; }
                        Vector2 world = new((origin.x + localX + 0.5f) * grid.z,
                            (origin.y + localY + 0.5f) * grid.z);
                        float distance = ((Vector3)world - cameraPosition).sqrMagnitude;
                        if (distance < nearestDistance)
                        {
                            nearestForeground = world;
                            nearestDistance = distance;
                        }
                    }
                }
            }
            TestContext.WriteLine($"foregroundAtlasTexels={foregroundAtlas}; meta={meta.width}x{meta.height}");
            metaRead = true;
        });
        yield return PlayModeHarness.WaitUntil(() => metaRead, 10f, "Terrain mesh addressing readback did not finish.");
        var standaloneMaterial = new RenderTexture(materialField.descriptor);
        var standaloneEmission = new RenderTexture(lighting.GPUResources.Geometry.StaticEmission!.descriptor);
        try
        {
            Assert.That(standaloneMaterial.Create(), Is.True);
            Assert.That(standaloneEmission.Create(), Is.True);
            using var commands = new CommandBuffer { name = "ProductionFieldBindingDiagnosis" };
            Vector4 standaloneRect = new(Mathf.Floor(nearestForeground.x) - 1f,
                Mathf.Floor(nearestForeground.y) - 1f, 4f, 4f);
            terrain.RenderMaterialEmissionFields(commands,
                new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                    standaloneMaterial, standaloneEmission, standaloneRect));
            Graphics.ExecuteCommandBuffer(commands);
            bool standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                var texels = request.GetData<Color32>();
                int colored = 0;
                int solid = 0;
                foreach (Color32 texel in texels)
                {
                    if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                    if (texel.a != 0) { solid++; }
                }
                TestContext.WriteLine($"standaloneField={colored} colored, {solid} occupied; rect={standaloneRect}");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Standalone production field did not finish.");
            commands.Clear();
            terrain.RenderMaterialEmissionFields(commands,
                new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                    standaloneMaterial, standaloneEmission, lighting.WorldRect));
            Graphics.ExecuteCommandBuffer(commands);
            standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                var texels = request.GetData<Color32>();
                int colored = 0;
                int solid = 0;
                foreach (Color32 texel in texels)
                {
                    if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                    if (texel.a != 0) { solid++; }
                }
                TestContext.WriteLine($"standaloneFullCoverage={colored} colored, {solid} occupied; rect={lighting.WorldRect}");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Full coverage production field did not finish.");
            foreach (int stage in new[] { 1, 2, 0 })
            {
                commands.Clear();
                commands.SetGlobalInteger(Shader.PropertyToID("_KernLightingFieldDiagnosticStage"), stage);
                terrain.RenderMaterialEmissionFields(commands,
                    new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                        standaloneMaterial, standaloneEmission, lighting.WorldRect));
                commands.SetGlobalInteger(Shader.PropertyToID("_KernLightingFieldDiagnosticStage"), 0);
                Graphics.ExecuteCommandBuffer(commands);
                standaloneRead = false;
                AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
                {
                    Assert.That(request.hasError, Is.False);
                    var texels = request.GetData<Color32>();
                    int colored = 0;
                    int solid = 0;
                    foreach (Color32 texel in texels)
                    {
                        if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                        if (texel.a != 0) { solid++; }
                    }
                    TestContext.WriteLine($"fieldDiagnosticStage={stage}; colored={colored}; occupied={solid}");
                    standaloneRead = true;
                });
                yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Production field checkpoint did not finish.");
            }
            commands.Clear();
            commands.SetRenderTarget(standaloneMaterial);
            commands.ClearRenderTarget(false, true, Color.red);
            Graphics.ExecuteCommandBuffer(commands);
            standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                TestContext.WriteLine($"largeFieldClear={request.GetData<Color32>()[0]}");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Large field clear did not finish.");
            standaloneMaterial.Release();
            standaloneEmission.Release();
            standaloneMaterial.width = standaloneEmission.width = 128;
            standaloneMaterial.height = standaloneEmission.height = 128;
            Assert.That(standaloneMaterial.Create(), Is.True);
            Assert.That(standaloneEmission.Create(), Is.True);
            commands.Clear();
            commands.SetRenderTarget(standaloneMaterial);
            commands.ClearRenderTarget(false, true, Color.red);
            Graphics.ExecuteCommandBuffer(commands);
            standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                TestContext.WriteLine($"clearedFieldFirstPixel={request.GetData<Color32>()[0]}; descriptor={materialField.descriptor.graphicsFormat}; " +
                    $"msaa={materialField.descriptor.msaaSamples}; depth={materialField.descriptor.depthStencilFormat}; memoryless={materialField.descriptor.memoryless}");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Field clear did not finish.");
            commands.Clear();
            terrain.RenderMaterialEmissionFields(commands,
                new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                    standaloneMaterial, standaloneEmission, lighting.WorldRect));
            Graphics.ExecuteCommandBuffer(commands);
            standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                var texels = request.GetData<Color32>();
                int colored = 0;
                int solid = 0;
                foreach (Color32 texel in texels)
                {
                    if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                    if (texel.a != 0) { solid++; }
                }
                TestContext.WriteLine($"sameDescriptorAfterResize={colored} colored, {solid} occupied");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Resized production field did not finish.");
            commands.Clear();
            commands.SetGlobalInteger(Shader.PropertyToID("_KernLightingFieldDiagnosticStage"), 1);
            terrain.RenderMaterialEmissionFields(commands,
                new Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext(
                    standaloneMaterial, standaloneEmission, lighting.WorldRect));
            commands.SetGlobalInteger(Shader.PropertyToID("_KernLightingFieldDiagnosticStage"), 0);
            Graphics.ExecuteCommandBuffer(commands);
            standaloneRead = false;
            AsyncGPUReadback.Request(standaloneMaterial, 0, request =>
            {
                Assert.That(request.hasError, Is.False);
                var texels = request.GetData<Color32>();
                int colored = 0;
                foreach (Color32 texel in texels)
                {
                    if (texel.r != 0 || texel.g != 0 || texel.b != 0) { colored++; }
                }
                int green = 0;
                int red = 0;
                foreach (Color32 texel in texels)
                {
                    if (texel.g == 255 && texel.r == 0) { green++; }
                    if (texel.r == 255 && texel.g == 0) { red++; }
                }
                TestContext.WriteLine($"smallFieldDiagnosticStage1={colored}; green={green}; red={red}");
                standaloneRead = true;
            });
            yield return PlayModeHarness.WaitUntil(() => standaloneRead, 10f, "Resized diagnostic stage did not finish.");
        }
        finally
        {
            Shader.SetGlobalInteger("_KernLightingFieldDiagnosticStage", 0);
            standaloneMaterial.Release();
            standaloneEmission.Release();
            Object.Destroy(standaloneMaterial);
            Object.Destroy(standaloneEmission);
        }
        Color32[] wholeMaterial = null!;
        bool materialRead = false;
        AsyncGPUReadback.Request(materialField, 0, request =>
        {
            Assert.That(request.hasError, Is.False);
            wholeMaterial = request.GetData<Color32>().ToArray();
            materialRead = true;
        });
        yield return PlayModeHarness.WaitUntil(() => materialRead, 10f, "Wall silhouette readback did not finish.");
        var alphaHistogram = new int[256];
        int coloredTexels = 0;
        foreach (Color32 texel in wholeMaterial)
        {
            alphaHistogram[texel.a]++;
            if (texel.r != 0 || texel.g != 0 || texel.b != 0)
            {
                coloredTexels++;
            }
        }
        TestContext.WriteLine($"materialColoredTexels={coloredTexels}; alphaHistogram=" +
            string.Join(",", Enumerable.Range(0, 256).Where(value => alphaHistogram[value] > 0)
                .Select(value => $"{value}:{alphaHistogram[value]}")));
        const int width = 128;
        const int height = 128;
        (int X, int Y)[] directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        int wallX = -1;
        int wallY = -1;
        (int X, int Y) airDirection = default;
        long closest = long.MaxValue;
        // The air-source fixture is deliberately in an open area. Find the
        // nearest real wall in the committed terrain, rather than assuming its
        // center patch contains one or synthesizing a probe material texture.
        for (int y = height; y < materialField.height - height; y++)
        {
            for (int x = width; x < materialField.width - width; x++)
            {
                if (wholeMaterial[y * materialField.width + x].a / 255.0 < LightingConfigHolder.SolidOccupancyThreshold)
                {
                    continue;
                }
                long dx = x - materialField.width / 2;
                long dy = y - materialField.height / 2;
                long distance = dx * dx + dy * dy;
                if (distance >= closest)
                {
                    continue;
                }
                foreach ((int stepX, int stepY) in directions)
                {
                    if (wholeMaterial[(y + stepY) * materialField.width + x + stepX].a == 0)
                    {
                        closest = distance;
                        wallX = x;
                        wallY = y;
                        airDirection = (stepX, stepY);
                        break;
                    }
                }
            }
        }
        Assert.That(wallX, Is.GreaterThanOrEqualTo(0), "The production geometry contains no exposed wall.");
        int materialX = wallX - width / 2;
        int materialY = wallY - height / 2;
        Color32[] material = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            System.Array.Copy(wholeMaterial, (materialY + y) * materialField.width + materialX,
                material, y * width, width);
        }
        int fieldY = lighting.MaterialYFlip != 0 ? dynamicField.height - materialY - height : materialY;
        double cellsX = lighting.WorldRect.z / lighting.CellSize / dynamicField.width;
        double cellsY = lighting.WorldRect.w / lighting.CellSize / dynamicField.height;
        int wallFieldY = lighting.MaterialYFlip != 0 ? dynamicField.height - 1 - wallY : wallY;
        int airFieldY = lighting.MaterialYFlip != 0 ? -airDirection.Y : airDirection.Y;
        Vector2 wallSource = new(
            lighting.WorldRect.x / lighting.CellSize + (float)((wallX + 0.5) * cellsX) + airDirection.X * 0.75f,
            lighting.WorldRect.y / lighting.CellSize + (float)((wallFieldY + 0.5) * cellsY) + airFieldY * 0.75f);
        Vector3 wallCameraPosition = new(wallSource.x * lighting.CellSize,
            wallSource.y * lighting.CellSize, _walkingProbeCamera!.transform.position.z);
        _walkingProbeCamera.transform.position = wallCameraPosition;
        if (_batchLightingCamera != null)
        {
            _batchLightingCamera.Camera.transform.position = wallCameraPosition;
        }
        lighting.SetDynamicLight(WalkingProbeLightId, wallSource, Color.red, 8f);
        yield return PlayModeHarness.Frames(10);
        ushort[] dynamic = null!;
        ushort[] @static = null!;
        ushort[] composite = null!;
        yield return ReadHalfRegion(dynamicField, materialX, fieldY, width, height, data => dynamic = data);
        yield return ReadHalfRegion(lighting.GPUResources.Direct.Static!, materialX, fieldY,
            width, height, data => @static = data);
        yield return ReadHalfRegion(lighting.GPUResources.Output.Lightmap!, materialX, fieldY,
            width, height, data => composite = data);

        // Independent oracle: enumerate the actual terrain silhouette in cell
        // coordinates. Do not read SurfaceAirCache or call production helpers.
        double reach = LightingConfigHolder.SurfaceReflectionReachCells;
        int padding = Mathf.CeilToInt((float)(reach / System.Math.Min(cellsX, cellsY))) + 1;
        double[] extinction =
        [
            LightingConfigHolder.EmptyExtinctionRGB.r * LightingConfigHolder.EmptyExtinctionMultiplier,
            LightingConfigHolder.EmptyExtinctionRGB.g * LightingConfigHolder.EmptyExtinctionMultiplier,
            LightingConfigHolder.EmptyExtinctionRGB.b * LightingConfigHolder.EmptyExtinctionMultiplier,
        ];
        double[] ambient =
        [
            LightingConfigHolder.AmbientColor.r * LightingConfigHolder.AmbientIntensity,
            LightingConfigHolder.AmbientColor.g * LightingConfigHolder.AmbientIntensity,
            LightingConfigHolder.AmbientColor.b * LightingConfigHolder.AmbientIntensity,
        ];
        int MaterialIndex(int x, int y) =>
            (lighting.MaterialYFlip != 0 ? height - 1 - y : y) * width + x;
        double Incident(int x, int y, int channel)
        {
            int index = (y * width + x) * 4 + channel;
            return Mathf.HalfToFloat(dynamic[index]) + Mathf.HalfToFloat(@static[index]);
        }
        int checkedTexels = 0;
        int faceDominates = 0;
        double maximumError = 0;
        double maximumErrorRatio = 0;
        var observedDepths = new HashSet<int>();
        for (int y = padding; y < height - padding; y++)
        {
            for (int x = padding; x < width - padding; x++)
            {
                Color32 surface = material[MaterialIndex(x, y)];
                if (surface.a == 0)
                {
                    continue;
                }
                int[] faceSteps = new int[4];
                bool exposed = false;
                for (int direction = 0; direction < directions.Length; direction++)
                {
                    (int dx, int dy) = directions[direction];
                    double stepCells = dx != 0 ? cellsX : cellsY;
                    for (int step = 1; step <= System.Math.Ceiling(reach / stepCells); step++)
                    {
                        if (material[MaterialIndex(x + dx * step, y + dy * step)].a / 255.0 >=
                            LightingConfigHolder.SolidOccupancyThreshold)
                        {
                            continue;
                        }
                        faceSteps[direction] = step;
                        exposed = true;
                        break;
                    }
                }
                if (!exposed)
                {
                    continue;
                }
                byte[] albedo = [surface.r, surface.g, surface.b];
                for (int channel = 0; channel < 3; channel++)
                {
                    double center = Incident(x, y, channel);
                    double reflectedIncident = center;
                    for (int direction = 0; direction < directions.Length; direction++)
                    {
                        int step = faceSteps[direction];
                        if (step == 0)
                        {
                            continue;
                        }
                        (int dx, int dy) = directions[direction];
                        double distance = step * (dx != 0 ? cellsX : cellsY);
                        double t = System.Math.Min(distance / reach, 1.0);
                        double fade = 1 - 3 * t * t + 2 * t * t * t;
                        double face = Incident(x + dx * step, y + dy * step, channel) * fade *
                            System.Math.Exp(-extinction[channel] * distance);
                        if (face > center + 0.01)
                        {
                            faceDominates++;
                            observedDepths.Add(step);
                        }
                        reflectedIncident = System.Math.Max(reflectedIncident, face);
                    }
                    double expected = ambient[channel] + center +
                        surface.a / 255.0 * albedo[channel] / 255.0 * reflectedIncident;
                    double actual = Mathf.HalfToFloat(composite[(y * width + x) * 4 + channel]);
                    double error = System.Math.Abs(actual - expected);
                    maximumError = System.Math.Max(maximumError, error);
                    maximumErrorRatio = System.Math.Max(maximumErrorRatio,
                        error / (0.003 + System.Math.Abs(expected) * 0.003));
                }
                checkedTexels++;
            }
        }
        Assert.That(checkedTexels, Is.GreaterThanOrEqualTo(32), "The production terrain patch has no exposed wall profile.");
        Assert.That(faceDominates, Is.GreaterThanOrEqualTo(32), "Surface reflection did not contribute to the fixture.");
        Assert.That(observedDepths.Count, Is.GreaterThanOrEqualTo(4), "The wall oracle did not cover different texel depths.");
        Assert.That(maximumErrorRatio, Is.LessThanOrEqualTo(1),
            $"The production wall composite disagrees with its independent texel oracle: maximum error={maximumError:R}.");
        TestContext.WriteLine($"wallTexels={checkedTexels}; faceDominates={faceDominates}; " +
            $"depths={observedDepths.Count}; maximumCompositeError={maximumError:R}");
    }

    private static IEnumerator ReadHalfRegion(
        RenderTexture texture,
        int x,
        int y,
        int width,
        int height,
        System.Action<ushort[]> receive)
    {
        bool done = false;
        AsyncGPUReadback.Request(texture, 0, x, width, y, height, 0, 1, request =>
        {
            Assert.That(request.hasError, Is.False);
            receive(request.GetData<ushort>().ToArray());
            done = true;
        });
        yield return PlayModeHarness.WaitUntil(() => done, 10f, "Production lighting patch readback did not finish.");
    }

    private IEnumerator SelectAndSettle(GraphicsPreset preset)
    {
        PlayModeHarness.RequireInGame<GraphicsSettingsController>().SelectPreset(preset);

        // Destroy освобождает объекты в конце кадра, а ресурсы новой
        // конфигурации создаются в ближайшем обновлении освещения.
        yield return PlayModeHarness.Frames(10);
    }

    private sealed class ScreenProbe
    {
        public Color Average { get; set; }
    }

    private static IEnumerator CaptureWorldProbe(
        Camera camera,
        Vector3 worldPosition,
        ScreenProbe result,
        int radius = 2)
    {
        if (Application.isBatchMode)
        {
            yield return CaptureBatchWorldProbe(camera, worldPosition, result, radius);
            yield break;
        }
        yield return new WaitForEndOfFrame();
        Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Vector3 screenPosition = camera.WorldToScreenPoint(worldPosition);
            Assert.That(screenPosition.z, Is.GreaterThan(0f), "World probe is behind the production camera.");
            Assert.That(screenPosition.x, Is.InRange(radius + 1f, frame.width - radius - 1f),
                $"World probe moved outside the frame horizontally: {screenPosition} in {frame.width}x{frame.height}.");
            Assert.That(screenPosition.y, Is.InRange(radius + 1f, frame.height - radius - 1f),
                $"World probe moved outside the frame vertically: {screenPosition} in {frame.width}x{frame.height}.");

            Color32[] pixels = frame.GetPixels32();
            int centerX = Mathf.RoundToInt(screenPosition.x);
            int centerY = Mathf.RoundToInt(screenPosition.y);
            long red = 0;
            long green = 0;
            long blue = 0;
            int count = 0;
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    Color32 pixel = pixels[(y * frame.width) + x];
                    red += pixel.r;
                    green += pixel.g;
                    blue += pixel.b;
                    count++;
                }
            }

            result.Average = new Color(
                red / (255f * count),
                green / (255f * count),
                blue / (255f * count),
                1f);
        }
        finally
        {
            Object.Destroy(frame);
        }
    }

    private static IEnumerator CaptureBatchWorldProbe(
        Camera gameplayCamera,
        Vector3 worldPosition,
        ScreenProbe result,
        int radius)
    {
        Assert.That(gameplayCamera.pixelWidth, Is.GreaterThan(0));
        Assert.That(gameplayCamera.pixelHeight, Is.GreaterThan(0));
        using var diagnostic = new FrameBenchmarkPlayModeTests.DiagnosticCameraScope(
            gameplayCamera, PlayModeHarness.RequireInGame<ISceneObjectFactory>());
        Camera camera = diagnostic.Camera;
        var target = new RenderTexture(gameplayCamera.pixelWidth, gameplayCamera.pixelHeight,
            24, RenderTextureFormat.ARGBHalf)
        {
            name = "StationaryRobotProductionImage",
        };
        Assert.That(target.Create(), Is.True);
        int size = radius * 2 + 1;
        Texture2D crop = RuntimeTextureFactory.CreateRGBA32NoMip(size, size, "StationaryRobotImageCrop",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        try
        {
            camera.targetTexture = target;
            camera.enabled = true;
            yield return PlayModeHarness.Frames(3);
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            Assert.That(screen.z, Is.GreaterThan(0f));
            int x = Mathf.RoundToInt(screen.x) - radius;
            int y = Mathf.RoundToInt(screen.y) - radius;
            Assert.That(x, Is.InRange(0, target.width - size));
            Assert.That(y, Is.InRange(0, target.height - size));
            RenderTexture? previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                crop.ReadPixels(new Rect(x, y, size, size), 0, 0);
                crop.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
            Color sum = Color.clear;
            foreach (Color pixel in crop.GetPixels())
            {
                sum += pixel;
            }
            result.Average = sum / (size * size);
        }
        finally
        {
            camera.enabled = false;
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(crop);
        }
    }

    private static Dictionary<string, int> LiveLightingTargets() =>
        Resources.FindObjectsOfTypeAll<RenderTexture>()
            .Where(texture => texture != null && _LightingTargetNames.Contains(texture.name))
            .GroupBy(texture => texture.name)
            .ToDictionary(group => group.Key, group => group.Count());
}
