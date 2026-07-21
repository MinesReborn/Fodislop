using System;
using Fodinae.Scripts.UI.Builders;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts
{
    public class PacketUIBuilder
    {
        private readonly PacketUIBuilderFactory _builderFactory = new();

        public VisualElement Build(IGUIComponentPacket packet)
        {
            var builder = _builderFactory.CreateBuilder(packet);
            VisualElement element;

            if (builder != null)
            {
                element = builder.Build(packet, this);
            }
            else
            {
                element = new Label($"[Unimplemented: {packet.GetType().Name}]");
                element.style.backgroundColor = Color.magenta;
            }

            StyleApplicator.ApplyStyles(element, packet);
            ApplyAttachedProperties(element, packet);

            element.userData = packet;

            return element;
        }

        private static void ApplyAttachedProperties(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.AttachedProperties == null || packet.AttachedProperties.Length == 0)
            {
                return;
            }

            foreach (var prop in packet.AttachedProperties)
            {
                if (prop.Key == "Canvas.X" && float.TryParse(prop.Value, out float left))
                {
                    element.style.position = Position.Absolute;
                    element.style.left = left;
                }

                if (prop.Key == "Canvas.Y" && float.TryParse(prop.Value, out float top))
                {
                    element.style.position = Position.Absolute;
                    element.style.top = top;
                }

                if (prop.Key == "Canvas.Width" && float.TryParse(prop.Value, out float width))
                {
                    element.style.position = Position.Absolute;
                    element.style.width = width;
                }

                if (prop.Key == "Canvas.Height" && float.TryParse(prop.Value, out float height))
                {
                    element.style.position = Position.Absolute;
                    element.style.height = height;
                }
            }
        }
    }
}
