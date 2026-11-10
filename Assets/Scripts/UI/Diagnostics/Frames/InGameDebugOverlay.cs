#nullable enable

using UnityEngine.UIElements;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

using Kern.Game.Rendering;
using Kern.Rendering.Diagnostics.Tools;
using Kern.Rendering.Diagnostics.Windows;
using Kern.Rendering.Diagnostics.World;
using Kern.Rendering.Display;

using Kern.Diagnostics.World;

namespace Kern.UI.Diagnostics
{
    [DisallowMultipleComponent]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "InGameDebugOverlay")]
    public sealed class InGameDebugOverlay : MonoBehaviour
    {
        [Inject]
        private LightingEngine _lighting = null!;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        [Inject]
        private SurfaceRenderer _surfaceRenderer = null!;
        [Inject]
        private Kern.Game.Rendering.WorldEntityBatchRenderer _entityRenderer = null!;
        [Inject]
        private UIDocument _gameUIDocument = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private Kern.Rendering.Display.DisplayManager _displayManager = null!;
        [Inject]
        private Kern.World.Terrain.TerrainRenderer _terrainRenderer = null!;

        private readonly WorldGizmoOptions _gizmos = new();
        private readonly ToolWindow?[] _ownedWindows = new ToolWindow?[10];
        private RenderBypassWindow? _bypassWindow;
        private bool _registered;

        public bool IsEnabled
        {
            get => ToolWindows.Enabled;
            set
            {
                EnsureWindows();
                SetToolsEnabled(value);
                UpdateTelemetryState();
            }
        }

        private void Awake()
        {
            // OnGUI живёт на этом компоненте всегда, а инструменты большую часть
            // времени скрыты. Без раскладки Unity не гоняет Layout-событие и не
            // строит кэш GUILayout на каждый кадр ради пустого вызова.
            useGUILayout = ToolWindows.Enabled;
        }

        private void OnEnable()
        {
            EnsureWindows();
            UpdateTelemetryState();
        }

        private void Start()
        {
            EnsureWindows();
        }

        private void OnDisable()
        {
            ToolWindows.SaveLayout(immediate: true);
            SetToolsEnabled(false);
            _telemetry?.SetAllocationTrackingEnabled(false);
        }

        private void OnDestroy()
        {
            ToolWindows.SaveLayout(immediate: true);
            SetToolsEnabled(false);
            _telemetry?.SetAllocationTrackingEnabled(false);
            foreach (ToolWindow? window in _ownedWindows)
            {
                if (window != null)
                {
                    ToolWindows.Unregister(window);
                    window.Dispose();
                }
            }

            _registered = false;
        }

        private void EnsureWindows()
        {
            if (_telemetry == null ||
                _debugSettings == null ||
                _clientConfig == null ||
                _displayManager == null ||
                _terrainRenderer == null)
            {
                return;
            }

            if (_registered)
            {
                foreach (ToolWindow? window in _ownedWindows)
                {
                    if (window != null && !ToolWindows.IsRegistered(window))
                    {
                        ToolWindows.Register(window);
                    }
                }

                return;
            }

            var toolbar = new ToolbarWindow(_lighting, _debugSettings);
            var stats = new FrameStatsWindow(_telemetry, _lighting);
            var world = new WorldInfoWindow(
                _telemetry,
                _lighting,
                _mapManager,
                _storage,
                _localPlayer,
                _gameplayCamera,
                _debugSettings,
                stats);
            var bypass = new RenderBypassWindow(
                _debugSettings,
                _lighting,
                _gizmos,
                _surfaceRenderer,
                _entityRenderer,
                _gameUIDocument,
                _clientConfig,
                _terrainRenderer);
            var lightingCost = new LightingCostWindow(_lighting, _telemetry);
            var breakdown = new FrameBreakdownWindow();
            var packets = new PacketTrafficWindow();
            var color = new ColorOutputWindow(_clientConfig, _displayManager, _lighting);
            var terrainDebug = new TerrainDebugWindow(_terrainRenderer);
            var logs = new LogWindow();
            _bypassWindow = bypass;
            _ownedWindows[0] = toolbar;
            _ownedWindows[1] = stats;
            _ownedWindows[2] = world;
            _ownedWindows[3] = bypass;
            _ownedWindows[4] = lightingCost;
            _ownedWindows[5] = breakdown;
            _ownedWindows[6] = packets;
            _ownedWindows[7] = color;
            _ownedWindows[8] = terrainDebug;
            _ownedWindows[9] = logs;

            foreach (ToolWindow? window in _ownedWindows)
            {
                if (window != null)
                {
                    ToolWindows.Register(window);
                }
            }

            _registered = true;
        }

        private void Update()
        {
            EnsureWindows();
            Keyboard? keyboard = Keyboard.current;
            if (Debug.isDebugBuild &&
                keyboard != null &&
                !ToolWindows.HasKeyboardCapture &&
                keyboard.f1Key.wasPressedThisFrame)
            {
                SetToolsEnabled(!ToolWindows.Enabled);
                UpdateTelemetryState();
            }

            // Polled before the early return below so the tail modes work with the tool suite
            // closed, same as F1.
            if (keyboard != null &&
                !ToolWindows.HasKeyboardCapture &&
                keyboard.f2Key.wasPressedThisFrame)
            {
                _debugSettings.TailMotionDebugMode = NextTailMotionDebugMode(
                    _debugSettings.TailMotionDebugMode);
            }

            // Раскладка сверяется с реестром каждый кадр: инструменты могут
            // включиться не только отсюда.
            useGUILayout = ToolWindows.Enabled;
            if (!ToolWindows.Enabled)
            {
                return;
            }

            ReleaseCaptureOnEscape(keyboard);
            UpdateTelemetryState();
            _telemetry.BeginFrame();
            ToolWindows.Tick();
        }

        /// <summary>
        ///     Cycles the tail motion debug modes. Kept as an explicit successor rather than an
        ///     unchecked increment so an out-of-range cast can never fall out of the cycle.
        /// </summary>
        private static TailMotionDebugMode NextTailMotionDebugMode(TailMotionDebugMode mode) => mode switch
        {
            TailMotionDebugMode.Normal => TailMotionDebugMode.Minimal,
            TailMotionDebugMode.Minimal => TailMotionDebugMode.Medium,
            TailMotionDebugMode.Medium => TailMotionDebugMode.Maximum,
            _ => TailMotionDebugMode.Normal,
        };

        private static void ReleaseCaptureOnEscape(Keyboard? keyboard)
        {
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame &&
                ToolWindows.HasKeyboardCapture)
            {
                ToolWindows.ReleaseInputCapture();
            }
        }

        private void UpdateTelemetryState()
        {
            _telemetry?.SetAllocationTrackingEnabled(ToolWindows.AnySampling);
        }

        private void SetToolsEnabled(bool enabled)
        {
            ToolWindows.Enabled = enabled;
            useGUILayout = enabled;
            if (enabled)
            {
                return;
            }

            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareMode = CompareMode.Off;
            PostProcessRuntimeState.CompareBefore = false;
        }

        private void OnGUI()
        {
            if (!_registered)
            {
                return;
            }

            // Реестр включили между Update и OnGUI этого кадра: окна используют
            // GUILayout, а раскладка на компоненте ещё выключена. Рисовать
            // начнём со следующего кадра, когда Update её включит.
            if (ToolWindows.Enabled && !useGUILayout)
            {
                useGUILayout = true;
                return;
            }

            ToolWindows.Draw();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !ToolWindows.Enabled)
            {
                return;
            }

            if (!_gizmos.ShowGrid && !_gizmos.ShowCursor)
            {
                return;
            }

            DebugOverlayGizmos.DrawWorldDebugGizmos(
                _gizmos.ShowGrid,
                _gizmos.ShowCursor,
                _mapManager,
                _storage,
                _localPlayer,
                _gameplayCamera);
        }
    }
}
