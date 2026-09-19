#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.World.Lighting.Diagnostics;

public struct InvalidationFrameRecord
{
    public ulong FrameIndex;
    public LightingInvalidationFlags Triggers;
    public string Reason;
    public string[] ExecutedPasses;
    public string[] SkippedPasses;
}

/// <summary>
/// Ring buffer storing the last 64 frames of lighting invalidation events and pass execution reasons.
/// Allows instant post-mortem analysis of why any pass executed or skipped.
/// </summary>
public sealed class LightingInvalidationJournal
{
    private const int Capacity = 64;
    private readonly InvalidationFrameRecord[] _records = new InvalidationFrameRecord[Capacity];
    private int _head;
    private int _count;

    public int Count => _count;

    public void Record(
        ulong frameIndex,
        LightingInvalidationFlags triggers,
        string reason,
        string[] executedPasses,
        string[] skippedPasses)
    {
        _records[_head] = new InvalidationFrameRecord
        {
            FrameIndex = frameIndex,
            Triggers = triggers,
            Reason = reason,
            ExecutedPasses = executedPasses,
            SkippedPasses = skippedPasses,
        };

        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    public List<InvalidationFrameRecord> GetRecent(int maxCount)
    {
        var result = new List<InvalidationFrameRecord>(Math.Min(_count, maxCount));
        int toFetch = Math.Min(_count, maxCount);

        for (int i = 0; i < toFetch; i++)
        {
            int index = (_head - 1 - i + Capacity * 2) % Capacity;
            result.Add(_records[index]);
        }

        return result;
    }
}
