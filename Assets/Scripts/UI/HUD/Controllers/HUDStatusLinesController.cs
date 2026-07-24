using System;
using System.Collections.Generic;
using Fodinae.Scripts.UI.HUD.Player.Model;
using UnityEngine;
using UnityEngine.UIElements;


namespace Fodinae.Scripts.UI.HUD.Controllers
{
    /// <summary>
    /// SOLID Component Controller for Player Status Lines Panel.
    /// Encapsulates status line entry updates, countdown schedules, and panel layout lifecycle.
    /// </summary>
    public class HUDStatusLinesController
    {
        private VisualElement _statusPanel;
        private readonly Dictionary<string, VisualElement> _statusLineElements = new();
        private readonly Color _panelBorderColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        public void Initialize(VisualElement rootContainer)
        {
            if (rootContainer == null)
            {
                return;
            }

            _statusPanel = new VisualElement { name = "StatusPanel" };
            _statusPanel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
            _statusPanel.style.paddingTop = 6;
            _statusPanel.style.paddingBottom = 6;
            _statusPanel.style.paddingLeft = 10;
            _statusPanel.style.paddingRight = 10;
            _statusPanel.style.borderTopWidth = 1;
            _statusPanel.style.borderBottomWidth = 1;
            _statusPanel.style.borderLeftWidth = 1;
            _statusPanel.style.borderRightWidth = 1;
            _statusPanel.style.borderTopColor = _panelBorderColor;
            _statusPanel.style.borderBottomColor = _panelBorderColor;
            _statusPanel.style.borderLeftColor = _panelBorderColor;
            _statusPanel.style.borderRightColor = _panelBorderColor;
            _statusPanel.style.flexDirection = FlexDirection.Column;
            _statusPanel.style.display = DisplayStyle.None;

            rootContainer.Add(_statusPanel);

            if (PlayerStatsModel.Instance != null)
            {
                PlayerStatsModel.Instance.OnStatusLinesChanged += Rebuild;
            }

            Rebuild();
        }

        public void Dispose()
        {
            if (PlayerStatsModel.Instance != null)
            {
                PlayerStatsModel.Instance.OnStatusLinesChanged -= Rebuild;
            }
        }

        public void Rebuild()
        {
            if (_statusPanel == null)
            {
                return;
            }

            var stats = PlayerStatsModel.Instance;
            if (stats == null)
            {
                return;
            }

            var currentLines = stats.StatusLines;
            if (currentLines.Count == 0)
            {
                _statusPanel.style.display = DisplayStyle.None;
                _statusLineElements.Clear();
                _statusPanel.Clear();
                return;
            }

            _statusPanel.style.display = DisplayStyle.Flex;
            var toRemove = new List<string>();
            foreach (var kvp in _statusLineElements)
            {
                if (!currentLines.ContainsKey(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _statusPanel.Remove(_statusLineElements[key]);
                _statusLineElements.Remove(key);
            }

            foreach (var kvp in currentLines)
            {
                if (_statusLineElements.TryGetValue(kvp.Key, out var existing))
                {
                    if (existing is Label label)
                    {
                        UpdateLabelText(label, kvp.Value);
                        label.style.color = kvp.Value.Color;
                    }
                }
                else
                {
                    var row = new Label
                    {
                        style =
                        {
                            fontSize = 14,
                            color = kvp.Value.Color,
                            marginBottom = 2,
                            whiteSpace = WhiteSpace.Normal,
                        },
                    };
                    UpdateLabelText(row, kvp.Value);
                    _statusPanel.Add(row);

                    if (kvp.Value.Expiry > 0)
                    {
                        row.schedule.Execute(() =>
                        {
                            if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                            {
                                return;
                            }

                            var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                            if (entry.Text != null)
                            {
                                UpdateLabelText(row, entry);
                            }
                        }).Every(1000);
                    }

                    _statusLineElements[kvp.Key] = row;
                }
            }
        }

        private static void UpdateLabelText(Label label, StatusLineEntry entry)
        {
            if (entry.Text == null || entry.Text.Length == 0)
            {
                label.text = string.Empty;
                return;
            }

            var name = entry.Text[0];
            if (entry.Expiry > 0)
            {
                var remaining = Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                label.text = $"{name}: {FormatTime(remaining)}";
            }
            else if (entry.Text.Length > 1)
            {
                label.text = $"{name}: {entry.Text[1]}";
            }
            else
            {
                label.text = name;
            }
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
    }
}
