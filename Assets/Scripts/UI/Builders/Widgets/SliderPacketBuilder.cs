#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class SliderPacketBuilder : PacketUIBuilderBase<SliderPacket>
    {
        protected override VisualElement BuildTyped(SliderPacket packet, PacketUIBuilder builder)
        {
            var slider = new Slider(packet.MinValue, packet.MaxValue)
            {
                value = Mathf.Clamp(packet.DefaultValue, packet.MinValue, packet.MaxValue),
            };

            // Вид гасится правилами .packet-slider в SciFi.uss: сервер прислал
            // свой ползунок, стандартную отрисовку Unity надо убрать из-под него.
            slider.AddToClassList("packet-slider");

            VisualElement? dragger = slider.Q(className: "unity-base-slider__dragger");
            if (dragger == null)
            {
                Debug.LogWarning("[PacketUI] Slider dragger is unavailable in this UI Toolkit version.");
                return slider;
            }

            dragger.Clear();
            dragger.Add(builder.Build(packet.Knob));
            slider.SetEnabled(packet.IsEnabled);
            return slider;
        }
    }
}
