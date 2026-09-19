#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kern.Tools.Imgui.Profiling;

// Поиск по всем маркерам профайлера и закреплённые маркеры.
//
// Записываются только найденные (не больше MaxResults) и закреплённые:
// держать рекордер на каждый из тысяч маркеров ради поиска нельзя.
// Закрепления переживают перезапуск игры.
public sealed class MarkerSearch : IDisposable
{
    public const int MaxResults = 40;
    private const int MinimumQueryLength = 2;
    // Закрепления — файл рядом с раскладкой окон инструментов, а не PlayerPrefs:
    // настройки проекта живут в файлах (KERN-FORBIDDEN-API).
    private const string PinsFileName = "tool_marker_pins.txt";

    private readonly List<FrameProbe> _results = [];
    private readonly List<FrameProbe> _pins = [];
    private readonly Queue<string> _pendingToggles = new();

    private string _appliedQuery = string.Empty;
    private int _appliedVersion = -1;

    public MarkerSearch()
    {
        string stored = File.Exists(PinsPath) ? File.ReadAllText(PinsPath) : string.Empty;
        foreach (string name in stored.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            _pins.Add(new FrameProbe(name, name));
        }
    }

    private static string PinsPath => Path.Combine(Application.persistentDataPath, PinsFileName);

    public string Query { get; set; } = string.Empty;

    public int MatchCount { get; private set; }

    public IReadOnlyList<FrameProbe> Results => _results;

    public IReadOnlyList<FrameProbe> Pins => _pins;

    public bool QueryTooShort => Query.Trim().Length < MinimumQueryLength;

    public bool IsPinned(string name)
    {
        foreach (FrameProbe pin in _pins)
        {
            if (pin.MarkerName == name)
            {
                return true;
            }
        }

        return false;
    }

    // Из OnGUI списки менять нельзя: Layout и Repaint обязаны увидеть одно
    // и то же число строк. Переключение применяется на следующем Tick.
    public void RequestTogglePin(string name)
    {
        _pendingToggles.Enqueue(name);
    }

    public void Tick(bool searching)
    {
        ApplyToggles();
        if (!searching)
        {
            ClearResults();
            _appliedQuery = string.Empty;
            return;
        }

        string query = Query.Trim();
        MarkerDirectory.Refresh();
        if (query == _appliedQuery && MarkerDirectory.Version == _appliedVersion)
        {
            return;
        }

        _appliedQuery = query;
        _appliedVersion = MarkerDirectory.Version;
        ClearResults();
        if (query.Length < MinimumQueryLength)
        {
            return;
        }

        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (info.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            MatchCount++;
            if (_results.Count < MaxResults)
            {
                var probe = new FrameProbe(info);
                probe.Start();
                _results.Add(probe);
            }
        }
    }

    public void Sample()
    {
        foreach (FrameProbe probe in _results)
        {
            probe.Sample();
        }

        _results.Sort(static (left, right) => Weight(right).CompareTo(Weight(left)));

        foreach (FrameProbe pin in _pins)
        {
            pin.Sample();
        }
    }

    // Время выше счётчиков: в поиске чаще ищут, что тормозит.
    private static double Weight(FrameProbe probe) =>
        !probe.Available ? double.MinValue : probe.IsTime ? 1e12 + probe.Average : probe.Average;

    public void Stop()
    {
        ClearResults();
        _appliedQuery = string.Empty;
        foreach (FrameProbe pin in _pins)
        {
            pin.Stop();
        }
    }

    private void ApplyToggles()
    {
        bool changed = false;
        while (_pendingToggles.Count > 0)
        {
            string name = _pendingToggles.Dequeue();
            int index = _pins.FindIndex(pin => pin.MarkerName == name);
            if (index >= 0)
            {
                _pins[index].Dispose();
                _pins.RemoveAt(index);
            }
            else
            {
                _pins.Add(new FrameProbe(name, name));
            }

            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var names = new string[_pins.Count];
        for (int i = 0; i < _pins.Count; i++)
        {
            names[i] = _pins[i].MarkerName;
        }

        File.WriteAllText(PinsPath, string.Join('\n', names));
    }

    private void ClearResults()
    {
        foreach (FrameProbe probe in _results)
        {
            probe.Dispose();
        }

        _results.Clear();
        MatchCount = 0;
    }

    public void Dispose()
    {
        ClearResults();
        foreach (FrameProbe pin in _pins)
        {
            pin.Dispose();
        }
    }
}
