#nullable enable

using System.Collections.Generic;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingLayersWindow : ToolWindow
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private readonly GradingLayerControlsDrawer _drawer;
    private Vector2 _scroll;
    private ColorGradeLayer? _selectedLayer = ColorGradeLayer.Exposure;
    private ColorGradeLayer? _selectedLayerRequested;
    private bool _selectionRequested;

    // Подписи вкладок и баннеров меняются только со сменой состояния слоёв;
    // собирать их на каждое событие IMGUI незачем.
    private readonly Dictionary<string, string[]> _tabTitles = [];

    private readonly GradingLayerStatusBanners _banners;

    public GradingLayersWindow(ColorGradeState state, ColorGradeZones zones)
        : base("Тонкоррекция  ·  F5", new Rect(292f, 16f, 430f, 740f))
    {
        _state = state;
        _zones = zones;
        _drawer = new GradingLayerControlsDrawer(state, zones);
        _banners = new GradingLayerStatusBanners(state, _drawer);
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(400f, 420f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _drawer.ResetState();
        _selectedLayer = ColorGradeLayer.Exposure;
        _selectedLayerRequested = null;
        _selectionRequested = false;
    }

    protected override void DrawContent()
    {
        _state.BeginHistoryFrame();
        ApplyPendingSelection();
        HandleKeyboardShortcuts();
        _drawer.ApplyPendingActions();
        DrawLayerTabBar();
        _banners.DrawMasterStatusBanners();

        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (_selectedLayer.HasValue)
            {
                DrawFocusedLayer(_selectedLayer.Value);
            }
            else
            {
                DrawAllLayers();
            }

            _drawer.DrawActions(SectionLabelStyle, WrappedLabelStyle);
        }

        _state.CommitHistoryFrame();
    }

    private void HandleKeyboardShortcuts()
    {
        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown ||
            GUIUtility.keyboardControl != 0 ||
            !ToolWindows.IsFocused(this))
        {
            return;
        }

        switch (currentEvent.keyCode)
        {
            case KeyCode.Alpha1:
                RequestLayer(ColorGradeLayer.Exposure);
                currentEvent.Use();
                break;

            case KeyCode.Alpha2:
                RequestLayer(ColorGradeLayer.WhiteBalance);
                currentEvent.Use();
                break;

            case KeyCode.Alpha3:
                RequestLayer(ColorGradeLayer.Cdl);
                currentEvent.Use();
                break;

            case KeyCode.Alpha4:
                RequestLayer(ColorGradeLayer.Saturation);
                currentEvent.Use();
                break;

            case KeyCode.Alpha5:
                RequestLayer(ColorGradeLayer.Contrast);
                currentEvent.Use();
                break;

            case KeyCode.Alpha6:
                RequestLayer(ColorGradeLayer.Curve);
                currentEvent.Use();
                break;

            case KeyCode.Alpha0:
            case KeyCode.BackQuote:
                RequestLayer(null);
                currentEvent.Use();
                break;

            case KeyCode.LeftBracket:
            case KeyCode.LeftArrow:
                SelectPreviousLayer();
                currentEvent.Use();
                break;

            case KeyCode.RightBracket:
            case KeyCode.RightArrow:
                SelectNextLayer();
                currentEvent.Use();
                break;

            case KeyCode.S:
                ToggleSoloCurrentLayer();
                currentEvent.Use();
                break;

            case KeyCode.B:
            case KeyCode.M:
                ToggleBypassCurrentLayer();
                currentEvent.Use();
                break;

            case KeyCode.R:
                ResetCurrentLayer();
                currentEvent.Use();
                break;

            case KeyCode.Z when currentEvent.control || currentEvent.command:
                if (currentEvent.shift)
                {
                    _state.Redo();
                }
                else
                {
                    _state.Undo();
                }

                _state.CancelHistoryFrame();
                currentEvent.Use();
                break;

            case KeyCode.Y when currentEvent.control || currentEvent.command:
                _state.Redo();
                _state.CancelHistoryFrame();
                currentEvent.Use();
                break;

            default:
                break;
        }
    }

    private void SelectPreviousLayer()
    {
        if (!_selectedLayer.HasValue)
        {
            RequestLayer(ColorGradeLayer.Curve);
            return;
        }

        int current = (int)_selectedLayer.Value;
        RequestLayer((ColorGradeLayer)((current - 1 + 6) % 6));
    }

    private void SelectNextLayer()
    {
        if (!_selectedLayer.HasValue)
        {
            RequestLayer(ColorGradeLayer.Exposure);
            return;
        }

        int current = (int)_selectedLayer.Value;
        RequestLayer((ColorGradeLayer)((current + 1) % 6));
    }

    private void ToggleSoloCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _drawer.RequestSolo(_state.Solo == layer ? null : layer);
    }

    private void ToggleBypassCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _drawer.RequestBypass(layer, !_state.IsBypassed(layer));
    }

    private void ResetCurrentLayer()
    {
        ColorGradeLayer layer = _selectedLayer ?? ColorGradeLayer.Exposure;
        _state.ResetLayer(layer);
        _drawer.ClearNumberCache();
    }

    private void DrawLayerTabBar()
    {
        GUILayout.Label("СЛОИ КОНВЕЙЕРА", SectionLabelStyle);
        using (ToolLayout.Horizontal())
        {
            DrawTabButton(ColorGradeLayer.Exposure, "1  Экспозиция");
            DrawTabButton(ColorGradeLayer.WhiteBalance, "2  Баланс");
            DrawTabButton(ColorGradeLayer.Cdl, "3  CDL");
        }

        using (ToolLayout.Horizontal())
        {
            DrawTabButton(ColorGradeLayer.Saturation, "4  Цвет");
            DrawTabButton(ColorGradeLayer.Contrast, "5  Контраст");
            DrawTabButton(ColorGradeLayer.Curve, "6  Кривая");
        }

        bool isAll = !_selectedLayer.HasValue;
        if (GUILayout.Toggle(isAll, "0  Все слои", SegmentedButtonStyle) && !isAll)
        {
            RequestLayer(null);
        }
    }

    private void DrawTabButton(ColorGradeLayer layer, string label)
    {
        bool isSelected = _selectedLayer == layer;
        bool isSolo = _state.Solo == layer;
        bool isBypassed = _state.IsBypassed(layer);

        if (!_tabTitles.TryGetValue(label, out string[]? titles))
        {
            titles = ["●" + label, "○" + label, "★" + label];
            _tabTitles[label] = titles;
        }

        string title = titles[isSolo ? 2 : (isBypassed ? 1 : 0)];

        if (GUILayout.Toggle(
                isSelected,
                title,
                SegmentedButtonStyle,
                ToolLayout.ExpandWidth(true)) &&
            !isSelected)
        {
            RequestLayer(layer);
        }
    }

    private void DrawFocusedLayer(ColorGradeLayer layer)
    {
        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("◄  Предыдущий", SecondaryButtonStyle, ToolLayout.Width(112f)))
            {
                SelectPreviousLayer();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                _banners.GetFocusedLabel(layer),
                SectionLabelStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Следующий  ►", SecondaryButtonStyle, ToolLayout.Width(112f)))
            {
                SelectNextLayer();
            }
        }

        using (ToolLayout.Vertical(CardStyle))
        {
            DrawLayerHeaderBar(layer, GradingLayerControlsDrawer.GetLayerTitle(layer), showFocusButton: false);
            GUILayout.Space(4f);
            _banners.DrawFocusedLayerStatusBanner(layer);
            _drawer.DrawLayerControls(layer);
        }

        GUILayout.Label(
            "Горячие клавиши: 1-6 — выбор слоя, 0 — все, [ / ] — перелистывание, B — обход, S — соло, R — сброс.",
            MutedLabelStyle);
    }

    private void DrawAllLayers()
    {
        GUILayout.Label("ЛИНЕЙНОЕ ПРОСТРАНСТВО  ·  ФИЗИКА СЪЁМКИ", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Exposure, "Экспозиция");
        DrawLayerSection(ColorGradeLayer.WhiteBalance, "Баланс белого");

        GUILayout.Space(8f);
        GUILayout.Label("ЛОГАРИФМИЧЕСКОЕ  ·  ТВОРЧЕСКИЙ ГРЕЙД", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Cdl, "ASC CDL");
        DrawLayerSection(ColorGradeLayer.Saturation, "Насыщенность");
        DrawLayerSection(ColorGradeLayer.Contrast, "Контраст");

        GUILayout.Space(8f);
        GUILayout.Label("КРИВАЯ ВЫВОДА", SectionLabelStyle);
        DrawLayerSection(ColorGradeLayer.Curve, "Кривая");
    }

    private void DrawLayerSection(ColorGradeLayer layer, string title)
    {
        using (ToolLayout.Vertical(CardStyle))
        {
            DrawLayerHeaderBar(layer, title, showFocusButton: true);
            GUILayout.Space(4f);
            _drawer.DrawLayerControls(layer);
        }
    }

    // Заголовок слоя со значком состояния. Склейка строк была в каждом
    // событии IMGUI для каждого из шести слоёв.
    private readonly Dictionary<string, string[]> _headerLabels = [];

    private string HeaderLabel(string title, int state)
    {
        if (!_headerLabels.TryGetValue(title, out string[]? labels))
        {
            labels = ["★ " + title, "● " + title, "○ " + title];
            _headerLabels[title] = labels;
        }

        return labels[state];
    }

    private void DrawLayerHeaderBar(ColorGradeLayer layer, string title, bool showFocusButton)
    {
        bool active = _state.IsActive(layer);
        bool soloed = _state.Solo == layer;
        bool wasBypassed = _state.IsBypassed(layer);

        using (ToolLayout.Horizontal())
        {
            GUILayout.Label(HeaderLabel(title, soloed ? 0 : (active ? 1 : 2)), SectionLabelStyle);
            GUILayout.FlexibleSpace();
            if (showFocusButton && GUILayout.Button("Открыть слой", ActiveButtonStyle, ToolLayout.Width(96f)))
            {
                RequestLayer(layer);
            }
        }

        using (ToolLayout.Horizontal())
        {
            bool controlsEnabled = GUI.enabled;
            bool wasEnabled = _state.IsEnabled(layer);
            bool enabled = GUILayout.Toggle(
                wasEnabled,
                "Enable",
                SegmentedButtonStyle,
                ToolLayout.ExpandWidth(true));
            if (enabled != wasEnabled)
            {
                _state.SetEnabled(layer, enabled);
            }

            GUI.enabled = controlsEnabled && !soloed;
            bool bypass = GUILayout.Toggle(
                wasBypassed,
                "B  Обход",
                SegmentedButtonStyle,
                ToolLayout.ExpandWidth(true));
            GUI.enabled = controlsEnabled;
            if (bypass != wasBypassed)
            {
                _drawer.RequestBypass(layer, bypass);
            }

            bool solo = GUILayout.Toggle(
                soloed,
                "S  Соло",
                SegmentedButtonStyle,
                ToolLayout.ExpandWidth(true));
            if (solo != soloed)
            {
                _drawer.RequestSolo(solo ? layer : null);
            }

            if (GUILayout.Button("R  Сброс", SecondaryButtonStyle, ToolLayout.Width(84f)))
            {
                _state.ResetLayer(layer);
                _drawer.ClearNumberCache();
            }
        }
    }

    private void RequestLayer(ColorGradeLayer? layer)
    {
        _selectedLayerRequested = layer;
        _selectionRequested = true;
    }

    private void ApplyPendingSelection()
    {
        if (!_selectionRequested || Event.current.type != EventType.Layout)
        {
            return;
        }

        _selectedLayer = _selectedLayerRequested;
        _selectedLayerRequested = null;
        _selectionRequested = false;
    }
}
