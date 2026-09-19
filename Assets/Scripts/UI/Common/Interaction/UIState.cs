#nullable enable

using UnityEngine.UIElements;

namespace Kern.UI;

public static class UIState
{
    public const string Hidden = "is-hidden";

    public static void SetHidden(VisualElement? element, bool hidden) =>
        element?.EnableInClassList(Hidden, hidden);

    public static void Show(VisualElement? element) => SetHidden(element, false);

    public static void Hide(VisualElement? element) => SetHidden(element, true);

    public static bool IsHidden(VisualElement? element) =>
        element == null || element.ClassListContains(Hidden);
}
