#nullable enable

using UnityEngine.UIElements;

namespace Kern.UI.Motion;

// Тревога (js/motion.js kernFlash, motion.css kern-flash): две вспышки и ровный цвет. Тусклая фаза — класс is-flash-dim
// (прозрачность .25 в листе), по FlashMs на фазу. Повтор — только при новом событии.
public static class KernFlash
{
    public const string DimClass = "is-flash-dim";

    public static void Play(VisualElement element)
    {
        long t = KernMotionTiming.FlashMs;
        element.AddToClassList(DimClass);
        element.schedule.Execute(() => element.RemoveFromClassList(DimClass)).StartingIn(t);
        element.schedule.Execute(() => element.AddToClassList(DimClass)).StartingIn(2 * t);
        element.schedule.Execute(() => element.RemoveFromClassList(DimClass)).StartingIn(3 * t);
    }
}
