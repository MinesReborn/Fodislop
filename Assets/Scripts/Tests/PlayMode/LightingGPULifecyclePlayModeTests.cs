#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Player;
using Kern.Rendering;
using Kern.World.Lighting;
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
    ];

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private GraphicsPreset _originalPreset;
    private LightingEngine? _walkingProbeLighting;
    private Camera? _walkingProbeCamera;
    private CameraFollow? _walkingProbeFollow;
    private Vector3 _walkingProbeCameraPosition;
    private float _walkingProbeOrthographicSize;
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
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
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
        }

        if (_walkingProbeFollow != null)
        {
            _walkingProbeFollow.enabled = _walkingProbeFollowWasEnabled;
        }

        GraphicsSettingsController? graphics = PlayModeHarness.ResolveInGame<GraphicsSettingsController>();
        graphics?.SelectPreset(_originalPreset);

        yield return PlayModeHarness.Shutdown();
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

                if (lighting.IsGPUPipelineInitialized)
                {
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
        yield return SelectAndSettle(GraphicsPreset.Overdrive);

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
        _walkingProbeFollow = _walkingProbeCamera.GetComponent<CameraFollow>();
        _walkingProbeFollowWasEnabled = _walkingProbeFollow != null && _walkingProbeFollow.enabled;
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

    private static Dictionary<string, int> LiveLightingTargets() =>
        Resources.FindObjectsOfTypeAll<RenderTexture>()
            .Where(texture => texture != null && _LightingTargetNames.Contains(texture.name))
            .GroupBy(texture => texture.name)
            .ToDictionary(group => group.Key, group => group.Count());
}
