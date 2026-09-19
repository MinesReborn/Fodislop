#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.Core.Interfaces.Diagnostics;

// Сколько раз и по какой причине дорогой путь пересобирался.
//
// Время участка («сборка меша 125 мс») говорит, что пересборка дорогая, но не
// говорит, почему она случилась и как часто. Здесь каждая причина — отдельный
// счётчик: всего за сеанс, за последнюю секунду и когда была последней.
// Считать дёшево (инкремент и чтение времени), поэтому работает всегда.
public static class RebuildLedger
{
    public sealed class Entry
    {
        internal Entry(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public long Total { get; internal set; }

        public int LastSecond { get; internal set; }

        public float LastTime { get; internal set; } = -1f;

        internal int CurrentSecondCount;
        internal int CurrentSecond = -1;
    }

    private static readonly List<Entry> _Entries = [];
    private static readonly Dictionary<string, Entry> _ByName = new(StringComparer.Ordinal);

    public static IReadOnlyList<Entry> Entries => _Entries;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        // Записи не удаляются: на них держат ссылки статические поля.
        foreach (Entry entry in _Entries)
        {
            entry.Total = 0;
            entry.LastSecond = 0;
            entry.LastTime = -1f;
            entry.CurrentSecondCount = 0;
            entry.CurrentSecond = -1;
        }
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

    public static void Count(Entry entry)
    {
        float now = Time.unscaledTime;
        Roll(entry, now);
        entry.Total++;
        entry.CurrentSecondCount++;
        entry.LastTime = now;
    }

    // Счётчик «за последнюю секунду» закрывается при чтении тоже, иначе
    // замолчавшая причина вечно показывала бы свою последнюю секунду.
    public static int RateOf(Entry entry)
    {
        Roll(entry, Time.unscaledTime);
        return entry.LastSecond;
    }

    private static void Roll(Entry entry, float now)
    {
        int second = Mathf.FloorToInt(now);
        if (second == entry.CurrentSecond)
        {
            return;
        }

        entry.LastSecond = second == entry.CurrentSecond + 1 ? entry.CurrentSecondCount : 0;
        entry.CurrentSecond = second;
        entry.CurrentSecondCount = 0;
    }
}
