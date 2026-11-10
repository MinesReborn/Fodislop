#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using UnityEngine;
using Kern.Rendering.Diagnostics.Tools;
using Kern.Diagnostics.World;

namespace Kern.Rendering.Diagnostics.Windows;

internal static class LightingQualitySelectionControls
{

    internal static int DrawPowerOfTwo(string label, int value, string[] labels, int first)
    {
        GUILayout.Label(label, ToolTheme.WrappedLabel);
        int index = -1;
        for (int candidate = 0; candidate < labels.Length; candidate++)
        {
            if ((first << candidate) == value)
            {
                index = candidate;
                break;
            }
        }
        int selected = GUILayout.SelectionGrid(index, labels, 3, ToolTheme.SegmentedButton);
        return selected < 0 ? value : first << selected;
    }

    internal static int DrawDiscreteSamples(string label, int value, string[] labels, int[] values)
    {
        GUILayout.Label(label, ToolTheme.WrappedLabel);
        int selectedIndex = System.Array.IndexOf(values, value);
        int selected = GUILayout.SelectionGrid(selectedIndex, labels, 3, ToolTheme.SegmentedButton);
        return selected < 0 ? value : values[selected];
    }

}
