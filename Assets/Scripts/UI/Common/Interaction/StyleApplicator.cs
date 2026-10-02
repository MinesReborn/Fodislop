#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;

public static class StyleApplicator
{
    public static void ApplyStyles(VisualElement element, IGUIComponentPacket packet)
    {
        if (packet.Style is null)
        {
            return;
        }

        var style = packet.Style.Value;

        element.style.borderTopWidth = style.BorderWidth;
        element.style.borderBottomWidth = style.BorderWidth;
        element.style.borderLeftWidth = style.BorderWidth;
        element.style.borderRightWidth = style.BorderWidth;

        ApplyMargins(style.Margin,
            left => element.style.marginLeft = left,
            top => element.style.marginTop = top,
            right => element.style.marginRight = right,
            bottom => element.style.marginBottom = bottom);

        ApplyMargins(style.Padding,
            left => element.style.paddingLeft = left,
            top => element.style.paddingTop = top,
            right => element.style.paddingRight = right,
            bottom => element.style.paddingBottom = bottom);
    }

    private static void ApplyMargins(
        Margins margins,
        System.Action<int> left,
        System.Action<int> top,
        System.Action<int> right,
        System.Action<int> bottom)
    {
        left(margins.Left);
        top(margins.Top);
        right(margins.Right);
        bottom(margins.Bottom);
    }

}
