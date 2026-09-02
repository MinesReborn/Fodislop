#nullable enable

using System;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
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
        private VisualElement? _frametimeGraphContainer;
        private VisualElement? _frametimeGraphCanvas;
        private Label? _frametimeGraphHeader;
        private VisualElement? _memoryGraphContainer;
        private VisualElement? _memoryGraphCanvas;
        private Label? _memoryGraphHeader;

        private const int GraphHistoryCapacity = 160;
        private readonly float[] _frametimeHistory = new float[GraphHistoryCapacity];
        private readonly float[] _gcAllocHistory = new float[GraphHistoryCapacity];
        private int _historyIndex;
        private int _historyCount;

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
        private readonly System.Collections.Generic.List<Fodinae.World.Lighting.CascadeCostSample> _cascadeCosts = new(4);

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
            _frametimeGraphContainer = null;
            _frametimeGraphCanvas = null;
            _frametimeGraphHeader = null;
            _memoryGraphContainer = null;
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

            // ==================== REAL-TIME TELEMETRY GRAPHS ROW ====================
            _graphsRow = new VisualElement
            {
                name = "f3-graphs-row",
                pickingMode = PickingMode.Ignore,
            };
            _graphsRow.style.flexDirection = FlexDirection.Row;
            _graphsRow.style.justifyContent = Justify.FlexStart;
            _graphsRow.style.alignItems = Align.FlexEnd;
            _graphsRow.style.marginLeft = 260;
            _graphsRow.style.marginBottom = 6;
            _graphsRow.style.display = _showFrametimeGraph ? DisplayStyle.Flex : DisplayStyle.None;

            // 1. Frametime Graph
            _frametimeGraphContainer = CreateGraphCard("Frametime", 320, 70, out _frametimeGraphHeader, out _frametimeGraphCanvas, OnGenerateFrametimeGraphVisualContent);
            _frametimeGraphContainer.style.marginRight = 10;
            _graphsRow.Add(_frametimeGraphContainer);

            // 2. GC / Allocation Graph
            _memoryGraphContainer = CreateGraphCard("GC Allocation", 240, 70, out _memoryGraphHeader, out _memoryGraphCanvas, OnGenerateMemoryGraphVisualContent);
            _graphsRow.Add(_memoryGraphContainer);

            _rootElement.Add(_graphsRow);

            _doc.rootVisualElement.Add(_rootElement);
            _rootElement.BringToFront();
        }

        private static VisualElement CreateGraphCard(
            string title,
            float width,
            float height,
            out Label headerLabel,
            out VisualElement canvasElement,
            Action<MeshGenerationContext> generateCallback)
        {
            var container = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            container.style.backgroundColor = new Color(0.03f, 0.03f, 0.03f, 0.85f);
            container.style.borderTopLeftRadius = 4;
            container.style.borderTopRightRadius = 4;
            container.style.borderBottomLeftRadius = 4;
            container.style.borderBottomRightRadius = 4;
            container.style.borderLeftColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
            container.style.borderRightColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
            container.style.borderTopColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
            container.style.borderBottomColor = new Color(0.2f, 0.25f, 0.35f, 0.6f);
            container.style.borderLeftWidth = 1;
            container.style.borderRightWidth = 1;
            container.style.borderTopWidth = 1;
            container.style.borderBottomWidth = 1;
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.paddingTop = 6;
            container.style.paddingBottom = 6;
            container.style.width = width;

            headerLabel = new Label(title)
            {
                pickingMode = PickingMode.Ignore,
            };
            headerLabel.style.color = new Color(0.9f, 0.93f, 1f, 1f);
            headerLabel.style.fontSize = 10;
            headerLabel.style.marginBottom = 4;

            canvasElement = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
            };
            canvasElement.style.width = Length.Percent(100);
            canvasElement.style.height = height;
            canvasElement.generateVisualContent += generateCallback;

            container.Add(headerLabel);
            container.Add(canvasElement);
            return container;
        }

        private void OnGenerateFrametimeGraphVisualContent(MeshGenerationContext context)
        {
            if (!_showFrametimeGraph || _historyCount == 0 || _frametimeGraphCanvas == null)
            {
                return;
            }

            float canvasWidth = _frametimeGraphCanvas.resolvedStyle.width > 0 ? _frametimeGraphCanvas.resolvedStyle.width : 304f;
            float canvasHeight = _frametimeGraphCanvas.resolvedStyle.height > 0 ? _frametimeGraphCanvas.resolvedStyle.height : 70f;

            int totalBars = _historyCount;
            // 1 bg quad + 2 guide line quads + totalBars quads
            int quadCount = 1 + 2 + totalBars;
            int vertexCount = quadCount * 4;
            int indexCount = quadCount * 6;

            var mesh = context.Allocate(vertexCount, indexCount);

            int vertIdx = 0;
            int idx = 0;

            void EmitQuad(Rect rect, Color32 color)
            {
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });

                mesh.SetNextIndex((ushort)(vertIdx + 0));
                mesh.SetNextIndex((ushort)(vertIdx + 1));
                mesh.SetNextIndex((ushort)(vertIdx + 2));
                mesh.SetNextIndex((ushort)(vertIdx + 2));
                mesh.SetNextIndex((ushort)(vertIdx + 3));
                mesh.SetNextIndex((ushort)(vertIdx + 0));

                vertIdx += 4;
                idx += 6;
            }

            // 1. Background
            EmitQuad(new Rect(0, 0, canvasWidth, canvasHeight), new Color32(3, 3, 3, 242));

            // 2. Guide lines
            const float maxGraphMs = 33.3f;
            float y60 = canvasHeight - (16.7f / maxGraphMs * canvasHeight);
            float y30 = canvasHeight - (33.3f / maxGraphMs * canvasHeight);
            EmitQuad(new Rect(0, y60, canvasWidth, 1f), new Color32(51, 217, 77, 115));
            EmitQuad(new Rect(0, y30, canvasWidth, 1f), new Color32(255, 140, 26, 102));

            // 3. Bars
            float barWidth = canvasWidth / GraphHistoryCapacity;
            int startIndex = (_historyIndex - _historyCount + GraphHistoryCapacity) % GraphHistoryCapacity;

            for (int i = 0; i < _historyCount; i++)
            {
                int bufIdx = (startIndex + i) % GraphHistoryCapacity;
                float frameMs = _frametimeHistory[bufIdx];
                float barHeight = Mathf.Clamp(frameMs / maxGraphMs * canvasHeight, 2f, canvasHeight);
                float x = i * barWidth;
                float y = canvasHeight - barHeight;
                float w = Mathf.Max(1f, barWidth - 0.5f);

                Color32 barColor = frameMs switch
                {
                    <= 16.7f => new Color32(64, 217, 89, 217),  // Green <= 60 fps
                    <= 33.4f => new Color32(242, 204, 51, 217), // Yellow <= 30 fps
                    _ => new Color32(242, 77, 64, 242),        // Red < 30 fps
                };

                EmitQuad(new Rect(x, y, w, barHeight), barColor);
            }
        }

        private void OnGenerateMemoryGraphVisualContent(MeshGenerationContext context)
        {
            if (!_showFrametimeGraph || _historyCount == 0 || _memoryGraphCanvas == null)
            {
                return;
            }

            float canvasWidth = _memoryGraphCanvas.resolvedStyle.width > 0 ? _memoryGraphCanvas.resolvedStyle.width : 224f;
            float canvasHeight = _memoryGraphCanvas.resolvedStyle.height > 0 ? _memoryGraphCanvas.resolvedStyle.height : 70f;

            int totalBars = _historyCount;
            int quadCount = 1 + totalBars;
            int vertexCount = quadCount * 4;
            int indexCount = quadCount * 6;

            var mesh = context.Allocate(vertexCount, indexCount);

            int vertIdx = 0;
            int idx = 0;

            void EmitQuad(Rect rect, Color32 color)
            {
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = color });
                mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = color });

                mesh.SetNextIndex((ushort)(vertIdx + 0));
                mesh.SetNextIndex((ushort)(vertIdx + 1));
                mesh.SetNextIndex((ushort)(vertIdx + 2));
                mesh.SetNextIndex((ushort)(vertIdx + 2));
                mesh.SetNextIndex((ushort)(vertIdx + 3));
                mesh.SetNextIndex((ushort)(vertIdx + 0));

                vertIdx += 4;
                idx += 6;
            }

            // 1. Background
            EmitQuad(new Rect(0, 0, canvasWidth, canvasHeight), new Color32(3, 3, 3, 242));

            // 2. Bars
            const float maxAllocKb = 64f;
            float barWidth = canvasWidth / GraphHistoryCapacity;
            int startIndex = (_historyIndex - _historyCount + GraphHistoryCapacity) % GraphHistoryCapacity;

            for (int i = 0; i < _historyCount; i++)
            {
                int bufIdx = (startIndex + i) % GraphHistoryCapacity;
                float allocKb = _gcAllocHistory[bufIdx];
                float barHeight = Mathf.Clamp(allocKb / maxAllocKb * canvasHeight, 1f, canvasHeight);
                float x = i * barWidth;
                float y = canvasHeight - barHeight;
                float w = Mathf.Max(1f, barWidth - 0.5f);

                Color32 barColor = allocKb switch
                {
                    <= 4f => new Color32(77, 179, 255, 217),   // Blue <= 4KB
                    <= 16f => new Color32(102, 230, 230, 217), // Cyan <= 16KB
                    <= 40f => new Color32(242, 191, 51, 217),  // Orange
                    _ => new Color32(242, 77, 64, 242),       // Red > 40KB
                };

                EmitQuad(new Rect(x, y, w, barHeight), barColor);
            }
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

            // Push into circular history
            _frametimeHistory[_historyIndex] = frameMs;
            _gcAllocHistory[_historyIndex] = allocKb;
            _historyIndex = (_historyIndex + 1) % GraphHistoryCapacity;
            if (_historyCount < GraphHistoryCapacity)
            {
                _historyCount++;
            }

            _fpsFrames++;
            _fpsTimer += dt;
            if (_fpsTimer >= 0.25f)
            {
                _currentFps = _fpsFrames / _fpsTimer;
                _currentFrameMs = (_fpsTimer / _fpsFrames) * 1000f;

                float minMs = float.MaxValue;
                float maxMs = 0f;
                float sumMs = 0f;
                for (int i = 0; i < _historyCount; i++)
                {
                    float sample = _frametimeHistory[i];
                    if (sample < minMs)
                    {
                        minMs = sample;
                    }

                    if (sample > maxMs)
                    {
                        maxMs = sample;
                    }

                    sumMs += sample;
                }

                _minFrameMs = minMs;
                _maxFrameMs = maxMs;
                _avgFrameMs = _historyCount > 0 ? sumMs / _historyCount : _currentFrameMs;

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
            ILocalPlayer? player = _localPlayer?.Current;

            // ==================== LEFT COLUMN (Minecraft style: System, Player, World, Target) ====================
            _leftSb.Clear();
            _leftSb.Append("<b>Fodinae Client (Unity 6 / URP 2D)</b>\n")
                   .Append(_currentFps.ToString("F0")).Append(" fps (").Append(_currentFrameMs.ToString("F1")).Append(" ms)\n\n");

            if (player != null && player.HasServerPosition)
            {
                Vector3 unityPos = player.transform.position;
                int chunkX = player.Position.x / ProjectRuntimeContracts.World.ChunkSize;
                int chunkY = player.Position.y / ProjectRuntimeContracts.World.ChunkSize;
                int inChunkX = player.Position.x % ProjectRuntimeContracts.World.ChunkSize;
                int inChunkY = player.Position.y % ProjectRuntimeContracts.World.ChunkSize;

                _leftSb.Append("XYZ: ").Append(player.Position.x).Append(" / ").Append(player.Position.y).Append(" (Unity: ").Append(unityPos.x.ToString("F2")).Append(", ").Append(unityPos.y.ToString("F2")).Append(")\n")
                       .Append("Block: ").Append(player.Position.x).Append(" ").Append(player.Position.y).Append(" [").Append(inChunkX).Append(" ").Append(inChunkY).Append(" in Chunk ").Append(chunkX).Append(" ").Append(chunkY).Append("]\n")
                       .Append("Facing: ").Append(player.LastDirection).Append(" | AutoDig: ").Append(player.AutoDig ? "ON" : "OFF").Append(" | Aggression: ").Append(player.Aggression ? "ON" : "OFF").Append("\n");
            }
            else
            {
                _leftSb.Append("XYZ: Waiting for server spawn...\n");
            }

            if (_mapManager != null && _mapManager.IsWorldInitialized)
            {
                _leftSb.Append("World: ").Append(_mapManager.WorldWidth).Append("x").Append(_mapManager.WorldHeight)
                       .Append(" [Chunks: ").Append(_mapManager.WorldWidth / ProjectRuntimeContracts.World.ChunkSize).Append("x").Append(_mapManager.WorldHeight / ProjectRuntimeContracts.World.ChunkSize).Append("] (").Append(_mapManager.WorldCodeName).Append(")\n");
            }

            // Target block info
            Camera? cam = _gameplayCamera?.Camera;
            if (cam != null && Mouse.current != null && _mapManager != null && _mapManager.IsWorldInitialized)
            {
                Vector2 mouseScreen = Mouse.current.position.ReadValue();
                Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
                if (worldPos.y >= 0f && worldPos.y < _mapManager.WorldHeight && worldPos.x >= 0f && worldPos.x < _mapManager.WorldWidth)
                {
                    Vector2Int cell = CoordinateUtils.UnityToServerPos(worldPos, _mapManager.WorldHeight);
                    if (_storage.CellLayer != null)
                    {
                        CellType cellType = _storage.CellLayer.GetCellSync(cell.x, cell.y);
                        var config = _mapManager.GetCellConfig(cellType);
                        bool passable = cellType == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
                        bool breakable = ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Breakable);

                        _leftSb.Append("\n<b>Targeted Block: ").Append(cell.x).Append(", ").Append(cell.y).Append("</b>\n")
                               .Append("fodinae:").Append(cellType.ToString().ToLowerInvariant()).Append(" (#").Append((int)cellType).Append(")\n")
                               .Append("passable: ").Append(passable ? "true" : "false")
                               .Append(" | breakable: ").Append(breakable ? "true" : "false")
                               .Append(" | relief: ").Append(config.ReliefGroup).Append("\n");
                    }
                }
            }

            _leftSb.Append("\n<b>[Channels: 1:Grid 2:Ents 3:Cursor]</b>");
            string leftText = _leftSb.ToString();
            if (_leftLabel!.text != leftText)
            {
                _leftLabel.text = leftText;
            }

            // ==================== RIGHT COLUMN (Hardware, Memory, Profiler, Radiance Cascades) ====================
            _rightSb.Clear();
            _rightSb.Append("<b>").Append(SystemInfo.graphicsDeviceName).Append("</b>\n")
                    .Append(SystemInfo.graphicsDeviceType).Append(" | ").Append(Screen.width).Append("x").Append(Screen.height).Append("@").Append(Screen.currentResolution.refreshRateRatio.value.ToString("F0")).Append("Hz\n\n");

            Fodinae.Rendering.DisplayManager.HDROutput.AppendDebugInfo(
                _rightSb,
                _gameplayCamera?.Camera);

            long totalMemMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
            long totalAllocMb = Profiler.GetMonoHeapSizeLong() / (1024 * 1024);
            long totalReservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            float gcAllocKb = _telemetry.GcAllocPerFrameBytes / 1024f;
            float gcAllocPerSecMb = _telemetry.GcAllocTotalPerSecondBytes / (1024f * 1024f);

            _rightSb.Append("Mem: ").Append((totalMemMb * 100) / Math.Max(1, totalAllocMb)).Append("% ").Append(totalMemMb).Append("/").Append(totalAllocMb).Append("MB (Res: ").Append(totalReservedMb).Append("MB)\n")
                    .Append("Alloc: ").Append(gcAllocKb.ToString("F1")).Append("KB/f (").Append(gcAllocPerSecMb.ToString("F2")).Append("MB/s) | GC: ").Append(_telemetry.GcCollectionCount).Append("\n\n");

            _rightSb.Append("<b>[Terrain Engine]</b>\n")
                    .Append("Mesh: ").Append(_telemetry.TerrainMeshTimeMs.ToString("F2")).Append("ms | Flood: ").Append(_telemetry.TerrainFloodFillTimeMs.ToString("F2")).Append("ms\n")
                    .Append("Cache: ").Append(_telemetry.TerrainCacheTimeMs.ToString("F2")).Append("ms | Upload: ").Append(_telemetry.TerrainGpuUploadTimeMs.ToString("F2")).Append("ms\n")
                    .Append("Rebuilds: ").Append(_telemetry.TerrainRebuildCount).Append(" | Patches: ").Append(_telemetry.TerrainDirtyPatchCount).Append("\n\n");

            var lighting = _lighting;
            string lightPassState = !_debugSettings.BypassLightingCompute ? "ON" : "MUTE";
            string terrainDrawState = !_debugSettings.BypassTerrainDraw ? "ON" : "MUTE";
            string cpuMeshState = !_debugSettings.BypassCpuMeshRebuild ? "ON" : "MUTE";

            _rightSb.Append("<b>[Radiance Cascades]</b>\n")
                    .Append("Solves/s: ").Append(_solvesPerSecond.ToString("F1")).Append(" | DynLights: ").Append(lighting != null ? lighting.UploadedDynamicLightCount : 0).Append("\n")
                    .Append("RC Build: ").Append(_telemetry.LightingBuildCommandsTimeMs.ToString("F2")).Append("ms | Exec: ").Append(_telemetry.LightingExecuteCommandsTimeMs.ToString("F2")).Append("ms\n")
                    .Append("Static: ").Append(_telemetry.LightingStaticSolveCount).Append(" | Dyn: ").Append(_telemetry.LightingDynamicSolveCount).Append(" | Inval: ").Append(_telemetry.LightingRegionInvalidationCount).Append("\n\n");

            // Постпроцесс из этого списка изъят намеренно: его нельзя
            // выключить ничем. Без тонмапа света срезаются в плоский белый,
            // то есть «выключенный» кадр не проще, а неверен.
            _rightSb.Append("<b>[Pass Toggles: 4:RC 6:Terr 7:Mesh 8:Dyn]</b>\n")
                    .Append("RC: ").Append(lightPassState).Append(" | Terr: ").Append(terrainDrawState).Append(" | Mesh: ").Append(cpuMeshState);

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

            DrawWorldDebugGizmos();
        }

        private void DrawWorldDebugGizmos()
        {
            ILocalPlayer? player = _localPlayer?.Current;

            if (_showGrid && _mapManager != null && _mapManager.IsWorldInitialized && player != null)
            {
                DrawChunkGrid(player.Position, _mapManager.WorldHeight);
            }

            if (_showCursor && _mapManager != null && _mapManager.IsWorldInitialized)
            {
                DrawCursorHighlight(_mapManager.WorldHeight);
            }
        }

        private void DrawChunkGrid(Vector2Int playerServerPos, int worldHeight)
        {
            const int chunkSize = ProjectRuntimeContracts.World.ChunkSize;
            int playerChunkX = playerServerPos.x / chunkSize;
            int playerChunkY = playerServerPos.y / chunkSize;

            for (int cx = playerChunkX - 1; cx <= playerChunkX + 1; cx++)
            {
                for (int cy = playerChunkY - 1; cy <= playerChunkY + 1; cy++)
                {
                    if (cx < 0 || cy < 0)
                    {
                        continue;
                    }

                    int serverLeft = cx * chunkSize;
                    int serverTop = cy * chunkSize;
                    Vector3 origin = CoordinateUtils.ServerToUnityPos(serverLeft, serverTop, worldHeight);
                    Vector3 center = origin + new Vector3(chunkSize * 0.5f - 0.5f, -(chunkSize * 0.5f - 0.5f), 0f);

                    FodinaeGizmos.DrawBounds(center, new Vector2(chunkSize, chunkSize), new Color(0f, 0.8f, 1f, 0.4f));
                }
            }
        }

        private void DrawCursorHighlight(int worldHeight)
        {
            Camera? cam = _gameplayCamera?.Camera;
            if (cam == null || Mouse.current == null)
            {
                return;
            }

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
            if (worldPos.y < 0f || worldPos.y >= worldHeight || worldPos.x < 0f)
            {
                return;
            }

            Vector2Int serverCell = CoordinateUtils.UnityToServerPos(worldPos, worldHeight);
            Vector3 cellCenter = CoordinateUtils.ServerToUnityPos(serverCell.x, serverCell.y, worldHeight);

            bool passable = false;
            if (_storage.CellLayer != null && _mapManager != null)
            {
                CellType type = _storage.CellLayer.GetCellSync(serverCell.x, serverCell.y);
                var config = _mapManager.GetCellConfig(type);
                passable = type == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
            }

            Color highlightColor = passable ? Color.green : Color.red;
            FodinaeGizmos.DrawBounds(cellCenter, Vector2.one * 0.95f, highlightColor);
        }
    }
}
