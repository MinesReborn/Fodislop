#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Manages AutoDig and Aggression mode toggle buttons, status LEDs, and tooltips in Player HUD.
/// </summary>
internal sealed class PlayerHUDModeController : IDisposable
{
    private readonly ILocalPlayerState _localPlayer;
    private readonly ILocalizationService _loc;

    private Button? _autoDigButton;
    private VisualElement? _autoDigIndicator;
    private Label? _autoDigLabel;
    private Button? _aggressionButton;
    private VisualElement? _aggressionIndicator;
    private Label? _aggressionLabel;

    public PlayerHUDModeController(ILocalPlayerState localPlayer, ILocalizationService loc)
    {
        _localPlayer = localPlayer;
        _loc = loc;
    }

    public void Initialize(VisualElement root, Tooltip tooltip)
    {
        _autoDigButton = root.Q<Button>("AutoDigButton") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigButton is missing from PlayerHUD.uxml.");
        _autoDigButton.clicked += ToggleAutoDig;

        _autoDigIndicator = root.Q<VisualElement>("AutoDigIndicator") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigIndicator is missing from PlayerHUD.uxml.");

        _autoDigLabel = root.Q<Label>("AutoDigLabel") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigLabel is missing from PlayerHUD.uxml.");

        Tooltip.AttachTo(_autoDigButton, () => _loc.Get("hud.tooltip.autodig"), tooltip);

        _aggressionButton = root.Q<Button>("AggressionButton") ??
            throw new InvalidOperationException("[PlayerHUD] AggressionButton is missing from PlayerHUD.uxml.");
        _aggressionButton.clicked += ToggleAggression;

        _aggressionIndicator = root.Q<VisualElement>("AggressionIndicator") ??
            throw new InvalidOperationException("[PlayerHUD] AggressionIndicator is missing from PlayerHUD.uxml.");

        _aggressionLabel = root.Q<Label>("AggressionLabel") ??
            throw new InvalidOperationException("[PlayerHUD] AggressionLabel is missing from PlayerHUD.uxml.");

        Tooltip.AttachTo(_aggressionButton, () => _loc.Get("hud.tooltip.aggression"), tooltip);

        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged += UpdateAutoDigButton;
            player.OnAggressionChanged += UpdateAggressionButton;
            UpdateAutoDigButton(player.AutoDig);
            UpdateAggressionButton(player.Aggression);
        }
    }

    public void ToggleAutoDig()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.AutoDig = !player.AutoDig;
        }
    }

    public void UpdateAutoDigButton(bool enabled)
    {
        _autoDigButton?.EnableInClassList("enabled", enabled);
        if (_autoDigLabel != null)
        {
            _autoDigLabel.text = enabled ? _loc.Get("hud.autodig.on") : _loc.Get("hud.autodig.off");
        }

        _autoDigIndicator?.EnableInClassList("hud-mode-led--active", enabled);
    }

    public void ToggleAggression()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.ToggleAggression();
        }
    }

    public void UpdateAggressionButton(bool enabled)
    {
        _aggressionButton?.EnableInClassList("enabled", enabled);
        if (_aggressionLabel != null)
        {
            _aggressionLabel.text = enabled ? _loc.Get("hud.aggression.on") : _loc.Get("hud.aggression.off");
        }

        _aggressionIndicator?.EnableInClassList("hud-mode-led--active", enabled);
    }

    public void Dispose()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged -= UpdateAutoDigButton;
            player.OnAggressionChanged -= UpdateAggressionButton;
        }
    }
}
