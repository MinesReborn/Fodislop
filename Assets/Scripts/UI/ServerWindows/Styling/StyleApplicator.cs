#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Controls;

namespace Kern.UI.ServerWindows;

public static class StyleApplicator
{
    public static void ApplyStyles(VisualElement element, IGUIComponentPacket packet)
    {
        if (packet.Style is null)
        {
            return;
        }

        var style = packet.Style.Value;
        IStyle elementStyle = element.style;

        if (element is not UILine)
        {
            elementStyle.backgroundColor = ToUnityColor(style.Background);
            elementStyle.borderTopWidth = style.BorderWidth;
            elementStyle.borderBottomWidth = style.BorderWidth;
            elementStyle.borderLeftWidth = style.BorderWidth;
            elementStyle.borderRightWidth = style.BorderWidth;

            Color border = ToUnityColor(style.Border);
            elementStyle.borderTopColor = border;
            elementStyle.borderBottomColor = border;
            elementStyle.borderLeftColor = border;
            elementStyle.borderRightColor = border;
        }

        ApplyMargins(style.Margin,
            left => elementStyle.marginLeft = left,
            top => elementStyle.marginTop = top,
            right => elementStyle.marginRight = right,
            bottom => elementStyle.marginBottom = bottom);

        ApplyMargins(style.Padding,
            left => elementStyle.paddingLeft = left,
            top => elementStyle.paddingTop = top,
            right => elementStyle.paddingRight = right,
            bottom => elementStyle.paddingBottom = bottom);
    }

    private static Color ToUnityColor(System.Drawing.Color color) =>
        new Color32(color.R, color.G, color.B, color.A);

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
