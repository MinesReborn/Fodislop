#nullable enable

using System;
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
        return element;
    }
}
