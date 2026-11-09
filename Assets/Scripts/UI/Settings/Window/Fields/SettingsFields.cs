#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using Kern.UI.Controls;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Строки окна настроек — макет visual/kern-ui (screens.css .set-page .field, .seg, .range, .keycap, .set-sec):
// подпись слева, управление справа, пунктир снизу. Каждое управление при открытии окна перечитывает
// живое значение (refreshers), а не показывает то, что было при сборке.
internal static class SettingsFields
{
    public static VisualElement Row(string label, VisualElement control)
    {
        var row = new VisualElement();
        row.AddToClassList("field");
        var caption = new Label(label);
        caption.AddToClassList("field__label");
        row.Add(caption);
        row.Add(control);
        return row;
    }

    public static VisualElement Switch(
        string label,
        Func<bool> read,
        Action<bool> write,
        ICollection<Action> refreshers,
        out Toggle toggle)
    {
        var control = new Toggle();
        control.AddToClassList("switch");
        control.RegisterValueChangedCallback(evt => write(evt.newValue));
        refreshers.Add(() => control.SetValueWithoutNotify(read()));
        control.SetValueWithoutNotify(read());
        toggle = control;
        return Row(label, control);
    }

    public static VisualElement Switch(string label, Func<bool> read, Action<bool> write, ICollection<Action> refreshers) =>
        Switch(label, read, write, refreshers, out _);

    public static VisualElement Select(
        string label,
        IReadOnlyList<string> choices,
        Func<int> readIndex,
        Action<int> write,
        ICollection<Action> refreshers,
        out DropdownField field)
    {
        var control = new DropdownField { choices = new List<string>(choices) };
        control.AddToClassList("select");
        void Refresh()
        {
            int index = readIndex();
            if (index >= 0 && index < control.choices.Count)
            {
                control.SetValueWithoutNotify(control.choices[index]);
            }
        }

        control.RegisterValueChangedCallback(_ =>
        {
            if (control.index >= 0)
            {
                write(control.index);
            }
        });
        refreshers.Add(Refresh);
        Refresh();
        field = control;
        return Row(label, control);
    }

    public static VisualElement Select(
        string label,
        IReadOnlyList<string> choices,
        Func<int> readIndex,
        Action<int> write,
        ICollection<Action> refreshers) =>
        Select(label, choices, readIndex, write, refreshers, out _);

    // Сегменты: выбран один (макет .seg, кнопка выбранного — is-on)
    public static VisualElement Segment(
        string label,
        IReadOnlyList<string> choices,
        Func<int> readIndex,
        Action<int> write,
        ICollection<Action> refreshers)
    {
        var group = new VisualElement();
        group.AddToClassList("seg");
        var buttons = new List<Button>();
        void Mark(int selected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].EnableInClassList("is-on", i == selected);
            }
        }

        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var button = new Button(() =>
            {
                Mark(index);
                write(index);
            })
            {
                text = choices[i],
                focusable = false,
            };
            button.AddToClassList("seg__btn");
            buttons.Add(button);
            group.Add(button);
        }

        refreshers.Add(() => Mark(readIndex()));
        Mark(readIndex());
        return Row(label, group);
    }

    // Ползунок с числом справа (макет .range: дорожка и «85%»)
    public static VisualElement Range(
        string label,
        float minimum,
        float maximum,
        Func<float> read,
        Action<float> write,
        Func<float, string> format,
        ICollection<Action> refreshers,
        float quantum = 0.01f)
    {
        var range = new VisualElement();
        range.AddToClassList("range");
        var slider = new Slider(minimum, maximum) { fill = true };
        slider.AddToClassList("range__slider");
        KernRange.Decorate(slider);
        var output = new Label();
        output.AddToClassList("range__out");
        range.Add(slider);
        range.Add(output);

        float Quantize(float value) => quantum > 0f
            ? Mathf.Clamp(Mathf.Round(value / quantum) * quantum, minimum, maximum)
            : value;

        void Refresh()
        {
            float value = Quantize(read());
            slider.SetValueWithoutNotify(value);
            output.text = format(value);
        }

        slider.RegisterValueChangedCallback(evt =>
        {
            float value = Quantize(evt.newValue);
            output.text = format(value);
            write(value);
        });
        refreshers.Add(Refresh);
        Refresh();
        return Row(label, range);
    }

    // Строка с кнопкой справа (макет .set-btn: «Калибровка дисплея — Настроить»)
    public static VisualElement ButtonRow(string label, string buttonText, Action click, out Button button)
    {
        var control = new Button(click) { text = buttonText };
        control.AddToClassList("btn");
        control.AddToClassList("set-btn");
        button = control;
        return Row(label, control);
    }

    // Подзаголовок раздела страницы (макет .set-sec с табличкой .sec-plate)
    public static VisualElement Section(string title)
    {
        var section = new VisualElement();
        section.AddToClassList("set-sec");
        var plate = new Label(title);
        plate.AddToClassList("sec-plate");
        plate.AddToClassList("caps");
        section.Add(plate);
        return section;
    }

    // Клавиша (макет .keycap); is-fixed — показ без переназначения
    public static Button Keycap(string text, Action? click)
    {
        var key = new Button { text = text, focusable = false };
        key.AddToClassList("keycap");
        if (click == null)
        {
            key.AddToClassList("is-fixed");
            key.pickingMode = PickingMode.Ignore;
        }
        else
        {
            key.clicked += click;
        }

        return key;
    }

    // Низ страницы с кнопкой справа (макет .set-foot)
    public static VisualElement Foot(string buttonText, Action click)
    {
        var foot = new VisualElement();
        foot.AddToClassList("set-foot");
        var button = new Button(click) { text = buttonText };
        button.AddToClassList("btn");
        button.AddToClassList("set-btn");
        foot.Add(button);
        return foot;
    }
}
