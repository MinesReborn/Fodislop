#nullable enable

using Kern.Core.Localization;
using UnityEngine.UIElements;
using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.UI.ServerWindows;

internal static class PacketCloseActionCaption
{
    public static void Apply(VisualElement element, ILocalizationService localization)
    {
        if (element.userData is IGUIComponentPacket packet &&
            TextPacketBuilder.IsCloseAction(packet) &&
            element is Button button)
        {
            button.text = localization.Get("common.close");
        }

        foreach (VisualElement child in element.Children())
        {
            Apply(child, localization);
        }
    }
}
