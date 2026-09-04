#nullable enable

using System;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Unified Minecraft-style F3 Debug Overlay built on UI Toolkit.
    /// Supports dynamic global PanelSettings / UI Scale with 0 runtime GC allocations.
    /// Left column: Client, FPS/Frametime, Coordinates, Facing Direction, World Dimensions, Target Block.
    /// Right column: Memory (Used/Allocated), GC Rates, Terrain Meshing/Cache, Radiance Cascades Lighting, Pipeline Passes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InGameDebugOverlay : MonoBehaviour
    {
        [Inject]
        private UIDocument _injectedDoc = null!;
        [Inject]
        private Fodinae.World.Lighting.LightingEngine _lighting = null!;
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

        [Header("Visualization Channels")]
        [SerializeField]
        private bool _showGrid = true;
        [SerializeField]
        private bool _showEntities = true;
        [SerializeField]
        private bool _showCursor = true;
        [SerializeField]
        private bool _showFrametimeGraph = true;

        private bool _isEnabled;
        private UIDocument? _doc;
        private VisualElement? _rootElement;
        private VisualElement? _columnsContainer;
        private Label? _leftLabel;
        private Label? _rightLabel;
        private VisualElement? _graphsRow;
        private VisualElement? _frametimeGraphCanvas;
        private Label? _frametimeGraphHeader;
        private VisualElement? _memoryGraphCanvas;
        private Label? _memoryGraphHeader;

        private readonly DebugTelemetryGraphsView _graphsView = new();
        private readonly StringBuilder _leftSb = new(1024);
        private readonly StringBuilder _rightSb = new(1024);
        private readonly StringBuilder _frametimeHeaderSb = new(128);
        private readonly StringBuilder _memoryHeaderSb = new(128);
        private float _nextUpdate;
        private float _nextGraphRepaint;

        private float _fpsTimer;
        private int _fpsFrames;
        private float _currentFps;
        private float _currentFrameMs;
        private float _minFrameMs = float.MaxValue;
        private float _maxFrameMs;
        private float _avgFrameMs;

        private ulong _lastSolveCount;
        private float _solvesPerSecond;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                UpdateVisibility();
            }
        }

        private void Awake()
        {
            float initialDelta = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 1f / 60f;
            _currentFps = 1f / initialDelta;
            _currentFrameMs = initialDelta * 1000f;
        }

        private void Start()
        {
            EnsureUI();
        }

        private void OnEnable()
        {
            EnsureUI();
            UpdateVisibility();
        }

        private void OnDisable()
        {
            if (_rootElement != null)
            {
                _rootElement.style.display = DisplayStyle.None;
            }
        }

        private void OnDestroy()
        {
            if (_rootElement != null && _doc != null && _doc.rootVisualElement != null)
            {
                _doc.rootVisualElement.Remove(_rootElement);
            }

            _rootElement = null;
            _columnsContainer = null;
            _leftLabel = null;
            _rightLabel = null;
            _graphsRow = null;
            _frametimeGraphCanvas = null;
            _frametimeGraphHeader = null;
            _memoryGraphCanvas = null;
            _memoryGraphHeader = null;
        }

        private void EnsureUI()
        {
            if (_rootElement != null)
            {
                return;
            }

            // [Inject]-поле заполняется при завершении сборки scope; до этого момента
            // EnsureUI ретраится из Update каждый кадр до готовности панели.
            _doc = _injectedDoc;
            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            _rootElement = new VisualElement
            {
                name = "minecraft-f3-debug-container",
                pickingMode = PickingMode.Ignore,
            };

            _rootElement.style.position = Position.Absolute;
            _rootElement.style.left = 10;
            _rootElement.style.top = 10;
            _rootElement.style.right = 10;
            _rootElement.style.bottom = 80; // Sit above the inventory hotbar (bottom: 16px + height ~56px)
            _rootElement.style.flexDirection = FlexDirection.Column;
            _rootElement.style.justifyContent = Justify.SpaceBetween;
            _rootElement.style.display = (_isEnabled || _showFrametimeGraph) ? DisplayStyle.Flex : DisplayStyle.None;

            _columnsContainer = new VisualElement
            {
                name = "f3-columns-row",
                pickingMode = PickingMode.Ignore,
            };
            _columnsContainer.style.flexDirection = FlexDirection.Row;
            _columnsContainer.style.justifyContent = Justify.SpaceBetween;
            _columnsContainer.style.alignItems = Align.FlexStart;
            _columnsContainer.style.width = Length.Percent(100);
            _columnsContainer.style.display = _isEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            _leftLabel = CreateDebugColumnLabel("f3-left-column", TextAnchor.UpperLeft);
            _leftLabel.style.marginLeft = 260; // Offset past PlayerStatusPanel (width 240px + margin)
            _rightLabel = CreateDebugColumnLabel("f3-right-column", TextAnchor.UpperRight);

            _columnsContainer.Add(_leftLabel);
            _columnsContainer.Add(_rightLabel);
            _rootElement.Add(_columnsContainer);

            _graphsRow = DebugTelemetryGraphsView.CreateGraphsRow(
                out _graphsRow,
                out _frametimeGraphHeader,
                out _frametimeGraphCanvas,
                out _memoryGraphHeader,
                out _memoryGraphCanvas,
                OnGenerateFrametimeGraphVisualContent,
                OnGenerateMemoryGraphVisualContent);
            _graphsRow.style.display = _showFrametimeGraph ? DisplayStyle.Flex : DisplayStyle.None;

            _rootElement.Add(_graphsRow);
            _doc.rootVisualElement.Add(_rootElement);
            _rootElement.BringToFront();
        }

        private void OnGenerateFrametimeGraphVisualContent(MeshGenerationContext context)
        {
            if (!_showFrametimeGraph || _frametimeGraphCanvas == null)
            {
                return;
            }

            _graphsView.GenerateFrametimeGraph(context, _frametimeGraphCanvas);
        }

        private void OnGenerateMemoryGraphVisualContent(MeshGenerationContext context)
        {
            if (!_showFrametimeGraph || _memoryGraphCanvas == null)
            {
                return;
            }

            _graphsView.GenerateMemoryGraph(context, _memoryGraphCanvas);
        }

        private static Label CreateDebugColumnLabel(string name, TextAnchor alignment)
        {
            var label = new Label
            {
                name = name,
                pickingMode = PickingMode.Ignore,
            };

            label.style.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            label.style.fontSize = 11;
            label.style.unityTextAlign = alignment;
            label.style.backgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.65f);
            label.style.borderTopLeftRadius = 2;
            label.style.borderTopRightRadius = 2;
            label.style.borderBottomLeftRadius = 2;
            label.style.borderBottomRightRadius = 2;
            label.style.paddingLeft = 6;
            label.style.paddingRight = 6;
            label.style.paddingTop = 4;
            label.style.paddingBottom = 4;
            label.style.flexShrink = 0;

            return label;
        }

        private void UpdateVisibility()
        {
            if (_rootElement != null)
            {
                bool showRoot = _isEnabled || _showFrametimeGraph;
                _rootElement.style.display = showRoot ? DisplayStyle.Flex : DisplayStyle.None;
                if (showRoot)
                {
                    _rootElement.BringToFront();
                }
            }

            if (_columnsContainer != null)
            {
                _columnsContainer.style.display = _isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_graphsRow != null)
            {
                _graphsRow.style.display = _showFrametimeGraph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_telemetry != null)
            {
                _telemetry.SetAllocationTrackingEnabled(_isEnabled || _showFrametimeGraph);
            }
        }

        private void Update()
        {
            if (Keyboard.current != null)
            {
                bool altPressed = Keyboard.current.altKey.isPressed;
                if (Keyboard.current.f3Key.wasPressedThisFrame)
                {
                    if (altPressed)
                    {
                        _showFrametimeGraph = !_showFrametimeGraph;
                        if (_graphsRow != null)
                        {
                            _graphsRow.style.display = _showFrametimeGraph ? DisplayStyle.Flex : DisplayStyle.None;
                        }
                    }
                    else
                    {
                        _isEnabled = !_isEnabled;
                        UpdateVisibility();
                    }
                }
            }

            bool active = _isEnabled || _showFrametimeGraph;
            if (!active)
            {
                return;
            }

            _telemetry.BeginFrame();
            HandleSubkeys();
            UpdateFps();

            if (_rootElement == null)
            {
                EnsureUI();
            }

            float now = Time.unscaledTime;
            if (_showFrametimeGraph && now >= _nextGraphRepaint)
            {
                _nextGraphRepaint = now + 0.033f;
                _frametimeGraphCanvas?.MarkDirtyRepaint();
                _memoryGraphCanvas?.MarkDirtyRepaint();
            }

            if (_isEnabled && now >= _nextUpdate && _leftLabel != null && _rightLabel != null)
            {
                _nextUpdate = now + 1.0f; // Update debug text columns at 1 Hz
                RefreshDebugContent();
            }
        }

        private void HandleSubkeys()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            var kb = Keyboard.current;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)
            {
                _showGrid = !_showGrid;
            }

            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)
            {
                _showEntities = !_showEntities;
            }

            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame)
            {
                _showCursor = !_showCursor;
            }

            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame || kb.f4Key.wasPressedThisFrame)
            {
                _debugSettings.BypassLightingCompute = !_debugSettings.BypassLightingCompute;
            }

            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame || kb.f6Key.wasPressedThisFrame)
            {
                _debugSettings.BypassTerrainDraw = !_debugSettings.BypassTerrainDraw;
            }

            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame || kb.f7Key.wasPressedThisFrame)
            {
                _debugSettings.BypassCpuMeshRebuild = !_debugSettings.BypassCpuMeshRebuild;
            }

            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame || kb.f8Key.wasPressedThisFrame)
            {
                if (_lighting != null)
                {
                    float current = _lighting.DynamicLightIntensity;
                    _lighting.SetDynamicLightSettings(current > 0.01f ? 0f : 1.25f, _lighting.DynamicLightColor);
                }
            }
        }

        private void UpdateFps()
        {
            float dt = Time.unscaledDeltaTime;
            float frameMs = dt * 1000f;
            float allocKb = _telemetry.GcAllocPerFrameBytes / 1024f;

            _graphsView.PushSample(frameMs, allocKb);

            _fpsFrames++;
            _fpsTimer += dt;
            if (_fpsTimer >= 0.25f)
            {
                _currentFps = _fpsFrames / _fpsTimer;
                _currentFrameMs = (_fpsTimer / _fpsFrames) * 1000f;

                _graphsView.ComputeAverages(_currentFrameMs, out _avgFrameMs, out _minFrameMs, out _maxFrameMs);

                if (_frametimeGraphHeader != null)
                {
                    _frametimeHeaderSb.Clear();
                    _frametimeHeaderSb.Append("Frametime: ").Append(_avgFrameMs.ToString("F1"))
                        .Append("ms (min ").Append(_minFrameMs.ToString("F1"))
                        .Append(" / max ").Append(_maxFrameMs.ToString("F1")).Append(") [Alt+F3]");
                    string ftText = _frametimeHeaderSb.ToString();
                    if (_frametimeGraphHeader.text != ftText)
                    {
                        _frametimeGraphHeader.text = ftText;
                    }
                }

                if (_memoryGraphHeader != null)
                {
                    _memoryHeaderSb.Clear();
                    _memoryHeaderSb.Append("GC Alloc: ").Append(allocKb.ToString("F1"))
                        .Append(" KB/f (").Append((_telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f)).ToString("F1"))
                        .Append(" MB/s)");
                    string memText = _memoryHeaderSb.ToString();
                    if (_memoryGraphHeader.text != memText)
                    {
                        _memoryGraphHeader.text = memText;
                    }
                }

                ulong solveCount = _lighting?.SolveCount ?? _lastSolveCount;
                _solvesPerSecond = (solveCount - _lastSolveCount) / _fpsTimer;
                _lastSolveCount = solveCount;

                _fpsFrames = 0;
                _fpsTimer = 0f;
            }
        }

        private void RefreshDebugContent()
        {
            DebugOverlayTextFormatter.FormatLeftColumn(
                _leftSb,
                _currentFps,
                _currentFrameMs,
                _localPlayer?.Current,
                _mapManager,
                _storage,
                _gameplayCamera);

            string leftText = _leftSb.ToString();
            if (_leftLabel!.text != leftText)
            {
                _leftLabel.text = leftText;
            }

            DebugOverlayTextFormatter.FormatRightColumn(
                _rightSb,
                _telemetry,
                _lighting,
                _debugSettings,
                _gameplayCamera,
                _solvesPerSecond);

            string rightText = _rightSb.ToString();
            if (_rightLabel!.text != rightText)
            {
                _rightLabel.text = rightText;
            }
        }

        private void OnDrawGizmos()
        {
            if (!_isEnabled || !Application.isPlaying)
            {
                return;
            }

            DebugOverlayGizmos.DrawWorldDebugGizmos(
                _showGrid,
                _showCursor,
                _mapManager,
                _storage,
                _localPlayer,
                _gameplayCamera);
        }
    }
}
