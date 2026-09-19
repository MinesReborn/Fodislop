#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace Kern.Tools.Imgui.Profiling;

// Полный разбор игрового цикла без перечня имён руками.
//
// Список систем берётся из текущего PlayerLoop, маркер каждой системы
// называется «Фаза.Система». Системы одного уровня не вложены друг в друга,
// но наличие типа не гарантирует покрытие соответствующим маркером.
// Сумма найденных маркеров не позволяет приписать остаток редактору.
public sealed class PlayerLoopBreakdown : IDisposable
{
    private readonly List<FrameProbe> _systems = [];
    private readonly List<FrameProbe> _sorted = [];

    public IReadOnlyList<FrameProbe> Sorted => _sorted;

    public int SystemCount => _systems.Count;

    public int MissingCount { get; private set; }

    public double AccountedMilliseconds { get; private set; }

    public void Begin()
    {
        if (_systems.Count == 0)
        {
            Collect();
        }

        foreach (FrameProbe probe in _systems)
        {
            probe.Start();
        }
    }

    private void Collect()
    {
        PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
        if (root.subSystemList == null)
        {
            return;
        }

        foreach (PlayerLoopSystem phase in root.subSystemList)
        {
            if (phase.type == null || phase.subSystemList == null)
            {
                continue;
            }

            foreach (PlayerLoopSystem system in phase.subSystemList)
            {
                if (system.type == null)
                {
                    continue;
                }

                string name = $"{phase.type.Name}.{system.type.Name}";
                _systems.Add(new FrameProbe(name, name, isDetail: true));
            }
        }
    }

    public void Sample()
    {
        double accounted = 0d;
        int missing = 0;
        _sorted.Clear();
        foreach (FrameProbe probe in _systems)
        {
            probe.Sample();
            if (!probe.Available)
            {
                missing++;
                continue;
            }

            accounted += probe.Average;
            _sorted.Add(probe);
        }

        _sorted.Sort(static (left, right) => right.Average.CompareTo(left.Average));
        AccountedMilliseconds = accounted;
        MissingCount = missing;
    }

    public void Stop()
    {
        foreach (FrameProbe probe in _systems)
        {
            probe.Dispose();
        }

        _systems.Clear();
        _sorted.Clear();
        AccountedMilliseconds = 0d;
        MissingCount = 0;
    }

    public void Dispose()
    {
        Stop();
    }
}
