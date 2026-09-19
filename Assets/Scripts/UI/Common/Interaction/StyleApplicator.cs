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

        if (style.Background.A > 0)
        {
            element.style.backgroundColor = ConvertColor(style.Background);
        }

        if (style.BorderWidth > 0)
        {
            Color border = ConvertColor(style.Border);
            element.style.borderTopColor = border;
            element.style.borderBottomColor = border;
            element.style.borderLeftColor = border;
            element.style.borderRightColor = border;

            element.style.borderTopWidth = style.BorderWidth;
            element.style.borderBottomWidth = style.BorderWidth;
            element.style.borderLeftWidth = style.BorderWidth;
            element.style.borderRightWidth = style.BorderWidth;
        }

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
        if (margins.Left > 0)
        {
            left(margins.Left);
        }

        if (margins.Top > 0)
        {
            top(margins.Top);
        }

        if (margins.Right > 0)
        {
            right(margins.Right);
        }

        if (margins.Bottom > 0)
        {
            bottom(margins.Bottom);
        }
    }

    public static Color ConvertColor(System.Drawing.Color color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}
