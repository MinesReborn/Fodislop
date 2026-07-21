using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class SliderPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not SliderPacket sliderPkt)
            {
                return null;
            }

            var slider = new Slider(sliderPkt.MinValue, sliderPkt.MaxValue)
            {
                value = Mathf.Clamp(sliderPkt.DefaultValue, sliderPkt.MinValue, sliderPkt.MaxValue),
            };
            var knob = builder.Build(sliderPkt.Knob);
            if (knob == null)
            {
                Debug.LogError("Slider knob is null");
                return slider;
            }

            var tracker = slider.Q(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.backgroundImage = null;
                tracker.style.borderTopColor = Color.clear;
                tracker.style.borderBottomColor = Color.clear;
                tracker.style.borderLeftColor = Color.clear;
                tracker.style.borderRightColor = Color.clear;
            }

            var dragContainer = slider.Q(className: "unity-base-slider__drag-container");
            if (dragContainer == null)
            {
                Debug.LogError("Slider drag container not found.");
                return slider;
            }

            dragContainer.style.backgroundColor = Color.clear;

            var dragger = dragContainer.Q(className: "unity-base-slider__dragger");
            if (dragger == null)
            {
                Debug.LogError("Slider dragger not found inside drag container.");
                return slider;
            }

            dragger.style.backgroundImage = null;
            dragger.style.backgroundColor = Color.clear;
            dragger.style.borderTopColor = Color.clear;
            dragger.style.borderBottomColor = Color.clear;
            dragger.style.borderLeftColor = Color.clear;
            dragger.style.borderRightColor = Color.clear;

            dragger.style.alignItems = Align.Center;
            dragger.style.justifyContent = Justify.Center;
            dragger.Clear();
            dragger.Add(knob);
            slider.SetEnabled(sliderPkt.IsEnabled);
            return slider;
        }
    }
}
