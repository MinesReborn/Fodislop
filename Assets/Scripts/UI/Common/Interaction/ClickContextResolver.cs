#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.UI.Controls;
using MinesServer.Networking.Shared.Packets;
using UnityEngine.UIElements;

namespace Kern.UI;

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

        // Determine starting element
        VisualElement current;
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

        var segments = clickContext.Split('/');
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment == "." || segment == "./")
            {
                continue;
            }

            if ((segment == ".." || segment == "../") && current != null)
            {
                current = current.parent;
                continue;
            }

            if (current != null && int.TryParse(segment, out int index))
            {
                var children = current.Children().ToList();
                if (index >= 0 && index < children.Count)
                {
                    current = children[index];
                }
            }
        }

        return current;
    }

    /// <param name="root">The root element to traverse.</param>
    /// <returns>Array of StringPairPacket with input names and their current values.</returns>
    public static StringPairPacket[] CollectInputValues(VisualElement? root)
    {
        if (root == null)
        {
            return [];
        }

        List<StringPairPacket> result = [];
        CollectRecursive(root, result);
        return [.. result];
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
        Slider sl => sl.value.ToString(),
        Toggle tg => tg.value.ToString(),
        Selectable sel => sel.value.ToString(),
        _ => string.Empty,
    };
}
