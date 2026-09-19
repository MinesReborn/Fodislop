#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Kern.Tools.Imgui.Profiling;

public readonly record struct MarkerInfo(
    ProfilerRecorderHandle Handle,
    string Name,
    ProfilerCategory Category,
    ProfilerMarkerDataUnit Unit,
    MarkerFlags Flags);

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

    private static readonly List<ProfilerRecorderHandle> _Handles = new(4096);
    private static readonly Dictionary<string, MarkerInfo> _ByName = new(4096, StringComparer.Ordinal);
    private static readonly List<MarkerInfo> _All = new(4096);

    private static float _nextRefresh = float.NegativeInfinity;
    private static int _knownHandleCount = -1;

    // Растёт при каждом изменении набора: пробы, не нашедшие имя, повторяют
    // поиск только после него, а не на каждом замере.
    public static int Version { get; private set; }

    public static IReadOnlyList<MarkerInfo> All
    {
        get
        {
            Refresh();
            return _All;
        }
    }

    public static void Refresh(bool force = false)
    {
        float now = Time.realtimeSinceStartup;
        if (!force && now < _nextRefresh)
        {
            return;
        }

        _nextRefresh = now + RefreshIntervalSeconds;
        _Handles.Clear();
        ProfilerRecorderHandle.GetAvailable(_Handles);
        if (_Handles.Count == _knownHandleCount)
        {
            return;
        }

        _knownHandleCount = _Handles.Count;
        _ByName.Clear();
        _All.Clear();
        foreach (ProfilerRecorderHandle handle in _Handles)
        {
            ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            string name = description.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var info = new MarkerInfo(handle, name, description.Category, description.UnitType, description.Flags);
            if (_ByName.TryAdd(name, info))
            {
                _All.Add(info);
            }
        }

        _All.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        Version++;
    }

    public static bool TryGet(string name, out MarkerInfo info)
    {
        Refresh();
        return _ByName.TryGetValue(name, out info);
    }
}
