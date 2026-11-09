#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using Kern.Rendering.Display;
using Kern.Rendering.PostProcessing;
using Kern.UI.Controls;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Калибровка дисплея — макет visual/kern-ui #calib, как в больших играх (HGIG): шаг — узор во весь экран и один ползунок.
// HDR: пиковая яркость → уровень чёрного → белая точка; SDR: яркость. Значения применяются сразу.
//
// Узор рисует проход вывода (PostProcess.compute, _CalibrationPattern): только там величина задаётся в долях paper white
// и не переделывается ни кривой, ни грейдом. Белый интерфейс и есть paper white, им нельзя показать ни пик, ни ступени
// у чёрного, поэтому рамки узора в разметке прозрачны, а их места уходят в проход вывода (SetCalibrationRects).
// Остальной интерфейс на время калибровки скрыт: поверх узора не должно лечь ничего, кроме подписей и ползунка.
internal sealed class DisplayCalibrationScreen
{
    private enum Step
    {
        Peak,
        Black,
        Paper,
        Brightness,
    }

    private readonly UIDocument _doc;
    private readonly IClientConfigManager _config;
    private readonly DisplayManager _display;
    private readonly ILocalizationService _loc;
    private readonly IPlayerStats? _stats;
    private readonly Action<bool> _setCapturing;
    private readonly List<(VisualElement Element, bool Visible)> _hidden = new();

    private TemplateContainer? _tree;
    private VisualElement? _stage;
    private VisualElement? _steps;
    private Slider? _range;
    private Label? _value;
    private Label? _title;
    private Label? _text;
    private Button? _back;
    private Button? _next;
    private IVisualElementScheduledItem? _keys;
    private Step[] _order = Array.Empty<Step>();
    private int _at;

    public DisplayCalibrationScreen(
        UIDocument doc,
        IClientConfigManager config,
        DisplayManager display,
        ILocalizationService loc,
        IPlayerStats? stats,
        Action<bool> setCapturing)
    {
        _doc = doc;
        _config = config;
        _display = display;
        _loc = loc;
        _stats = stats;
        _setCapturing = setCapturing;
    }

    public void Open()
    {
        if (_tree != null)
        {
            return;
        }

        VisualElement root = _doc.rootVisualElement ??
            throw new InvalidOperationException("[Calibration] The UI panel is not ready; the calibration screen cannot be shown.");
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(ResourcePaths.CalibrationUxml) ??
            throw new InvalidOperationException($"[Calibration] Resources/{ResourcePaths.CalibrationUxml}.uxml is required.");

        HideOtherInterface(root);

        _tree = template.Instantiate();
        _tree.AddToClassList("ui-fullscreen");
        _stage = Require<VisualElement>("CalibStage");
        _steps = Require<VisualElement>("CalibSteps");
        _range = Require<Slider>("CalibRange");
        _value = Require<Label>("CalibValue");
        _title = Require<Label>("CalibTitle");
        _text = Require<Label>("CalibText");
        _back = Require<Button>("CalibBack");
        _next = Require<Button>("CalibNext");

        Require<Label>("CalibKicker").text = _loc.Get("settings.window.calibration").ToUpperInvariant();
        Require<Button>("CalibCancel").tooltip = _loc.Get("settings.calibration.cancel");
        Require<Button>("CalibCancel").clicked += Close;
        Require<Button>("CalibMinus").tooltip = _loc.Get("settings.calibration.less");
        Require<Button>("CalibMinus").clicked += () => Nudge(-1);
        Require<Button>("CalibPlus").tooltip = _loc.Get("settings.calibration.more");
        Require<Button>("CalibPlus").clicked += () => Nudge(1);
        Require<Button>("CalibReset").text = _loc.Get("settings.calibration.reset");
        Require<Button>("CalibReset").clicked += () => Write(DefaultOf(_order[_at]));
        _back.text = _loc.Get("settings.calibration.back");
        _back.clicked += () => Go(_at - 1);
        _next.clicked += () =>
        {
            if (_at < _order.Length - 1)
            {
                Go(_at + 1);
            }
            else
            {
                Close();
            }
        };
        KernRange.Decorate(_range);
        _range.RegisterValueChangedCallback(evt => Write(evt.newValue));

        string[] captions = { "settings.calibration.mark_hidden", "settings.calibration.mark_faint", "settings.calibration.mark_visible" };
        for (int i = 0; i < captions.Length; i++)
        {
            Require<Label>("CalibCaption" + i).text = _loc.Get(captions[i]).ToUpperInvariant();
        }

        FillCard();
        root.Add(_tree);
        _tree.BringToFront();
        _stage.RegisterCallback<GeometryChangedEvent>(_ => LayoutPattern());

        // HDR: три шага по узорам прохода вывода; SDR: одна яркость по знакам у чёрного
        _order = HDROutput.Active ? new[] { Step.Peak, Step.Black, Step.Paper } : new[] { Step.Brightness };
        _setCapturing(true);
        _keys = _tree.schedule.Execute(PollKeys).Every(0);
        Go(0);
    }

