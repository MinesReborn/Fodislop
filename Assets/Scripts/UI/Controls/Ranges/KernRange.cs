#nullable enable

using UnityEngine.UIElements;

namespace Kern.UI.Controls;

// Ползунок макета (micro.css :is(.pk-slider, .range input)): бегунок буквой «Т» — планка 3 px и ножка 2 px,
// под дорожкой риски по четвертям (0, 25, 50, 75, 100 %). Стандартный Slider этих частей не имеет —
// они добавляются в его дорожку и бегунок; вид — Kern.uss (.range__tick, .range__thumb-*).
public static class KernRange
{
    public static void Decorate(Slider slider)
    {
        VisualElement? dragContainer = slider.Q(className: "unity-base-slider__drag-container");
        VisualElement? dragger = slider.Q(className: "unity-base-slider__dragger");
        if (dragContainer == null || dragger == null || dragger.Q(className: "range__thumb-bar") != null)
        {
            return;
        }

        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("range__thumb-bar");
        var stem = new VisualElement { pickingMode = PickingMode.Ignore };
        stem.AddToClassList("range__thumb-stem");
        dragger.Add(bar);
        dragger.Add(stem);

        var ticks = new VisualElement { pickingMode = PickingMode.Ignore };
        ticks.AddToClassList("range__ticks");
        for (int i = 0; i <= 4; i++)
        {
            var tick = new VisualElement { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("range__tick");
            // Последняя риска — у правого края изнутри, а не за ним
            if (i == 4)
            {
                tick.style.right = 0;
            }
            else
            {
                tick.style.left = Length.Percent(i * 25f);
            }

            ticks.Add(tick);
        }

        dragContainer.Insert(0, ticks);
    }
}
