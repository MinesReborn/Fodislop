#nullable enable

using System;
using System.Globalization;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class CanvasPacketBuilder : PacketUIBuilderBase<CanvasPacket>
{
    protected override VisualElement BuildTyped(CanvasPacket packet, PacketUIBuilder builder)
    {
        var element = new VisualElement();
        element.AddToClassList("rel");
        const string heightKey = "PacketUI.CanvasHeight";
        if (AttachedProperties.Has(packet, heightKey))
        {
            if (!AttachedProperties.TryGetFloat(packet, heightKey, out float parsed) || parsed <= 0f)
            {
                string raw = AttachedProperties.Find(packet, heightKey) ?? string.Empty;
                throw new InvalidOperationException($"[PacketUI] Invalid canvas height '{raw}'.");
            }

            element.style.height = parsed;
        }

        builder.AddChildren(element, packet);

        // Дети канваса позиционируются абсолютно (Canvas.X/Y) и выпадают из
        // потока: без резерва высоты контейнер схлопывается и контент после
        // него наезжает. Держим minHeight по самому нижнему элементу.
        float bottom = 0f;
        foreach (IGUIComponentPacket child in packet.Children)
        {
            if (AttachedProperties.TryGetFloat(child, "Canvas.Y", out float y))
            {
                float childHeight = AttachedProperties.TryGetFloat(child, "Canvas.Height", out float h) ? h : 20f;
                bottom = Math.Max(bottom, y + childHeight);
            }
        }

        if (bottom > 0f)
        {
            element.style.minHeight = bottom;
        }

        return element;
    }
}
