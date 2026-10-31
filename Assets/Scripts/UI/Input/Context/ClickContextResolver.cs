#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kern.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine.UIElements;

namespace Kern.UI.Input;

public static class ClickContextResolver
{
    /// <param name="clickedElement">The element that was clicked.</param>
    /// <param name="windowRoot">The root VisualElement of the window.</param>
    /// <param name="clickContext">The click context path string (e.g. "../../0/0/2").</param>
    /// <returns>The root element from which to traverse for input controls.</returns>
    public static VisualElement? ResolveRoot(VisualElement clickedElement, VisualElement windowRoot, string? clickContext)
    {
        if (string.IsNullOrEmpty(clickContext))
        {
            return clickedElement;
        }

        VisualElement? current;
        if (clickContext[0] == '/')
        {
            current = windowRoot;
            clickContext = clickContext.Substring(1);
        }
        else
        {
            current = clickedElement;
        }

        if (string.IsNullOrEmpty(clickContext))
        {
            return current;
        }

        string[] segments = clickContext.Split('/');
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment == ".")
            {
                continue;
            }

            if (current == null)
            {
                return null;
            }

            if (segment == "..")
            {
                current = PacketParent(current, windowRoot);
                continue;
            }

            if (!int.TryParse(segment, out int index) ||
                current.userData is not IContainerComponentPacket container ||
                index < 0 || index >= container.Children.Count)
            {
                return null;
            }

            current = FindDirectPacketChild(current, container.Children[index]);
        }

        return current;
    }

    private static VisualElement? PacketParent(VisualElement element, VisualElement windowRoot)
    {
        for (VisualElement? parent = element.parent; parent != null; parent = parent.parent)
        {
            if (parent.userData is IGUIComponentPacket)
            {
                return parent;
            }

            if (ReferenceEquals(parent, windowRoot))
            {
                return null;
            }
        }

        return null;
    }

    private static VisualElement? FindDirectPacketChild(
        VisualElement parent,
        IGUIComponentPacket target)
    {
        foreach (VisualElement child in parent.Children())
        {
            VisualElement? found = FindThroughLayout(child, target);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static VisualElement? FindThroughLayout(
        VisualElement element,
        IGUIComponentPacket target)
    {
        if (element.userData is IGUIComponentPacket packet)
        {
            return ReferenceEquals(packet, target) ? element : null;
        }

        foreach (VisualElement child in element.Children())
        {
            VisualElement? found = FindThroughLayout(child, target);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <param name="root">The root element to traverse.</param>
    /// <returns>Array of StringPairPacket with input names and their current values.</returns>
    public static StringPairPacket[] CollectInputValues(VisualElement? root)
    {
        if (root == null)
        {
            return Array.Empty<StringPairPacket>();
        }

        List<StringPairPacket> result = new();
        CollectRecursive(root, result);
        return result.ToArray();
    }

    private static void CollectRecursive(VisualElement element, List<StringPairPacket> result)
    {
        if (!string.IsNullOrEmpty(element.name) && IsInputElement(element))
        {
            result.Add(new StringPairPacket(element.name, GetControlValue(element)));
        }

        foreach (var child in element.Children())
        {
            CollectRecursive(child, result);
        }
    }

    private static bool IsInputElement(VisualElement element) =>
        element is TextField
            || element is DropdownField
            || element is Slider
            || element is Toggle
            || element is Selectable;

    private static string GetControlValue(VisualElement element) => element switch
    {
        TextField tf => tf.value,
        DropdownField dd => dd.value,
        Slider sl => sl.value.ToString(CultureInfo.InvariantCulture),
        Toggle tg => tg.value.ToString(),
        Selectable sel => sel.value.ToString(),
        _ => string.Empty,
    };
}
