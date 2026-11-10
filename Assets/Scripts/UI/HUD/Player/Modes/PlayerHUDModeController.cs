#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client.Packets.Actions;
using UnityEngine.UIElements;

using Kern.UI.Tooltips;

namespace Kern.UI.HUD;

internal sealed class PlayerHUDModeController : IDisposable
{
    private readonly ILocalPlayerState _localPlayer;
    private readonly ILocalizationService _loc;
    private readonly INetworkService? _networkService;

    private Button? _autoDigButton;

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

        Tooltip.AttachTo(_autoDigButton, () => _loc.Get("hud.tooltip.autodig"), tooltip);

        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged += UpdateAutoDigButton;
            UpdateAutoDigButton(player.AutoDig);

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

    // Подпись постоянная («Автокопание»), состояние — бегунок переключателя (макет: .toggle[aria-pressed])
    public void UpdateAutoDigButton(bool enabled) => _autoDigButton?.EnableInClassList("is-on", enabled);

    public void Dispose()
    {
        var player = _localPlayer.Current;
        if (player != null)
        {
            player.OnAutoDigChanged -= UpdateAutoDigButton;
        }
    }
}
