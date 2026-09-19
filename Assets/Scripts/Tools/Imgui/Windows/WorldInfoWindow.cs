#nullable enable

using System.Text;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.World;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public sealed class WorldInfoWindow : ToolWindow
{
    private const float TwoColumnWidth = 470f;

    private readonly IFrameTelemetry _telemetry;
    private readonly LightingEngine? _lighting;
    private readonly MapManager? _mapManager;
    private readonly IWorldDataStorage? _storage;
    private readonly ILocalPlayerState? _localPlayer;
    private readonly IGameplayCamera? _camera;
    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly FrameStatsWindow _stats;
    private readonly StringBuilder _leftSb = new(1024);
    private readonly StringBuilder _rightSb = new(1024);
    private string _left = string.Empty;
    private string _right = string.Empty;
    private float _nextUpdate;
    private Vector2 _scroll;

    public WorldInfoWindow(
        IFrameTelemetry telemetry,
        LightingEngine? lighting,
        MapManager? mapManager,
        IWorldDataStorage? storage,
        ILocalPlayerState? localPlayer,
        IGameplayCamera? camera,
        IRuntimeDebugSettings debugSettings,
        FrameStatsWindow stats)
        : base("Мир и клиент", new Rect(628f, 16f, 520f, 560f))
    {
        _telemetry = telemetry;
        _lighting = lighting;
        _mapManager = mapManager;
        _storage = storage;
        _localPlayer = localPlayer;
        _camera = camera;
        _debugSettings = debugSettings;
        _stats = stats;
    }

    public override Vector2 MinimumSize => new(420f, 360f);

    public override void Tick()
    {
        if (!Visible || Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + 1f;

        DebugOverlayTextFormatter.FormatLeftColumn(
            _leftSb, _localPlayer?.Current, _mapManager, _storage, _camera);
        _left = _leftSb.ToString();

        DebugOverlayTextFormatter.FormatRightColumn(
            _rightSb, _telemetry, _lighting, _debugSettings, _camera, _stats.SolvesPerSecond);
        _right = _rightSb.ToString();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        _left = string.Empty;
        _right = string.Empty;
        _leftSb.Clear();
        _rightSb.Clear();
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            // Две колонки, пока окно достаточно широкое. Столбцы читают
            // вместе — «где я» и «во что это обходится», — и на широком окне
            // разносить их по вертикали значило бы заставлять прокручивать
            // ради сравнения того, что помещается рядом.
            if (Rect.width >= TwoColumnWidth)
            {
                using (ToolLayout.Horizontal())
                {
                    DrawColumn("МИР И ИГРОК", _left, ToolLayout.Width((Rect.width - 46f) * 0.5f));
                    DrawColumn("РЕНДЕР И КЛИЕНТ", _right);
                }

                return;
            }

            DrawColumn("МИР И ИГРОК", _left);
            DrawColumn("РЕНДЕР И КЛИЕНТ", _right);
        }
    }

    private static void DrawColumn(string title, string body, params GUILayoutOption[] options)
    {
        using (new GUILayout.VerticalScope(options))
        {
            ToolChrome.SectionHeader(title);
            using (ToolLayout.Vertical(CardStyle))
            {
                GUILayout.Label(body, RichLabelStyle);
            }
        }
    }
}
