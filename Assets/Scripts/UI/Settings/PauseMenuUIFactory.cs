#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    internal static class PauseMenuUIFactory
    {
        public static float SnapValue(float rawValue, float min, float max, float step = 0.01f)
        {
            float range = Mathf.Abs(max - min);
            if (range <= 0.0001f)
            {
                return rawValue;
            }

            // 1. Magnetic threshold for integers: 2.5% of total slider range (minimum 0.025)
            float snapThreshold = Mathf.Max(0.025f, range * 0.025f);

            // 2. Check snapping to whole integers (..., -1.0, 0.0, 1.0, 2.0, ...)
            float nearestInt = Mathf.Round(rawValue);
            if (nearestInt >= min - 0.001f && nearestInt <= max + 0.001f)
            {
                if (Mathf.Abs(rawValue - nearestInt) <= snapThreshold)
                {
                    return nearestInt;
                }
            }

            // 3. Check snapping to half-integers (0.5, 1.5, ...) with a tighter radius
            float nearestHalf = Mathf.Round(rawValue * 2f) / 2f;
            if (nearestHalf >= min - 0.001f && nearestHalf <= max + 0.001f)
            {
                if (Mathf.Abs(rawValue - nearestHalf) <= snapThreshold * 0.5f)
                {
                    return nearestHalf;
                }
            }

            // 4. Quantize to clean step to eliminate float noise like 1.00183
            if (step > 0f)
            {
                float snapped = Mathf.Round(rawValue / step) * step;
                return (float)Math.Round(snapped, 2);
            }

            return (float)Math.Round(rawValue, 2);
        }

        public static VisualElement CreateSlider(string labelText, float initialValue, Action<float> onChange, float min, float max)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(min, max);
            float snappedInitial = SnapValue(initialValue, min, max);
            slider.SetValueWithoutNotify(snappedInitial);
            void UpdateLabel(float value)
            {
                label.text = $"{labelText}: {value:F2}";
            }

            UpdateLabel(snappedInitial);
            slider.RegisterValueChangedCallback(evt =>
            {
                float snapped = SnapValue(evt.newValue, min, max);
                if (!Mathf.Approximately(snapped, evt.newValue))
                {
                    slider.SetValueWithoutNotify(snapped);
                }

                UpdateLabel(snapped);
                onChange(snapped);
            });
            container.Add(slider);

            return container;
        }

        /// <summary>
        /// Ползунок, берущий границы и подпись из объявления поля секции.
        /// </summary>
        /// <remarks>
        /// ЗАЧЕМ. Диапазон настройки объявлен над её полем
        /// (<c>[SettingRange]</c>). Перегрузка с явными <c>minimum</c> и
        /// <c>maximum</c> делала билдер четвёртым местом, где записан тот же
        /// отрезок, и эти четыре записи расходились: ползунок мог показывать
        /// диапазон, который валидатор не принимает.
        ///
        /// Имя поля проверяется в <see cref="SettingSchema.RangeOf{TSection}"/>
        /// и падает при опечатке, а не показывает игроку неверные границы.
        /// </remarks>
        public static VisualElement CreateBoundSlider<TSection>(
            string fieldName,
            ILocalizationService loc,
            Func<float> readValue,
            Action<float> onChange,
            ICollection<Action> refreshers)
            where TSection : class, new()
        {
            SettingRangeAttribute range = SettingSchema.RangeOf<TSection>(fieldName);
            return CreateBoundSlider(
                loc.Get(SettingSchema.LabelOf<TSection>(fieldName)),
                readValue,
                onChange,
                range.Minimum,
                range.Maximum,
                refreshers);
        }

        public static VisualElement CreateBoundSlider(
            string labelText,
            Func<float> readValue,
            Action<float> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");

            var label = new Label();
            label.AddToClassList("pause-slider-label");
            container.Add(label);

            var slider = new Slider(minimum, maximum);
            void Refresh()
            {
                float value = SnapValue(readValue(), minimum, maximum);
                slider.SetValueWithoutNotify(value);
                label.text = $"{labelText}: {value:F2}";
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                float snapped = SnapValue(evt.newValue, minimum, maximum);
                if (!Mathf.Approximately(snapped, evt.newValue))
                {
                    slider.SetValueWithoutNotify(snapped);
                }

                label.text = $"{labelText}: {snapped:F2}";
                onChange(snapped);
            });
            container.Add(slider);
            refreshers.Add(Refresh);
            Refresh();
            return container;
        }

        public static VisualElement CreateBoundColorControls(
            string labelText,
            Func<Color> readValue,
            Action<Color> onChange,
            float minimum,
            float maximum,
            ICollection<Action> refreshers)
        {
            var container = new VisualElement();
            container.AddToClassList("pause-slider-container");
            container.Add(CreateLabel(labelText));
            container.Add(CreateBoundSlider(
                $"{labelText} R",
                () => readValue().r,
                value =>
                {
                    Color color = readValue();
                    color.r = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} G",
                () => readValue().g,
                value =>
                {
                    Color color = readValue();
                    color.g = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            container.Add(CreateBoundSlider(
                $"{labelText} B",
                () => readValue().b,
                value =>
                {
                    Color color = readValue();
                    color.b = value;
                    onChange(color);
                },
                minimum,
                maximum,
                refreshers));
            return container;
        }

        public static Toggle CreateBoundToggle(
            string label,
            Func<bool> readValue,
            Action<bool> onChange,
            ICollection<Action> refreshers)
        {
            var toggle = new Toggle(label);
            void Refresh()
            {
                toggle.SetValueWithoutNotify(readValue());
            }

            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            refreshers.Add(Refresh);
            Refresh();
            return toggle;
        }

        public static Button CreateBoundCycleButton(
            Func<string> readLabel,
            Action onCycle,
            ICollection<Action> refreshers)
        {
            var btn = new Button();
            void Refresh()
            {
                btn.text = readLabel();
            }

            btn.clicked += () =>
            {
                onCycle();
                Refresh();
            };
            btn.AddToClassList("pause-btn");
            refreshers.Add(Refresh);
            Refresh();
            return btn;
        }

        public static Button CreateButton(string text, Action action)
        {
            var btn = new Button(action);
            btn.text = text;
            btn.AddToClassList("pause-btn");
            return btn;
        }

        public static Label CreateLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("pause-slider-label");
            return label;
        }

        public static void ShowConfirmation(UIDocument doc, string title, string description, string confirmText, Action onConfirm, ILocalizationService loc)
        {
            if (doc == null || doc.rootVisualElement == null)
            {
                // Защитный гард: без готового документа показывать подтверждение
                // негде; вызывающий сам решает, когда документ готов.
                return;
            }

            var root = doc.rootVisualElement;

            var overlay = new VisualElement();
            overlay.name = "ConfirmOverlay";
            overlay.AddToClassList("pause-confirm-overlay");
            overlay.AddToClassList("ui-overlay");
            overlay.AddToClassList("ui-overlay--modal");

            var panel = new VisualElement();
            panel.AddToClassList("pause-confirm-panel");
            panel.AddToClassList("ui-panel");
            panel.AddToClassList("ui-panel--modal");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("pause-confirm-title");
            panel.Add(titleLabel);

            var descLabel = new Label(description);
            descLabel.AddToClassList("pause-confirm-desc");
            panel.Add(descLabel);

            var buttonsRow = new VisualElement();
            buttonsRow.AddToClassList("pause-confirm-buttons");
            buttonsRow.AddToClassList("ui-actions-row");

            var confirmBtn = new Button(() =>
            {
                root.Remove(overlay);
                onConfirm();
            });
            confirmBtn.text = confirmText;
            confirmBtn.AddToClassList("pause-btn-confirm");

            var cancelBtn = new Button(() => root.Remove(overlay));
            cancelBtn.text = loc.Get("common.cancel");
            cancelBtn.AddToClassList("pause-btn");

            buttonsRow.Add(confirmBtn);
            buttonsRow.Add(cancelBtn);
            panel.Add(buttonsRow);

            overlay.Add(panel);
            root.Add(overlay);
        }
    }
}
