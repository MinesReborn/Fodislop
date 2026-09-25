#nullable enable

using System.Collections.Generic;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingZonesWindow : ToolWindow
{
    private const float DefaultHalfHeight = 24f;
    private const float DefaultFeather = 16f;
    private const string ZonesOnLabel = "●  Зоны действуют";
    private const string ZonesOffLabel = "○  Зоны действуют";
    private const string NoCameraLabel = "Камеры нет: зона не определяется.";

    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;

    // Подписи пересобираются на Layout и только при смене показанных чисел.
    // Камера движется каждый кадр, но IMGUI зовёт отрисовку несколько раз за
    // кадр, и строки на каждое событие были бы мусором на ровном месте.
    private readonly List<ZoneLabels> _zoneLabels = [];
    private string _cameraLabel = NoCameraLabel;
    private string _countLabel = string.Empty;
    private bool _labeledHasCamera;
    private bool _labeledZonesEnabled;
    private float _labeledCameraY = float.NaN;
    private int _labeledCount = -1;

    private Vector2 _scroll;
    private int _nextIndex = 1;
    private int _removeRequested = -1;
    private bool _clearRequested;
    private ColorGradeZone? _addRequested;

    public GradingZonesWindow(ColorGradeState state, ColorGradeZones zones)
        : base("Зоны тонкоррекции", new Rect(16f, 382f, 260f, 450f))
    {
        _state = state;
        _zones = zones;
        Visible = false;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 340f);

    public bool CaptureEnabled { get; set; }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextIndex = 1;
        _removeRequested = -1;
        _clearRequested = false;
        _addRequested = null;
        _zoneLabels.Clear();
        _cameraLabel = NoCameraLabel;
        _countLabel = string.Empty;
        _labeledCount = -1;
        _labeledCameraY = float.NaN;
    }

    protected override void DrawContent()
    {
        ApplyPendingChanges();

        // Не Camera.main: правило проекта запрещает искать камеру по тегу, и
        // правильно запрещает — здесь нужна ровно та камера, по которой
        // считается кадр, а её проходу уже толкнули снаружи.
        Camera? camera = PostProcessRuntimeState.MainCamera;
        float cameraY = camera != null ? camera.transform.position.y : float.NaN;
        if (Event.current.type == EventType.Layout || _labeledCount != _zones.Count)
        {
            RefreshLabels(camera != null, cameraY);
        }

        GUILayout.Label("ПРИВЯЗКА К ВЫСОТЕ", SectionLabelStyle);
        _zones.Enabled = GUILayout.Toggle(
            _zones.Enabled,
            _zones.Enabled ? ZonesOnLabel : ZonesOffLabel,
            SegmentedButtonStyle);
        GUILayout.Label(_cameraLabel, camera != null ? MutedLabelStyle : ToolTheme.WarningLabel);

        using (ToolLayout.Horizontal())
        {
            bool controlsEnabled = GUI.enabled;
            GUI.enabled = controlsEnabled && camera != null && CaptureEnabled;
            if (GUILayout.Button("Снять грейд сюда", ActiveButtonStyle))
            {
                _state.Sanitize();
                _addRequested = new ColorGradeZone
                {
                    Name = NextZoneName(),
                    CenterY = cameraY,
                    HalfHeight = DefaultHalfHeight,
                    Feather = DefaultFeather,
                    Exposure = _state.Exposure,
                    Contrast = _state.Contrast,
                    Saturation = _state.Saturation,
                    Grade = _state.ToAuthoredSnapshot(),
                };
            }

            GUI.enabled = controlsEnabled;
            if (GUILayout.Button("Очистить", DangerButtonStyle))
            {
                _clearRequested = true;
            }
        }

        if (!CaptureEnabled)
        {
            GUILayout.Label(
                "Чтобы снять грейд в зону, откройте окно «Тонкоррекция»: " +
                "сохраняться должен именно показанный кадр.",
                ToolTheme.WarningLabel);
        }

        if (_state.HasPreviewOverrides)
        {
            GUILayout.Label(
                "Соло/обход — только просмотр; зона сохранит полный грейд.",
                ToolTheme.WarningLabel);
        }

        ToolTheme.Separator();
        GUILayout.Label(_countLabel, SectionLabelStyle);
        using var scroll = ToolLayout.ScrollView(ref _scroll);

        if (_zones.Count == 0)
        {
            GUILayout.Label(
                "Зон нет — работает единый грейд. Он же остаётся основой: " +
                "зоны накладываются поверх, поэтому дыра между ними не " +
                "оставляет кадр без кривой.",
                MutedLabelStyle);
            return;
        }

        for (int index = 0; index < _zones.Count && index < _zoneLabels.Count; index++)
        {
            DrawZone(index, _zoneLabels[index]);
        }
    }

    private void RefreshLabels(bool hasCamera, float cameraY)
    {
        bool zonesChanged = _labeledCount != _zones.Count;
        while (_zoneLabels.Count < _zones.Count)
        {
            _zoneLabels.Add(new ZoneLabels());
        }

        if (_zoneLabels.Count > _zones.Count)
        {
            _zoneLabels.RemoveRange(_zones.Count, _zoneLabels.Count - _zones.Count);
        }

        for (int index = 0; index < _zones.Count; index++)
        {
            zonesChanged |= _zoneLabels[index].Refresh(_zones.Zones[index], cameraY);
        }

        bool cameraMoved = !(cameraY == _labeledCameraY || (float.IsNaN(cameraY) && float.IsNaN(_labeledCameraY)));
        if (zonesChanged ||
            cameraMoved ||
            hasCamera != _labeledHasCamera ||
            _zones.Enabled != _labeledZonesEnabled)
        {
            _labeledHasCamera = hasCamera;
            _labeledZonesEnabled = _zones.Enabled;
            _labeledCameraY = cameraY;
            _cameraLabel = !hasCamera
                ? NoCameraLabel
                : _zones.Enabled
                    ? $"Камера Y: {cameraY:F1}   —   {_zones.DescribeAt(cameraY)}"
                    : $"Камера Y: {cameraY:F1}   —   зоны выключены, действует база";
        }

        if (_labeledCount != _zones.Count)
        {
            _labeledCount = _zones.Count;
            _countLabel = $"СОХРАНЁННЫЕ ЗОНЫ  ·  {_zones.Count}";
        }
    }

    private void DrawZone(int index, ZoneLabels labels)
    {
        ColorGradeZone zone = _zones.Zones[index];
        using var box = ToolLayout.Vertical(CardStyle);

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(labels.Title, SectionLabelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", DangerButtonStyle, ToolLayout.Width(28f)))
            {
                _removeRequested = index;
            }
        }

        GUILayout.Label(labels.Center, MutedLabelStyle);
        GUILayout.Label(labels.Grade, MutedLabelStyle);
        float half;
        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(labels.Core, ToolTheme.FieldLabel, ToolLayout.Width(112f));
            half = GUILayout.HorizontalSlider(zone.HalfHeight, 0f, 256f);
        }

        float feather;
        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(labels.Transition, ToolTheme.FieldLabel, ToolLayout.Width(112f));
            feather = GUILayout.HorizontalSlider(zone.Feather, 0f, 256f);
        }

        if (!Mathf.Approximately(half, zone.HalfHeight) ||
            !Mathf.Approximately(feather, zone.Feather))
        {
            _zones.Replace(index, zone with { HalfHeight = half, Feather = feather });
        }
    }

    private void ApplyPendingChanges()
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_clearRequested)
        {
            _zones.Clear();
            _clearRequested = false;
            _removeRequested = -1;
        }
        else if (_removeRequested >= 0)
        {
            _zones.RemoveAt(_removeRequested);
            _removeRequested = -1;
        }

        if (_addRequested.HasValue)
        {
            _zones.Add(_addRequested.Value);
            _addRequested = null;
        }
    }

    private string NextZoneName()
    {
        while (true)
        {
            string candidate = $"зона {_nextIndex++}";
            bool exists = false;
            for (int index = 0; index < _zones.Count; index++)
            {
                if (_zones.Zones[index].Name == candidate)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                return candidate;
            }
        }
    }

    private sealed class ZoneLabels
    {
        private const int NoWeight = int.MinValue;

        private string? _name;
        private float _centerY = float.NaN;
        private float _exposure = float.NaN;
        private float _contrast = float.NaN;
        private float _saturation = float.NaN;
        private float _halfHeight = float.NaN;
        private float _feather = float.NaN;
        private int _weightPercent = NoWeight + 1;

        public string Title { get; private set; } = string.Empty;

        public string Center { get; private set; } = string.Empty;

        public string Grade { get; private set; } = string.Empty;

        public string Core { get; private set; } = string.Empty;

        public string Transition { get; private set; } = string.Empty;

        public bool Refresh(ColorGradeZone zone, float cameraY)
        {
            float weight = zone.WeightAt(cameraY);
            int weightPercent = float.IsNaN(weight) ? NoWeight : Mathf.RoundToInt(weight * 100f);
            bool changed = false;

            if (!string.Equals(_name, zone.Name, System.StringComparison.Ordinal) ||
                weightPercent != _weightPercent)
            {
                _name = zone.Name;
                _weightPercent = weightPercent;
                Title = weightPercent == NoWeight
                    ? $"{zone.Name}  ·  вес —"
                    : $"{zone.Name}  ·  вес {weightPercent}%";
                changed = true;
            }

            if (zone.CenterY != _centerY)
            {
                _centerY = zone.CenterY;
                Center = $"Центр Y  {zone.CenterY:F1}";
                changed = true;
            }

            if (zone.Exposure != _exposure || zone.Contrast != _contrast || zone.Saturation != _saturation)
            {
                _exposure = zone.Exposure;
                _contrast = zone.Contrast;
                _saturation = zone.Saturation;
                Grade =
                    $"эксп {zone.Exposure:+0.##;-0.##;0}   " +
                    $"контраст {zone.Contrast:+0.##;-0.##;0}   " +
                    $"цвет {zone.Saturation:0.##}";
                changed = true;
            }

            if (zone.HalfHeight != _halfHeight)
            {
                _halfHeight = zone.HalfHeight;
                Core = $"Ядро ±{zone.HalfHeight:F1}";
                changed = true;
            }

            if (zone.Feather != _feather)
            {
                _feather = zone.Feather;
                Transition = $"Переход {zone.Feather:F1}";
                changed = true;
            }

            return changed;
        }
    }
}
