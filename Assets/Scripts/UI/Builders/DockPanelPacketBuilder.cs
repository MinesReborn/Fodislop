using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class DockPanelPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not DockPanelPacket dpp)
                return null;

            var element = new VisualElement
            {
                style =
                {
                    flexGrow = 1
                }
            };
            HandleDockPanelChildren(element, dpp.Children, builder);
            return element;
        }

        private void HandleDockPanelChildren(VisualElement parent, IEnumerable<IGUIComponentPacket> children, PacketUIBuilder builder)
        {
            parent.style.flexDirection = FlexDirection.Column;
            parent.style.flexGrow = 0;

            var lastChild = children.LastOrDefault(c => c.AttachedProperties?.All(p => p.Key != "DockPanel.Dock") ?? true);
            VisualElement current;
            if (lastChild != null)
            {
                current = builder.Build(lastChild);
                current.style.flexGrow = 1;
            }
            else
            {
                current = new VisualElement { style = { flexGrow = 1 } };
            }

            foreach (var childPacket in children.Reverse())
            {
                if (childPacket == lastChild) continue;

                var childElement = builder.Build(childPacket);
                var dock = Dock.Left;
                var dockProp = childPacket.AttachedProperties?.FirstOrDefault(p => p.Key == "DockPanel.Dock");
                if (dockProp != null)
                {
                    Enum.TryParse(dockProp.Value.Value, true, out dock);
                }
                else if (childPacket != lastChild)
                {
                    parent.Add(childElement);
                    continue;
                }

                var wrapper = new VisualElement { style = { flexGrow = 1, alignSelf = Align.Auto } };
                childElement.style.flexShrink = 0;

                switch (dock)
                {
                    case Dock.Top:
                        wrapper.style.flexDirection = FlexDirection.Column;
                        wrapper.Add(childElement);
                        wrapper.Add(current);
                        break;
                    case Dock.Bottom:
                        wrapper.style.flexDirection = FlexDirection.Column;
                        wrapper.Add(current);
                        wrapper.Add(childElement);
                        break;
                    case Dock.Left:
                        wrapper.style.flexDirection = FlexDirection.Row;
                        wrapper.style.alignItems = Align.FlexStart;
                        wrapper.Add(childElement);
                        wrapper.Add(current);
                        break;
                    case Dock.Right:
                        wrapper.style.flexDirection = FlexDirection.Row;
                        wrapper.style.alignItems = Align.FlexStart;
                        wrapper.Add(current);
                        wrapper.Add(childElement);
                        break;
                }
                current = wrapper;
            }

            parent.Add(current);
        }
    }
}
