#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Models;
using Fodinae.UI.HUD.Player.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Manages live status lines, temporary buffs and expiration timers in the HUD.
/// </summary>
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

    public void Rebuild(PlayerStatsModel? stats)
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
                if (existing is Label label)
                {
                    ApplyStatusLabel(label, kvp.Key, kvp.Value);
                    label.style.color = kvp.Value.Color;
                }
            }
            else
            {
                var row = new Label();
                row.AddToClassList("hud-status-line");
                row.style.color = kvp.Value.Color;
                ApplyStatusLabel(row, kvp.Key, kvp.Value);
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

                        if (row is Label scheduledLabel)
                        {
                            ApplyStatusLabel(scheduledLabel, kvp.Key, entry);
                        }
                    }).Every(1000);
                    _statusSchedules[kvp.Key] = schedule;
                }

                _statusLineElements[kvp.Key] = row;
            }
        }
    }

    private void ApplyStatusLabel(Label label, string key, StatusLineEntry entry)
    {
        string next = ComposeStatusText(entry);
        if (_statusLineTexts.TryGetValue(key, out var cached) && cached == next)
        {
            return;
        }

        _statusLineTexts[key] = next;
        label.text = next;
    }

    private static string ComposeStatusText(StatusLineEntry entry)
    {
        if (entry.Text == null || entry.Text.Length == 0)
        {
            return string.Empty;
        }

        var name = entry.Text[0];
        if (entry.Expiry > 0)
        {
            var remaining = Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return $"{name}: {FormatTime(remaining)}";
        }

        if (entry.Text.Length > 1)
        {
            return $"{name}: {entry.Text[1]}";
        }

        return name;
    }

    private static string FormatTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
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
