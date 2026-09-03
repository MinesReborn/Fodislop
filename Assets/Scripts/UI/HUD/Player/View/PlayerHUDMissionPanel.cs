#nullable enable

using Fodinae.Core.Localization;
using Fodinae.UI.HUD.Player.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Controls the mission objective panel in Player HUD.
/// </summary>
public sealed class PlayerHUDMissionPanel
{
    private readonly ILocalizationService _loc;
    private VisualElement? _missionPanel;
    private Label? _missionTitleLabel;
    private Label? _missionDescLabel;
    private VisualElement? _missionProgressFill;
    private Label? _missionProgressLabel;

    public PlayerHUDMissionPanel(ILocalizationService loc)
    {
        _loc = loc;
    }

    public void Initialize(VisualElement root)
    {
        _missionPanel = root.Q<VisualElement>("MissionPanel");
        _missionTitleLabel = root.Q<Label>("MissionTitleLabel");
        _missionDescLabel = root.Q<Label>("MissionDescLabel");
        _missionProgressFill = root.Q<VisualElement>("MissionProgressFill");
        _missionProgressLabel = root.Q<Label>("MissionProgressLabel");
    }

    public void Update(PlayerStatsModel? stats)
    {
        if (_missionPanel == null || stats == null)
        {
            return;
        }

        if (stats.IsMissionActive)
        {
            UIState.Show(_missionPanel);
        }
        else
        {
            UIState.Hide(_missionPanel);
            return;
        }

        if (_missionTitleLabel != null)
        {
            _missionTitleLabel.text = stats.MissionTitle ?? _loc.Get("hud.mission");
        }

        if (_missionDescLabel != null)
        {
            _missionDescLabel.text = stats.MissionDescription ?? string.Empty;
        }

        float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
        if (_missionProgressFill != null)
        {
            _missionProgressFill.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
        }

        if (_missionProgressLabel != null)
        {
            _missionProgressLabel.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }
    }
}
