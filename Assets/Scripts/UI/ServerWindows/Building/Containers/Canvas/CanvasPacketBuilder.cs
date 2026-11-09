#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class CanvasPacketBuilder : PacketUIBuilderBase<CanvasPacket>
{
    protected override VisualElement BuildTyped(CanvasPacket packet, PacketUIBuilder builder)
    {
        var element = new VisualElement();
        element.AddToClassList("rel");
        builder.AddChildren(element, packet);

        float measuredBottom = 0f;
        void UpdateExtent(GeometryChangedEvent change)
        {
            float bottom = 0f;
            foreach (VisualElement child in element.Children())
            {
                Rect bounds = child.layout;
                if (float.IsNaN(bounds.yMax) || float.IsInfinity(bounds.yMax))
                {
                    continue;
                }

                bottom = Math.Max(bottom, bounds.yMax);
            }

            if (!Mathf.Approximately(bottom, measuredBottom))
            {
                measuredBottom = bottom;
                element.style.minHeight = bottom;
            }
        }

        foreach (VisualElement child in element.Children())
        {
            child.RegisterCallback<GeometryChangedEvent>(UpdateExtent);
        }

        return element;
    }
}
