#nullable enable

using System;
using System.Collections.Generic;
using Kern.Tools.Imgui.Profiling;

namespace Kern.Tools.Imgui.Windows;

internal sealed class FrameProbeDiscoverer
{
    private const int MaxDiscoveredMarkers = 80;
    private const string OwnMarkerPrefix = "Kern.";

    private readonly List<FrameProbe> _discovered = [];
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private int _discoveredVersion = -1;

    public List<FrameProbe> Discovered => _discovered;

    public void Discover(ISet<string> catalogNames)
    {
        if (_discoveredVersion == MarkerDirectory.Version)
        {
            return;
        }

        _discoveredVersion = MarkerDirectory.Version;
        _known.Clear();
        foreach (FrameProbe probe in _discovered)
        {
            _known.Add(probe.MarkerName);
        }

        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (_discovered.Count >= MaxDiscoveredMarkers)
            {
                break;
            }

            if (!info.Name.StartsWith(OwnMarkerPrefix, StringComparison.Ordinal) ||
                info.Name.StartsWith("Kern.Tools.", StringComparison.Ordinal) ||
                // Маркеры Test Runner — имена тестов, а не участки кадра.
                info.Name.StartsWith("Kern.Tests.", StringComparison.Ordinal) ||
                catalogNames.Contains(info.Name) ||
                _known.Contains(info.Name) ||
                info.Unit != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                continue;
            }

            var probe = new FrameProbe(info, "· " + info.Name.Substring(OwnMarkerPrefix.Length));
            probe.Start();
            _discovered.Add(probe);
        }
    }

    public void Sample()
    {
        foreach (FrameProbe probe in _discovered)
        {
            probe.Sample();
        }

        _discovered.Sort(static (left, right) => right.Average.CompareTo(left.Average));
    }

    public void Reset()
    {
        foreach (FrameProbe probe in _discovered)
        {
            probe.Dispose();
        }

        _discovered.Clear();
        _known.Clear();
        _discoveredVersion = -1;
    }
}
