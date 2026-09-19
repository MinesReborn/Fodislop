#nullable enable

using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

// Status banners above the layer tabs: solo / bypassed-layers / focused-layer.
// Owns its label caches; the window keeps tab chrome and drawing order.
internal sealed class GradingLayerStatusBanners
{
    private readonly ColorGradeState _state;
    private readonly GradingLayerControlsDrawer _drawer;
    private ColorGradeLayer? _soloLabelLayer;
    private string _soloLabel = string.Empty;
    private int _bypassLabelCount = -1;
    private string _bypassLabel = string.Empty;
    private ColorGradeLayer? _focusedLabelLayer;
    private string _focusedLabel = string.Empty;

    public GradingLayerStatusBanners(ColorGradeState state, GradingLayerControlsDrawer drawer)
    {
        _state = state;
        _drawer = drawer;
    }

    public void DrawMasterStatusBanners()
    {
        if (_state.Solo.HasValue)
        {
            ColorGradeLayer soloLayer = _state.Solo.Value;
            if (_soloLabelLayer != soloLayer)
            {
                _soloLabelLayer = soloLayer;
                _soloLabel = $"[!] СОЛО: показывается только слой «{_drawer.GetLayerName(soloLayer)}»";
            }

            GuiStyles.WarningBanner(_soloLabel);
            if (GUILayout.Button("Сбросить соло", GuiStyles.Button, GUILayout.Height(18f)))
            {
                _state.ToggleSolo(soloLayer);
            }

            GuiStyles.Spacing(4f);
        }

        int bypassedCount = _state.BypassedCount;
        if (bypassedCount > 0 && !_state.Solo.HasValue)
        {
            if (_bypassLabelCount != bypassedCount)
            {
                _bypassLabelCount = bypassedCount;
                _bypassLabel = $"[!] Отключено слоев: {bypassedCount} из {ColorGradeState.LayerCount}";
            }

            GuiStyles.WarningBanner(_bypassLabel);
            if (GUILayout.Button("Включить все слои", GuiStyles.Button, GUILayout.Height(18f)))
            {
                _drawer.RequestClearBypasses();
            }

            GuiStyles.Spacing(4f);
        }
    }

    public string GetFocusedLabel(ColorGradeLayer layer)
    {
        if (_focusedLabelLayer != layer)
        {
            _focusedLabelLayer = layer;
            _focusedLabel = $"Слой «{_drawer.GetLayerName(layer)}»";
        }

        return _focusedLabel;
    }

    public void DrawFocusedLayerStatusBanner(ColorGradeLayer layer)
    {
        if (_state.IsBypassed(layer))
        {
            if (_focusedLabelLayer != layer)
            {
                _focusedLabelLayer = layer;
                _focusedLabel = $"Слой «{_drawer.GetLayerName(layer)}» сейчас отключен — его правки не влияют на кадр.";
            }

            GuiStyles.WarningBanner(_focusedLabel);
            GuiStyles.Spacing(6f);
        }
    }
}
