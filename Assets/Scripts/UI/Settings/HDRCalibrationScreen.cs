#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

// Калибровка дисплея. Отдельный экран, а не два ползунка во вкладке, по одной
// причине: данные, которые дисплей сообщает о своей яркости, врут, поэтому
// detectPaperWhite и detectBrightnessLimits выключены (HdrOutputReconciler), и
// единственный достоверный источник этих двух чисел — глаз человека перед
// узором с известной яркостью.
//
// Узор рисует проход вывода (PostProcess.compute, _CalibrationPattern): только
// там величина задаётся в долях paper white и не будет переделана ни кривой,
// ни грейдом. UI поверх узора несёт объяснение и ползунок, но не образец: белый
// UI и есть paper white, и им нельзя показать ни пик, ни ступени над ним.
internal sealed class HDRCalibrationScreen
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly DisplayManager _displayManager;
    private readonly ILocalizationService _loc;

    private VisualElement? _overlay;

    // Свой список обновлений, а не общий список меню: ползунки этого экрана
    // живут ровно столько, сколько он открыт. Пустой массив сюда передать
    // нельзя — фабрика ползунка в него пишет.
    private readonly List<Action> _refreshers = new();

    public HDRCalibrationScreen(
        UIDocument doc,
        IClientConfigManager clientConfig,
        DisplayManager displayManager,
        ILocalizationService loc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _clientConfig = clientConfig ?? throw new ArgumentNullException(nameof(clientConfig));
        _displayManager = displayManager ?? throw new ArgumentNullException(nameof(displayManager));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
    }

    private enum Step
    {
        PaperWhite,
        Peak,
    }

    public void Open()
    {
        // Экран уже открыт — повторный вызов ничего не значит и молчит.
        if (_overlay != null)
        {
            return;
        }

        if (_doc == null || _doc.rootVisualElement == null)
        {
            // А вот это сбой: калибровку попросили, а показать её негде.
            // Молча выйти значило бы оставить человека без единственного
            // способа настроить яркость и без следа в консоли.
            Debug.LogError(
                "[HDRCalibration] Calibration was requested before the UI panel was ready; " +
                "the screen cannot be shown.");
            return;
        }

        var overlay = new VisualElement();
        overlay.name = "HDRCalibrationOverlay";
        overlay.AddToClassList("ui-overlay");
        overlay.AddToClassList("ui-overlay--modal");
        _overlay = overlay;

        // Панель прижата к низу: узор занимает середину экрана, и объяснение
        // не должно стоять поверх того, что человек сравнивает глазами.
        var panel = new VisualElement();
        panel.AddToClassList("pause-confirm-panel");
        panel.AddToClassList("ui-panel");
        panel.AddToClassList("ui-panel--modal");
        panel.style.alignSelf = Align.Center;
        panel.style.marginTop = StyleKeyword.Auto;

        var title = new Label();
        title.AddToClassList("pause-confirm-title");
        panel.Add(title);

        var description = new Label();
        description.AddToClassList("pause-confirm-desc");
        panel.Add(description);

        var sliderHost = new VisualElement();
        panel.Add(sliderHost);

        var buttons = new VisualElement();
        buttons.AddToClassList("pause-confirm-buttons");
        buttons.AddToClassList("ui-actions-row");

        var nextButton = new Button();
        nextButton.AddToClassList("pause-btn-confirm");
        var closeButton = new Button(Close);
        closeButton.text = _loc.Get("common.cancel");
        closeButton.AddToClassList("pause-btn");
        buttons.Add(nextButton);
        buttons.Add(closeButton);
        panel.Add(buttons);

        Step step = Step.PaperWhite;

        void ShowStep()
        {
            sliderHost.Clear();
            _refreshers.Clear();
            if (step == Step.PaperWhite)
            {
                title.text = _loc.Get("settings.display.calibration_paper_title");
                description.text = _loc.Get("settings.display.calibration_paper_desc");
                nextButton.text = _loc.Get("settings.display.calibration_next");
                sliderHost.Add(PauseMenuUIFactory.CreateBoundSlider(
                    _loc.Get("settings.display.paper_white"),
                    () => _clientConfig.Config.Display.PaperWhiteNits,
                    value =>
                    {
                        _displayManager.SetPaperWhiteNits(value);
                        PushPattern(step);
                    },
                    DisplaySettings.PaperWhiteMin,
                    DisplaySettings.PaperWhiteMax,
                    _refreshers,
                    DisplaySettings.BrightnessStepNits));
            }
            else
            {
                title.text = _loc.Get("settings.display.calibration_peak_title");
                description.text = _loc.Get("settings.display.calibration_peak_desc");
                nextButton.text = _loc.Get("settings.display.calibration_done");
                sliderHost.Add(PauseMenuUIFactory.CreateBoundSlider(
                    _loc.Get("settings.display.peak_brightness"),
                    () => _clientConfig.Config.Display.PeakBrightnessNits,
                    value =>
                    {
                        _displayManager.SetPeakBrightnessNits(value);
                        PushPattern(step);
                    },
                    DisplaySettings.PeakBrightnessMin,
                    DisplaySettings.PeakBrightnessMax,
                    _refreshers,
                    DisplaySettings.BrightnessStepNits));
            }

            PushPattern(step);
        }

        nextButton.clicked += () =>
        {
            if (step == Step.PaperWhite)
            {
                step = Step.Peak;
                ShowStep();
                return;
            }

            Close();
        };

        overlay.Add(panel);
        _doc.rootVisualElement.Add(overlay);
        ShowStep();
    }

    public void Close()
    {
        // Узор гасится первым: если экран почему-то не снимется, игра всё
        // равно должна вернуться на место, а не остаться под белым полем.
        PostProcessRuntimeState.SetCalibrationPattern(CalibrationPattern.Off, 0f);
        if (_overlay != null)
        {
            _overlay.RemoveFromHierarchy();
            _overlay = null;
        }
    }

    private void PushPattern(Step step)
    {
        DisplaySettings display = _clientConfig.Config.Display;
        if (step == Step.PaperWhite)
        {
            PostProcessRuntimeState.SetCalibrationPattern(
                CalibrationPattern.PaperWhite,
                display.PaperWhiteNits);
            return;
        }

        PostProcessRuntimeState.SetCalibrationPattern(
            CalibrationPattern.PeakLadder,
            display.PeakBrightnessNits);
    }
}
