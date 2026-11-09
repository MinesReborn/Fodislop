#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Motion;

// Шкала из ячеек (js/motion.js kernQuant): ячейки не пересоздаются, меняется только состояние. Загораются по одной
// от края к новому значению, гаснут — от нового значения обратно; каждая следующая — на шаг StepMs позже.
// Задержка — transition-delay на самой ячейке; переход цвета задаёт лист (.cells > .cell).
public static class KernQuant
{
    public const string CellClass = "cell";
    public const string OnClass = "on";
    public const string FirstClass = "cell--first";

    private sealed class Run
    {
        public bool Up;
        public double T0;
        public int From;
        public double End;
    }

    private static readonly ConditionalWeakTable<VisualElement, Run> s_runs = new();

    // paint — свой цвет ячейки (точки прочности красятся долей прочности); вызывается после смены класса
    public static void Set(VisualElement box, int total, int lit, Action<VisualElement, bool>? paint = null)
    {
        total = Math.Max(0, total);
        lit = Math.Clamp(lit, 0, total);
        while (box.childCount < total)
        {
            var cell = new VisualElement { pickingMode = PickingMode.Ignore };
            cell.AddToClassList(CellClass);
            // Первая — без зазора слева: у USS нет :first-child
            cell.EnableInClassList(FirstClass, box.childCount == 0);
            box.Add(cell);
        }

        while (box.childCount > total)
        {
            box.RemoveAt(box.childCount - 1);
        }

        int was = 0;
        for (int i = 0; i < total; i++)
        {
            if (box[i].ClassListContains(OnClass))
            {
                was++;
            }
        }

        double now = Time.realtimeSinceStartupAsDouble * 1000d;
        bool up = lit > was;
        // Один фронт: если прошлое изменение в ту же сторону ещё идёт, новые ячейки встают за ним
        bool going = s_runs.TryGetValue(box, out Run? run) && run.Up == up && now < run.End;
        double t0 = going ? run!.T0 : now;
        int from = going ? run!.From : was;

        for (int i = 0; i < total; i++)
        {
            VisualElement cell = box[i];
            bool changes = up ? i >= was && i < lit : i >= lit && i < was;
            if (changes)
            {
                int order = up ? i - from : from - 1 - i;
                double delay = Math.Max(0d, order * KernMotionTiming.StepMs - (now - t0));
                cell.style.transitionDelay = new List<TimeValue> { new((float)delay, TimeUnit.Millisecond) };
            }

            bool on = i < lit;
            cell.EnableInClassList(OnClass, on);
            paint?.Invoke(cell, on);
        }

        if (run == null)
        {
            run = new Run();
            s_runs.Add(box, run);
        }

        run.Up = up;
        run.T0 = t0;
        run.From = from;
        run.End = t0 + Math.Abs(lit - from) * KernMotionTiming.StepMs;
    }
}
