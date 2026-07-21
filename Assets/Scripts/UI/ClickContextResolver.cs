using System.Collections.Generic;
using System.Linq;
using Fodinae.UI.Controls;
using MinesServer.Networking.Shared.Packets;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Resolves click context path strings against the VisualElement tree
    /// and collects input control values from a root element.
    /// </summary>
    public static class ClickContextResolver
    {
        /// <summary>
        /// Resolves a click context path to find the root element for input traversal.
        /// </summary>
        /// <param name="clickedElement">The element that was clicked.</param>
        /// <param name="windowRoot">The root VisualElement of the window.</param>
        /// <param name="clickContext">The click context path string (e.g. "../../0/0/2").</param>
        /// <returns>The root element from which to traverse for input controls.</returns>
        public static VisualElement ResolveRoot(VisualElement clickedElement, VisualElement windowRoot, string clickContext)
        {
            if (string.IsNullOrEmpty(clickContext))
            {
                return clickedElement;
            }

            // Determine starting element
            VisualElement current;
            if (clickContext[0] == '/')
            {
                // Start from window root
                current = windowRoot;
                clickContext = clickContext.Substring(1);
            }
            else
            {
                // Start from clicked element
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

        /// <summary>
        /// Collects all named input control values from the given root element's subtree.
        /// </summary>
        /// <param name="root">The root element to traverse.</param>
        /// <returns>Array of StringPairPacket with input names and their current values.</returns>
        public static StringPairPacket[] CollectInputValues(VisualElement root)
        {
            var result = new List<StringPairPacket>();
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

        private static bool IsInputElement(VisualElement element)
        {
            return element is TextField
                || element is DropdownField
                || element is Slider
                || element is Toggle
                || element is Selectable;
        }

        private static string GetControlValue(VisualElement element)
        {
            return element switch
            {
                TextField tf => tf.value,
                DropdownField dd => dd.value,
                Slider sl => sl.value.ToString(),
                Toggle tg => tg.value.ToString(),
                Selectable sel => sel.value.ToString(),
                _ => string.Empty,
            };
        }
    }
}
