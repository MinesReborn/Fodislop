#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Screens;

namespace Kern.UI.HUD;

public sealed class PlayerHUDStatusPanel
{
    private readonly Dictionary<string, VisualElement> _statusLineElements = new();
    private readonly Dictionary<string, string> _statusLineTexts = new();
    private readonly Dictionary<string, IVisualElementScheduledItem> _statusSchedules = new();
    private readonly List<string> _toRemove = new();
    private VisualElement? _statusPanel;

    public void Initialize(VisualElement root)
    {
        _statusPanel = root.Q<VisualElement>("StatusPanel");
    }

    public void Rebuild(IPlayerStats? stats)
    {
        if (_statusPanel == null || stats == null)
        {
            return;
        }

        var currentLines = stats.StatusLines;
        if (currentLines.Count == 0)
        {
            UIState.Hide(_statusPanel);
            ClearSchedules();
            _statusLineElements.Clear();
            _statusLineTexts.Clear();
            _statusPanel.Clear();
            return;
        }

        UIState.Show(_statusPanel);
        _toRemove.Clear();
        foreach (var kvp in _statusLineElements)
        {
            if (!currentLines.ContainsKey(kvp.Key))
            {
                _toRemove.Add(kvp.Key);
            }
        }

        for (int i = 0; i < _toRemove.Count; i++)
        {
            string key = _toRemove[i];
            _statusPanel.Remove(_statusLineElements[key]);
            if (_statusSchedules.TryGetValue(key, out var schedule))
            {
                schedule.Pause();
                _statusSchedules.Remove(key);
            }

            _statusLineElements.Remove(key);
            _statusLineTexts.Remove(key);
        }

        foreach (var kvp in currentLines)
        {
            if (_statusLineElements.TryGetValue(kvp.Key, out var existing))
            {
                ApplyStatusRow(existing, kvp.Key, kvp.Value);
            }
            else
            {
                // Строка состояния (макет: .states li): название цветом сервера, справа — остаток времени
                var row = new VisualElement { pickingMode = PickingMode.Position };
                row.AddToClassList("state");
                var name = new Label { pickingMode = PickingMode.Ignore };
                name.AddToClassList("state__name");
                var time = new Label { pickingMode = PickingMode.Ignore };
                time.AddToClassList("state__time");
                row.Add(name);
                row.Add(time);
                ApplyStatusRow(row, kvp.Key, kvp.Value);
                _statusPanel.Add(row);

                if (kvp.Value.Expiry > 0)
                {
                    var schedule = row.schedule.Execute(() =>
                    {
                        if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                        {
                            return;
                        }

                        var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                        if (entry.Text == null)
                        {
                            return;
                        }

                        ApplyStatusRow(row, kvp.Key, entry);
                    }).Every(1000);
                    _statusSchedules[kvp.Key] = schedule;
                }

                _statusLineElements[kvp.Key] = row;
            }
        }
    }

    private void ApplyStatusRow(VisualElement row, string key, StatusLineEntry entry)
    {
        var name = (Label)row[0];
        var time = (Label)row[1];
        name.style.color = entry.Color;
        string title = entry.Text is { Length: > 0 } ? entry.Text[0] : string.Empty;
        string right = entry.Expiry > 0
            ? FormatTime(Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            : entry.Text is { Length: > 1 } ? entry.Text[1] : string.Empty;
        string next = title + "\n" + right;
        if (_statusLineTexts.TryGetValue(key, out var cached) && cached == next)
        {
            return;
        }

        _statusLineTexts[key] = next;
        name.text = title;
        time.text = right;
    }

    // Остаток — «4:12», от часа — «1:04:12»
    private static string FormatTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        return $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public void ClearSchedules()
    {
        foreach (var schedule in _statusSchedules.Values)
        {
            schedule.Pause();
        }

        _statusSchedules.Clear();
    }
}
