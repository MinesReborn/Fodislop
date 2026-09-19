#nullable enable

using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public enum FrameBreakdownRowKind
{
    Header,
    Text,
    Warning,
}

public readonly record struct FrameBreakdownRow(
    FrameBreakdownRowKind Kind,
    string Text,
    float Share = -1f,
    Color Color = default,
    string? Pin = null);
