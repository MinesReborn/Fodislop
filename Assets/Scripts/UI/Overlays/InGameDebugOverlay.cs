#nullable enable

using UnityEngine.UIElements;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.Tools;
using Kern.Tools.Imgui;
using Kern.Tools.Imgui.Windows;
using Kern.World;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Kern.UI
{
    [DisallowMultipleComponent]
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
        private Kern.Game.WorldEntityBatchRenderer _entityRenderer = null!;
        [Inject]
        private UIDocument _gameUIDocument = null!;

        private readonly WorldGizmoOptions _gizmos = new();
        private readonly ToolWindow?[] _ownedWindows = new ToolWindow?[7];
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
            if (_telemetry == null || _debugSettings == null)
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

            var toolbar = new ToolbarWindow(_lighting);
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
                _gameUIDocument);
            var lightingCost = new LightingCostWindow(_lighting, _telemetry);
            var breakdown = new FrameBreakdownWindow();
            var packets = new PacketTrafficWindow();
            _bypassWindow = bypass;
            _ownedWindows[0] = toolbar;
            _ownedWindows[1] = stats;
            _ownedWindows[2] = world;
            _ownedWindows[3] = bypass;
            _ownedWindows[4] = lightingCost;
            _ownedWindows[5] = breakdown;
            _ownedWindows[6] = packets;

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
            if (keyboard != null &&
                !ToolWindows.HasKeyboardCapture &&
                keyboard.f1Key.wasPressedThisFrame)
            {
                SetToolsEnabled(!ToolWindows.Enabled);
                UpdateTelemetryState();
            }

            // Включить инструменты может не только F1 (F5 открывает грейдинг
            // напрямую), поэтому раскладка сверяется с реестром каждый кадр.
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
