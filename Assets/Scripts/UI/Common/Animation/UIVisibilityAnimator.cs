#nullable enable

using UnityEngine.UIElements;

namespace Kern.UI;
public static class UIVisibilityAnimator
{
    public const string HiddenState = "sci-fi-window-anim--hidden";

    public const string ShownState = "sci-fi-window-anim--shown";

    private const long FallbackMilliseconds = 1000;

    public static void Show(VisualElement? element)
    {
        if (element == null)
        {
            return;
        }

        element.AddToClassList(HiddenState);
        element.RemoveFromClassList(ShownState);
        UIState.Show(element);

        element.schedule.Execute(() =>
        {
            element.RemoveFromClassList(HiddenState);
            element.AddToClassList(ShownState);
        });
    }

    public static void Hide(VisualElement? element)
    {
        if (element == null)
        {
            return;
        }

        if (UIState.IsHidden(element))
        {
            return;
        }

        element.RemoveFromClassList(ShownState);
        element.AddToClassList(HiddenState);

        bool finished = false;
        void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            UIState.Hide(element);
        }

        element.RegisterCallbackOnce<TransitionEndEvent>(_ => Finish());
        element.schedule.Execute(Finish).StartingIn(FallbackMilliseconds);
    }

    public static void SetHidden(VisualElement? element, bool hidden)
    {
        if (hidden)
        {
            Hide(element);
        }
        else
        {
            Show(element);
        }
    }
}
