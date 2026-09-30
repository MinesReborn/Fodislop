#nullable enable

using System.Globalization;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

// Проверка LUT до сервера: тот же вход PostProcessRuntimeState.SetLut, через
// который эффект придёт по сети. Окно владеет загруженной таблицей и
// освобождает её, когда таблица снимается или заменяется.
internal sealed class GradingLutWindow : ToolWindow
{
    private string _path = string.Empty;
    private string _status = string.Empty;
    private bool _statusIsError;
    private ColorGradeCubeLut? _lut;
    private float _intensity = 1f;
    private bool _srgbInput;

    private bool _loadRequested;
    private bool _clearRequested;

    public GradingLutWindow()
        : base("LUT", new Rect(16f, 16f, 410f, 230f))
    {
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(320f, 200f);

    protected override void OnPlaySessionReset() => ClearLut();

    protected override void OnDispose() => ClearLut();

    protected override void DrawContent()
    {
        // Запросы применяются на Layout: число контролов Layout и Repaint
        // обязано совпадать.
        if (Event.current.type == EventType.Layout)
        {
            ApplyPendingRequests();
        }

        GUILayout.Label("ФАЙЛ .CUBE", SectionLabelStyle);
        _path = GUILayout.TextField(_path);
        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("Загрузить", SecondaryButtonStyle))
            {
                _loadRequested = true;
            }

            if (GUILayout.Button("Убрать", DangerButtonStyle))
            {
                _clearRequested = true;
            }
        }

        GUILayout.Label(
            $"сила {_intensity.ToString("0.00", CultureInfo.InvariantCulture)}",
            SectionLabelStyle);
        float intensity = GUILayout.HorizontalSlider(_intensity, 0f, 1f);
        bool srgbInput = GUILayout.Toggle(
            _srgbInput,
            _srgbInput ? "●  Вход таблицы в sRGB" : "○  Вход таблицы в sRGB",
            SegmentedButtonStyle);
        if (!Mathf.Approximately(intensity, _intensity) || srgbInput != _srgbInput)
        {
            _intensity = intensity;
            _srgbInput = srgbInput;
            Publish();
        }

        if (_status.Length > 0)
        {
            GUILayout.Label(_status, _statusIsError ? ToolTheme.ErrorLabel : MutedLabelStyle);
        }
    }

    private void ApplyPendingRequests()
    {
        if (_clearRequested)
        {
            _clearRequested = false;
            ClearLut();
            _status = string.Empty;
        }

        if (!_loadRequested)
        {
            return;
        }

        _loadRequested = false;
        if (!ColorGradeCubeLut.TryLoad(_path.Trim(), out ColorGradeCubeLut? lut, out string error) || lut == null)
        {
            _status = error;
            _statusIsError = true;
            return;
        }

        ClearLut();
        _lut = lut;
        _status = $"3D, {lut.Size}";
        _statusIsError = false;
        Publish();
    }

    private void Publish() =>
        PostProcessRuntimeState.SetLut(
            _lut,
            _intensity,
            _srgbInput ? ColorGradeLutColorSpace.SrgbRec709 : ColorGradeLutColorSpace.LinearRec709);

    private void ClearLut()
    {
        if (_lut == null)
        {
            return;
        }

        PostProcessRuntimeState.SetLut(null, 0f);
        _lut.Dispose();
        _lut = null;
    }
}
