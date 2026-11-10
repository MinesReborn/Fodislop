#nullable enable

using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Motion;

// Число досчитывается до нового значения за CountMs ступенями по кадру (js/motion.js kernCount);
// format — как показывать (группы разрядов, сокращение K…). Первое значение ставится сразу.
public static class KernCount
{
    private sealed class State
    {
        public long Value;
        public IVisualElementScheduledItem? Tick;
    }

    private static readonly ConditionalWeakTable<Label, State> s_states = new();

    public static void Set(Label label, long to, Func<long, string> format)
    {
        if (!s_states.TryGetValue(label, out State? state))
        {
            s_states.Add(label, new State { Value = to });
            label.text = format(to);
            return;
        }

        state.Tick?.Pause();
        long from = state.Value;
        state.Value = to;
        if (from == to)
        {
            label.text = format(to);
            return;
        }

        double t0 = Time.realtimeSinceStartupAsDouble * 1000d;
        state.Tick = label.schedule.Execute(() =>
        {
            double k = Math.Min(1d, (Time.realtimeSinceStartupAsDouble * 1000d - t0) / KernMotionTiming.CountMs);
            label.text = format((long)Math.Round(from + (to - from) * k));
            if (k >= 1d)
            {
                state.Tick?.Pause();
            }
        }).Every(16);
        label.text = format(from);
    }

    // Сбросить досчёт: следующее значение встанет сразу (скелет до первых данных)
    public static void Reset(Label label) => s_states.Remove(label);
}
