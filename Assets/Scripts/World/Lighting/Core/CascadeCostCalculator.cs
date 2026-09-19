#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Lighting;
public readonly record struct CascadeCostSample(
    int Index,
    int ProbeWidth,
    int ProbeHeight,
    int DirectionCount,
    float IntervalStart,
    float IntervalEnd,
    int StepCount,
    long RayCount,
    long RayStepCount,
    long MergeTapCount);

public static class CascadeCostCalculator
{
    public static long EstimateRayWorkUnits(IReadOnlyList<CascadeLayout> cascades)
    {
        if (cascades == null)
        {
            throw new ArgumentNullException(nameof(cascades));
        }

        var samples = new List<CascadeCostSample>(cascades.Count);
        CollectCascadeCosts(cascades, int.MaxValue, samples);

        long total = 0;
        foreach (CascadeCostSample sample in samples)
        {
            total = checked(total + sample.RayStepCount);
        }

        return total;
    }

    public static void CollectCascadeCosts(
        IReadOnlyList<CascadeLayout> cascades,
        int maximumSteps,
        List<CascadeCostSample> destination)
    {
        if (cascades == null)
        {
            throw new ArgumentNullException(nameof(cascades));
        }

        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.Clear();
        for (int index = 0; index < cascades.Count; index++)
        {
            CascadeLayout cascade = cascades[index];

            // Conservative DDA texel visits, including each connected merge
            // path. Early opacity termination and field clipping reduce this.
            // maximumSteps is retained for caller compatibility; it no longer
            // permits the transport solver to skip geometry.
            float traceLength = Mathf.Max(cascade.IntervalEnd - cascade.IntervalStart, 0f);
            int traceCount = 1;
            long mergeTaps = 0;
            if (index + 1 < cascades.Count)
            {
                CascadeLayout far = cascades[index + 1];
                int branchCount = Mathf.Clamp(
                    far.DirectionCount / Mathf.Max(1, cascade.DirectionCount),
                    1,
                    4);
                traceCount = branchCount * 4;
                mergeTaps = (long)cascade.EntryCount * traceCount;
                traceLength = Mathf.Abs(cascade.IntervalStart) + Mathf.Abs(far.IntervalStart) +
                    Mathf.Sqrt(2f) * far.ProbeSpacing;
            }

            int stepsPerTrace = Mathf.CeilToInt(Mathf.Sqrt(2f) * traceLength) + 2;
            int stepCount = stepsPerTrace * traceCount;

            destination.Add(new CascadeCostSample(
                index,
                cascade.ProbeWidth,
                cascade.ProbeHeight,
                cascade.DirectionCount,
                cascade.IntervalStart,
                cascade.IntervalEnd,
                stepCount,
                cascade.EntryCount,
                (long)cascade.EntryCount * stepCount,
                mergeTaps));
        }
    }
}
