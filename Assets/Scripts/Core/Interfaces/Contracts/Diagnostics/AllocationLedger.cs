#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Kern.Core.Interfaces.Diagnostics;

// Сколько управляемой памяти аллоцирует каждый игровой путь за кадр.
//
// Счётчик «мусор за кадр» — один на весь процесс, а в редакторе ещё и вместе
// с самим редактором. Здесь разница занятой кучи до и после участка
// приписывается участку. GC.GetAllocatedBytesForCurrentThread под Boehm всегда
// 0, поэтому берётся Profiler.GetMonoUsedSizeLong: куча растёт на каждую
// аллокацию до сборки, а замер со сборкой внутри (отрицательная разница)
// отбрасывается и считается отдельно.
//
// Участки вложенные: родитель включает детей. Пока окно разбора кадра закрыто,
// Enabled = false и Measure ничего не делает.
public static class AllocationLedger
{
    public sealed class Entry
    {
        internal Entry(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public long LastFrameBytes { get; internal set; }

        public int LastFrameCalls { get; internal set; }

        public double AverageBytes { get; internal set; }

        public long PeakBytes { get; internal set; }

        public long TotalBytes { get; internal set; }

        public int CollectionsInside { get; internal set; }

        internal long FrameBytes;
        internal int FrameCalls;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly Entry? _entry;
        private readonly long _before;

        internal Scope(Entry entry)
        {
            _entry = entry;
            _before = Profiler.GetMonoUsedSizeLong();
        }

        public void Dispose()
        {
            if (_entry == null)
            {
                return;
            }

            long delta = Profiler.GetMonoUsedSizeLong() - _before;
            Roll();
            _entry.FrameCalls++;
            if (delta < 0)
            {
                _entry.CollectionsInside++;
                return;
            }

            _entry.FrameBytes += delta;
        }
    }

    private const double AverageWeight = 0.05;

    private static readonly List<Entry> _Entries = [];
    private static readonly Dictionary<string, Entry> _ByName = new(StringComparer.Ordinal);
    private static int _frame = -1;

    public static bool Enabled { get; set; }

    public static IReadOnlyList<Entry> Entries => _Entries;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        // Записи не удаляются: на них держат ссылки статические поля игровых
        // классов, а без перезагрузки домена эти поля переживают Play Mode.
        Enabled = false;
        Reset();
        _frame = -1;
    }

    public static Entry Register(string name)
    {
        if (!_ByName.TryGetValue(name, out Entry? entry))
        {
            entry = new Entry(name);
            _ByName[name] = entry;
            _Entries.Add(entry);
        }

        return entry;
    }

    public static Scope Measure(Entry entry) => Enabled ? new Scope(entry) : default;

    public static void Reset()
    {
        foreach (Entry entry in _Entries)
        {
            entry.LastFrameBytes = 0;
            entry.LastFrameCalls = 0;
            entry.AverageBytes = 0d;
            entry.PeakBytes = 0;
            entry.TotalBytes = 0;
            entry.CollectionsInside = 0;
            entry.FrameBytes = 0;
            entry.FrameCalls = 0;
        }
    }

    // Закрывает прошлый кадр при первом замере нового.
    private static void Roll()
    {
        int frame = Time.frameCount;
        if (frame == _frame)
        {
            return;
        }

        bool closePrevious = _frame >= 0;
        _frame = frame;
        foreach (Entry entry in _Entries)
        {
            if (closePrevious)
            {
                entry.LastFrameBytes = entry.FrameBytes;
                entry.LastFrameCalls = entry.FrameCalls;
                entry.TotalBytes += entry.FrameBytes;
                entry.PeakBytes = Math.Max(entry.PeakBytes, entry.FrameBytes);
                entry.AverageBytes += (entry.FrameBytes - entry.AverageBytes) * AverageWeight;
            }

            entry.FrameBytes = 0;
            entry.FrameCalls = 0;
        }
    }
}
