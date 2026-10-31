#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using Kern.UI.Input;

namespace Kern.UI.Chat;

internal static class ChatResizeCursor
{

    internal enum BoxEdge
    {
        North,
        South,
        West,
        East,
        Northwest,
        Northeast,
        Southwest,
        Southeast,
    }

    internal static ResizeCursors.Shape CursorShape(ChatResizeCursor.BoxEdge edge) => edge switch
    {
        ChatResizeCursor.BoxEdge.North or ChatResizeCursor.BoxEdge.South => ResizeCursors.Shape.NorthSouth,
        ChatResizeCursor.BoxEdge.West or ChatResizeCursor.BoxEdge.East => ResizeCursors.Shape.WestEast,
        ChatResizeCursor.BoxEdge.Northwest or ChatResizeCursor.BoxEdge.Southeast => ResizeCursors.Shape.NorthwestSoutheast,
        ChatResizeCursor.BoxEdge.Northeast or ChatResizeCursor.BoxEdge.Southwest => ResizeCursors.Shape.NortheastSouthwest,
        _ => ResizeCursors.Shape.MoveAll,
    };

}
