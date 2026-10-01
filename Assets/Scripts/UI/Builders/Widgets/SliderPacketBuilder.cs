#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
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
        // Name обязателен: ClickContextResolver.CollectInputValues собирает
        // значения контролов по element.name и отдаёт их в ElementClickPacket.
        if (!string.IsNullOrEmpty(packet.Name))
        {
            slider.name = packet.Name;
        }

        VisualElement? dragger = slider.Q(className: "unity-base-slider__dragger");
        if (dragger == null)
        {
            Debug.LogWarning("[PacketUI] Slider dragger is unavailable in this UI Toolkit version.");
            return slider;
        }

        dragger.Clear();
        dragger.Add(builder.Build(packet.Knob));
        return slider;
    }
}
