#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Kern.Tools.ImGui.Profiling;

public readonly record struct MarkerInfo(
    ProfilerRecorderHandle Handle,
    string Name,
    ProfilerCategory Category,
    ProfilerMarkerDataUnit Unit,
    MarkerFlags Flags)
{
    // Годится для перебора всех маркеров: это время и у маркера нет замера
    // GPU. Рекордер на маркере с SampleGPU ставит метки времени в командный
    // буфер вокруг прохода — перебор, который меняет то, что меряет: под ним
    // террейн становился чёрным.
    public bool IsSweepable =>
        Unit == ProfilerMarkerDataUnit.TimeNanoseconds && (Flags & MarkerFlags.SampleGPU) == 0;
}

// Справочник всех маркеров и счётчиков, которые профайлер знает прямо сейчас.
//
// Раньше замер искал участок перебором категорий по угаданному имени, и в
// выводе «участок не найден» смешивались две разные вещи: имя неверное и
// категория не та. Здесь имя сверяется со списком самого профайлера — не
// найдено значит, что такого маркера в этой сборке и в этом кадре нет.
//
// Маркеры регистрируются лениво, при первом срабатывании, поэтому список
// перечитывается раз в пару секунд; при неизменном числе — без разбора имён.
public static class MarkerDirectory
{
    private const float RefreshIntervalSeconds = 2f;

    private static readonly List<ProfilerRecorderHandle> s_handles = new(4096);
    private static readonly Dictionary<string, MarkerInfo> s_byName = new(4096, StringComparer.Ordinal);
    private static readonly List<MarkerInfo> s_all = new(4096);

    private static float s_nextRefresh = float.NegativeInfinity;
    private static int s_knownHandleCount = -1;

    // Растёт при каждом изменении набора: пробы, не нашедшие имя, повторяют
    // поиск только после него, а не на каждом замере.
    public static int Version { get; private set; }

    public static IReadOnlyList<MarkerInfo> All
    {
        get
        {
            Refresh();
            return s_all;
        }
    }

    public static void Refresh(bool force = false)
    {
        float now = Time.realtimeSinceStartup;
        if (!force && now < s_nextRefresh)
        {
            return;
        }

        s_nextRefresh = now + RefreshIntervalSeconds;
        s_handles.Clear();
        ProfilerRecorderHandle.GetAvailable(s_handles);
        if (s_handles.Count == s_knownHandleCount)
        {
            return;
        }

        s_knownHandleCount = s_handles.Count;
        s_byName.Clear();
        s_all.Clear();
        foreach (ProfilerRecorderHandle handle in s_handles)
        {
            ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            string name = description.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var info = new MarkerInfo(handle, name, description.Category, description.UnitType, description.Flags);
            if (s_byName.TryAdd(name, info))
            {
                s_all.Add(info);
            }
        }

        s_all.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        Version++;
    }

    public static bool TryGet(string name, out MarkerInfo info)
    {
        Refresh();
        return s_byName.TryGetValue(name, out info);
    }
}
