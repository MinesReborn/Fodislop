#nullable enable

using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingActionsDrawer
{
    private readonly ColorGradeState _state;
    private readonly ColorGradeZones _zones;
    private string? _status;
    private bool _statusIsError;
    private bool _clearPreviewRequested;
    private bool _loadRequested;
    private bool _loadPresetRequested;
    private bool _resetAllRequested;
    private string _presetName = "default";

    public GradingActionsDrawer(ColorGradeState state, ColorGradeZones zones)
    {
        _state = state;
        _zones = zones;
    }

    public string? Status => _status;

    public bool StatusIsError => _statusIsError;

    public void ClearStatus()
    {
        _status = null;
        _statusIsError = false;
    }

    public void ResetState()
    {
        _status = null;
        _statusIsError = false;
        _clearPreviewRequested = false;
        _loadRequested = false;
        _loadPresetRequested = false;
        _resetAllRequested = false;
        _presetName = "default";
    }

    public void SetStatus(bool success, string successMessage, string failureMessage)
    {
        _statusIsError = !success;
        _status = success ? successMessage : failureMessage;
    }

    public void DrawActions(GUIStyle sectionStyle, GUIStyle wrappedLabelStyle)
    {
        ToolTheme.Separator();
        GUILayout.Label("ФАЙЛ И ЭКСПОРТ", sectionStyle);
        if (_state.HasPreviewOverrides)
        {
            using (ToolLayout.Vertical(ToolTheme.Card))
            {
                GUILayout.Label(
                    "Соло/обход меняют только предпросмотр. Сохранение, экспорт и " +
                    "зоны содержат полный грейд со всеми слоями.",
                    ToolTheme.WarningLabel);
                if (GUILayout.Button("Показать полный грейд", ToolTheme.ActiveButton))
                {
                    _clearPreviewRequested = true;
                }
            }
        }

        using (ToolLayout.Horizontal())
        {
            GUI.enabled = _state.CanUndo;
            if (GUILayout.Button("Undo", ToolTheme.SecondaryButton))
            {
                _state.Undo();
                _state.CancelHistoryFrame();
            }

            GUI.enabled = _state.CanRedo;
            if (GUILayout.Button("Redo", ToolTheme.SecondaryButton))
            {
                _state.Redo();
                _state.CancelHistoryFrame();
            }

            GUI.enabled = true;
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Сохранить", ToolTheme.ActiveButton))
            {
                SetStatus(
                    ColorGradeFile.Save(_state, _zones),
                    "Сохранено: " + ColorGradeFile.Path,
                    "Ошибка сохранения");
            }

            if (GUILayout.Button("Загрузить", ToolTheme.SecondaryButton))
            {
                _loadRequested = true;
            }

            if (GUILayout.Button("Сбросить всё", ToolTheme.DangerButton))
            {
                _resetAllRequested = true;
            }
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Экспорт .cdl", ToolTheme.SecondaryButton))
            {
                SetStatus(
                    ColorGradeFile.ExportCdl(_state),
                    "ASC CDL: " + ColorGradeFile.CdlPath,
                    "Ошибка экспорта ASC CDL");
            }

            if (GUILayout.Button("Копировать код", ToolTheme.SecondaryButton))
            {
                GUIUtility.systemCopyBuffer = ColorGradeFile.ToLookSource(_state);
                SetStatus(true, "Блок PostProcessLook скопирован", string.Empty);
            }
        }

        GUILayout.Label(
            "ASC CDL содержит только Slope/Offset/Power и насыщенность; " +
            "полный look переносится кнопкой «копировать код».",
            wrappedLabelStyle);

        GUILayout.Label("ПРЕСЕТЫ", sectionStyle);
        _presetName = GUILayout.TextField(_presetName);
        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Сохранить пресет", ToolTheme.ActiveButton))
            {
                SetStatus(
                    ColorGradeFile.SavePreset(_state, _zones, _presetName),
                    $"Пресет сохранён: {_presetName}",
                    $"Ошибка сохранения пресета: {_presetName}");
            }

            if (GUILayout.Button("Загрузить пресет", ToolTheme.SecondaryButton))
            {
                _loadPresetRequested = true;
            }
        }

        string[] presets = ColorGradeFile.ListPresets();
        GUILayout.Label(
            presets.Length == 0
                ? "Сохранённых пресетов пока нет."
                : "Доступно: " + string.Join(", ", presets),
            wrappedLabelStyle);

        GUIStyle statusStyle = _statusIsError ? ToolTheme.ErrorLabel : ToolTheme.SuccessLabel;
        GUILayout.Label(_status ?? string.Empty, statusStyle);
    }

    public void ApplyPendingActions(GradingLayerControlsDrawer drawer)
    {
        if (Event.current.type != EventType.Layout)
        {
            return;
        }

        if (_clearPreviewRequested)
        {
            _state.ClearPreviewOverrides();
            _clearPreviewRequested = false;
        }

        if (_loadRequested)
        {
            bool loaded = ColorGradeFile.TryLoad(_state, _zones);
            drawer.ClearNumberCache();
            SetStatus(
                loaded,
                "Загружено: " + ColorGradeFile.Path,
                "Файл не загружен: " + ColorGradeFile.Path);
            _loadRequested = false;
        }

        if (_loadPresetRequested)
        {
            bool loaded = ColorGradeFile.TryLoadPreset(_state, _zones, _presetName);
            drawer.ClearNumberCache();
            SetStatus(
                loaded,
                $"Пресет загружен: {_presetName}",
                $"Пресет не загружен: {_presetName}");
            _loadPresetRequested = false;
        }

        if (_resetAllRequested)
        {
            _state.ResetToLook();
            _zones.Clear();
            _zones.Enabled = false;
            drawer.ClearNumberCache();
            SetStatus(true, "Возвращен PostProcessLook; зоны очищены", string.Empty);
            _resetAllRequested = false;
        }
    }
}
