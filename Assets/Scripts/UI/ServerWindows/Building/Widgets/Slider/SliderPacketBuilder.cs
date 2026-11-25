#nullable enable

using System;

using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class SliderPacketBuilder : PacketUIBuilderBase<SliderPacket>
{
    protected override VisualElement BuildTyped(SliderPacket packet, PacketUIBuilder builder)
    {
        var slider = new Slider(packet.MinValue, packet.MaxValue)
        {
            name = packet.Name,
            value = Mathf.Clamp(packet.DefaultValue, packet.MinValue, packet.MaxValue),
        };
        slider.SetEnabled(packet.IsEnabled);

        if (packet.Step > 0f)
        {
            slider.RegisterValueChangedCallback(change =>
            {
                float steps = Mathf.Round((change.newValue - packet.MinValue) / packet.Step);
                float snapped = Mathf.Clamp(
                    packet.MinValue + steps * packet.Step,
                    packet.MinValue,
                    packet.MaxValue);
                if (!Mathf.Approximately(change.newValue, snapped))
                {
                    slider.SetValueWithoutNotify(snapped);
                }
            });
        }

        // Вид гасится правилами .packet-slider в SciFi.uss: сервер прислал
        // свой ползунок, стандартную отрисовку Unity надо убрать из-под него.
        slider.AddToClassList("packet-slider");

        bool hasRange = packet.MaxValue > packet.MinValue;
        if (hasRange)
        {
            slider.AddToClassList("packet-slider--filled");
        }

        VisualElement dragger = slider.Q(className: "unity-base-slider__dragger") ??
            throw new InvalidOperationException($"[PacketUI] Slider '{packet.Name}' has no dragger element.");

        // Слайдер без диапазона не имеет ползунка: перетаскивание бессмысленно,
        // поэтому элемент скрыт и не перехватывает указатель.
        if (!hasRange)
        {
            dragger.style.display = DisplayStyle.None;
            dragger.pickingMode = PickingMode.Ignore;
        }

        // Дорожка по умолчанию: тонкая линия во всю ширину слайдера. Кладётся
        // первым ребёнком, чтобы ручка с кнобом рисовались поверх неё. Свой
        // элемент, а не стандартный __tracker: позицию и цвет линии задаём
        // сами в SciFi.uss и не зависим от внутренней геометрии темы Unity.
        var track = new VisualElement();
        track.AddToClassList("packet-slider__track");
        track.pickingMode = PickingMode.Ignore;
        slider.Insert(0, track);

        dragger.Clear();
        VisualElement knob = builder.Build(packet.Knob);
        dragger.style.width = packet.Knob.Width;
        dragger.style.height = packet.Knob.Height;
        // Вертикальное центрирование ручки — только инлайн: top:50% и
        // отрицательный отступ в полвысоты. Слайдер сам ведёт ручку инлайновым
        // translate — SliderUtilities.UpdateDragElementPosition пишет (x, 0)
        // при первом же изменении значения и затирает USS-кроссфейс translate
        // (репорт: до первого сдвига ручка стоит выше дорожки). top/margin
        // движок не трогает — ручка отцентрирована и до, и после сдвига.
        dragger.style.position = Position.Absolute;
        dragger.style.top = Length.Percent(50);
        dragger.style.marginTop = -packet.Knob.Height / 2f;
        dragger.Add(knob);
        return slider;
    }
}
