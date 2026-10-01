#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

internal sealed class PlayerHUDModeController : IDisposable
{
    private readonly ILocalPlayerState _localPlayer;
    private readonly ILocalizationService _loc;
    private readonly INetworkService? _networkService;

    private Button? _autoDigButton;
    private Label? _autoDigLabel;
    private Button? _aggressionButton;
    private Label? _aggressionLabel;

    public PlayerHUDModeController(ILocalPlayerState localPlayer, ILocalizationService loc, INetworkService? networkService = null)
    {
        _localPlayer = localPlayer;
        _loc = loc;
        _networkService = networkService;
    }

    public void Initialize(VisualElement root, Tooltip tooltip)
    {
        _autoDigButton = root.Q<Button>("AutoDigButton") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigButton is missing from PlayerHUD.uxml.");
        _autoDigButton.clicked += ToggleAutoDig;

        _autoDigLabel = root.Q<Label>("AutoDigLabel") ??
            throw new InvalidOperationException("[PlayerHUD] AutoDigLabel is missing from PlayerHUD.uxml.");

        Tooltip.AttachTo(_autoDigButton, () => _loc.Get("hud.tooltip.autodig"), tooltip);

        _aggressionButton = root.Q<Button>("AggressionButton") ??
            throw new InvalidOperationException("[PlayerHUD] AggressionButton is missing from PlayerHUD.uxml.");
        _aggressionButton.clicked += ToggleAggression;

        _aggressionLabel = root.Q<Label>("AggressionLabel") ??
            throw new InvalidOperationException("[PlayerHUD] AggressionLabel is missing from PlayerHUD.uxml.");

        Tooltip.AttachTo(_aggressionButton, () => _loc.Get("hud.tooltip.aggression"), tooltip);

        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged += UpdateAutoDigButton;
            UpdateAutoDigButton(player.AutoDig);

            player.OnAggressionChanged += UpdateAggressionButton;
            UpdateAggressionButton(player.Aggression);
        }
    }

    public void ToggleAutoDig()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.AutoDig = !player.AutoDig;
            // Серверу уходит ToggleAutoDigPacket, он подтверждает пакетом
            // AutoMineStatePacket (тот же путь, что у хоткея E).
            _networkService?.SendAction(new ToggleAutoDigPacket());
        }
    }

    public void ToggleAggression()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            // Оптимистично переключаем локально для мгновенного отклика;
            // сервер подтверждает пакетом AggressionStatePacket.
            player.Aggression = !player.Aggression;
            _networkService?.SendAction(new ToggleAgressionPacket());
        }
    }

    public void UpdateAutoDigButton(bool enabled)
    {
        _autoDigButton?.EnableInClassList("enabled", enabled);
        if (_autoDigLabel != null)
        {
            _autoDigLabel.text = enabled ? _loc.Get("hud.autodig.on") : _loc.Get("hud.autodig.off");
        }
    }

    public void UpdateAggressionButton(bool enabled)
    {
        _aggressionButton?.EnableInClassList("enabled", enabled);
        if (_aggressionLabel != null)
        {
            _aggressionLabel.text = enabled ? _loc.Get("hud.aggression.on") : _loc.Get("hud.aggression.off");
        }
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