    public void Close()
    {
        // Узор гасится первым: если экран почему-то не снимется, игра всё равно вернётся на место
        PostProcessRuntimeState.SetCalibrationPattern(CalibrationPattern.Off, 0f);
        PostProcessRuntimeState.SetCalibrationRects(Vector4.zero, Vector4.zero, Vector4.zero);
        if (_tree == null)
        {
            return;
        }

        _keys?.Pause();
        _keys = null;
        _tree.RemoveFromHierarchy();
        _tree = null;
        foreach ((VisualElement element, bool visible) in _hidden)
        {
            element.visible = visible;
        }

        _hidden.Clear();
        // Перехват снимается кадром позже: Escape, закрывший калибровку, не должен в этом же кадре закрыть настройки
        _doc.rootVisualElement?.schedule.Execute(() => _setCapturing(false));
    }

    // Всё остальное — соседи в этом документе и корни других документов — на время калибровки невидимо
    private void HideOtherInterface(VisualElement root)
    {
        _hidden.Clear();
        foreach (VisualElement child in root.Children())
        {
            _hidden.Add((child, child.visible));
            child.visible = false;
        }

        foreach (UIDocument other in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
        {
            if (other != _doc && other.rootVisualElement != null)
            {
                _hidden.Add((other.rootVisualElement, other.rootVisualElement.visible));
                other.rootVisualElement.visible = false;
            }
        }
    }

    private void Go(int index)
    {
        _at = Mathf.Clamp(index, 0, _order.Length - 1);
        Step step = _order[_at];
        (string title, string text) = step switch
        {
            Step.Peak => ("settings.calibration.peak", "settings.calibration.peak_text"),
            Step.Black => ("settings.calibration.black", "settings.calibration.black_text"),
            Step.Paper => ("settings.calibration.paper", "settings.calibration.paper_text"),
            _ => ("settings.calibration.brightness", "settings.calibration.black_text"),
        };
        _title!.text = _loc.Get(title);
        _text!.text = _loc.Get(text);

        (float min, float max) = RangeOf(step);
        _range!.lowValue = min;
        _range.highValue = max;
        _range.SetValueWithoutNotify(Read(step));
        ShowValue(step);

        Require<VisualElement>("CalibPeak").EnableInClassList("is-on", step == Step.Peak);
        Require<VisualElement>("CalibBlack").EnableInClassList("is-on", step is Step.Black or Step.Brightness);
        Require<VisualElement>("CalibPaper").EnableInClassList("is-on", step == Step.Paper);

        _steps!.Clear();
        UIState.SetHidden(_steps, _order.Length < 2);
        for (int i = 0; i < _order.Length; i++)
        {
            var dot = new Label((i + 1).ToString(CultureInfo.InvariantCulture));
            dot.AddToClassList("calib-step-dot");
            dot.EnableInClassList("is-done", i < _at);
            dot.EnableInClassList("is-on", i == _at);
            _steps.Add(dot);
        }

        UIState.SetHidden(_back!, _at == 0);
        _next!.text = _loc.Get(_at == _order.Length - 1 ? "settings.calibration.done" : "settings.calibration.next");
        PushPattern();
        LayoutPattern();
    }

    private void Nudge(int direction)
    {
        Step step = _order[_at];
        Write(Read(step) + direction * StepOf(step));
    }

    private void Write(float value)
    {
        Step step = _order[_at];
        (float min, float max) = RangeOf(step);
        float quantum = StepOf(step);
        value = Mathf.Clamp(Mathf.Round(value / quantum) * quantum, min, max);
        switch (step)
        {
            case Step.Peak:
                _display.SetPeakBrightnessNits(value);
                break;
            case Step.Black:
                _display.SetBlackLevel(value);
                break;
            case Step.Paper:
                _display.SetPaperWhiteNits(value);
                break;
            default:
                _display.SetBrightness(value);
                break;
        }

        _range!.SetValueWithoutNotify(Read(step));
        ShowValue(step);
        PushPattern();
    }

    private void ShowValue(Step step)
    {
        float value = Read(step);
        string number = value.ToString("0", CultureInfo.InvariantCulture);
        _value!.text = step is Step.Peak or Step.Paper ? _loc.Get("settings.calibration.nits", number) : number;
    }

    private float Read(Step step)
    {
        DisplaySettings display = _config.Config.Display;
        return step switch
        {
            Step.Peak => display.PeakBrightnessNits,
            Step.Black => display.BlackLevel,
            Step.Paper => display.PaperWhiteNits,
            _ => display.Brightness,
        };
    }

    private static (float Min, float Max) RangeOf(Step step) => step switch
    {
        Step.Peak => (DisplaySettings.PeakBrightnessMin, DisplaySettings.PeakBrightnessMax),
        Step.Paper => (DisplaySettings.PaperWhiteMin, DisplaySettings.PaperWhiteMax),
        _ => (DisplaySettings.AdjustmentMin, DisplaySettings.AdjustmentMax),
    };

    private static float StepOf(Step step) => step is Step.Peak or Step.Paper ? DisplaySettings.BrightnessStepNits : 1f;

    private static float DefaultOf(Step step) => step switch
    {
        Step.Peak => DisplaySettings.DefaultPeakBrightness,
        Step.Black => DisplaySettings.DefaultBlackLevel,
        Step.Paper => DisplaySettings.DefaultPaperWhite,
        _ => DisplaySettings.DefaultBrightness,
    };

    private void PushPattern()
    {
        Step step = _order[_at];
        CalibrationPattern pattern = step switch
        {
            Step.Peak => CalibrationPattern.PeakMark,
            Step.Paper => CalibrationPattern.PaperWhite,
            _ => CalibrationPattern.BlackMarks,
        };
        PostProcessRuntimeState.SetCalibrationPattern(pattern, _config.Config.Display.PeakBrightnessNits);
    }

    // Размеры узора по макету: квадрат пика — 78 % высоты сцены; тройка — по 22 % ширины с зазором 4 %;
    // сцена белой точки — 16:7, высотой не больше 320 и 86 % сцены, шириной не больше 90 %
    private void LayoutPattern()
    {
        if (_tree == null || _stage == null)
        {
            return;
        }

        float width = _stage.layout.width;
        float height = _stage.layout.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
        {
            return;
        }

        SetSquare(Require<VisualElement>("CalibPeakTile"), height * 0.78f);

        float figure = width * 0.22f;
        for (int i = 0; i < 3; i++)
        {
            VisualElement mark = Require<VisualElement>("CalibMark" + i);
            SetSquare(mark, figure);
            mark.parent.style.marginLeft = width * 0.02f;
            mark.parent.style.marginRight = width * 0.02f;
        }

        float sceneHeight = Mathf.Min(320f, height * 0.86f);
        float sceneWidth = Mathf.Min(sceneHeight * 16f / 7f, width * 0.9f);
        sceneHeight = Mathf.Min(sceneHeight, sceneWidth * 7f / 16f);
        VisualElement scene = Require<VisualElement>("CalibScene");
        scene.style.width = sceneWidth;
        scene.style.height = sceneHeight;

        // Места узора — после раскладки: рамки получат размеры в следующем проходе
        _tree.schedule.Execute(PublishRects);
    }

    private static void SetSquare(VisualElement element, float side)
    {
        element.style.width = side;
        element.style.height = side;
    }

    private void PublishRects()
    {
        if (_tree?.panel == null)
        {
            return;
        }

        Rect panel = _tree.panel.visualTree.layout;
        Vector4 Normalize(VisualElement element)
        {
            Rect box = element.worldBound;
            return panel.width <= 0f || panel.height <= 0f
                ? Vector4.zero
                : new Vector4(box.x / panel.width, box.y / panel.height, box.width / panel.width, box.height / panel.height);
        }

        Step step = _order[_at];
        if (step == Step.Peak)
        {
            PostProcessRuntimeState.SetCalibrationRects(Normalize(Require<VisualElement>("CalibPeakTile")), Vector4.zero, Vector4.zero);
        }
        else if (step == Step.Paper)
        {
            PostProcessRuntimeState.SetCalibrationRects(Normalize(Require<VisualElement>("CalibPaperSheet")), Vector4.zero, Vector4.zero);
        }
        else
        {
            PostProcessRuntimeState.SetCalibrationRects(
                Normalize(Require<VisualElement>("CalibMark0")),
                Normalize(Require<VisualElement>("CalibMark1")),
                Normalize(Require<VisualElement>("CalibMark2")));
        }
    }

    // Образец сцены белой точки — карточка робота: ник, прочность ячейками по 5 %, деньги
    private void FillCard()
    {
        Require<Label>("CalibCardName").text = _stats != null && !string.IsNullOrEmpty(_stats.Nickname) ? _stats.Nickname : string.Empty;
        Require<Label>("CalibCardMoney").text = "$" + KernNumbers.Group(_stats?.Money ?? 0);
        float health = _stats != null && _stats.MaxHealth > 0 ? _stats.HealthPercent : 0.7f;
        VisualElement hp = Require<VisualElement>("CalibCardHp");
        hp.Clear();
        int filled = Mathf.RoundToInt(Mathf.Clamp01(health) * 20f);
        for (int i = 0; i < 20; i++)
        {
            var cell = new VisualElement { pickingMode = PickingMode.Ignore };
            cell.AddToClassList("calib-hp__cell");
            cell.EnableInClassList("is-empty", i >= filled);
            hp.Add(cell);
        }
    }

    // Клавиши — из планировщика панели: без клавиатурного фокуса KeyDownEvent не приходит.
    // Escape — закрыть (как «Отмена»), стрелки — шаг ползунка.
    private void PollKeys()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null || _tree == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
        else if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            Nudge(-1);
        }
        else if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            Nudge(1);
        }
    }

    private T Require<T>(string name)
        where T : VisualElement =>
        _tree?.Q<T>(name) ?? throw new InvalidOperationException($"[Calibration] {name} is missing from Calibration.uxml.");
}
