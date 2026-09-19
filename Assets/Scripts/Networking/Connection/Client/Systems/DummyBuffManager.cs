#nullable enable

using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Utilities;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyBuffManager
{
    private readonly Action<ServerPacket> _onReceived;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IDummyClock _clock;
    private readonly Func<int, bool> _loopAlive;
    private readonly Dictionary<string, long> _activeBuffs = new();
    private readonly List<string> _expiredBuffTags = new();
    private bool _buffLoopStarted;
    private bool _bonusClaimed;
    private int _bonusCountdown;
    private ItemType _pendingBonusItem;
    private int _pendingBonusAmount;
    private int _activeLifecycleVersion;

    public DummyBuffManager(
        Action<ServerPacket> onReceived,
        IAsyncOperationSupervisor operations,
        IDummyClock clock,
        Func<int, bool> loopAlive)
    {
        _onReceived = onReceived;
        _operations = operations;
        _clock = clock;
        _loopAlive = loopAlive;
    }
    public void StartBuffLoop(int lifecycleVersion)
    {
        if (_buffLoopStarted)
        {
            return;
        }

        _buffLoopStarted = true;
        _activeLifecycleVersion = lifecycleVersion;
        _operations.Run(
            "dummy_buff_loop",
            cancellationToken => CheckBuffsLoop(lifecycleVersion, cancellationToken));
    }

    public void ActivateBuff(
        string tag,
        int durationSeconds,
        System.Drawing.Color color,
        string name)
    {
        if (!_buffLoopStarted)
        {
            StartBuffLoop(_activeLifecycleVersion);
        }

        long now = DummyClockTime.UnixSeconds(_clock);
        var expiry = Math.Max(_activeBuffs.GetValueOrDefault(tag), now) + durationSeconds;
        _activeBuffs[tag] = expiry;
        _onReceived.Invoke(new ServerPacket(new AddStatusLinePacket(0, color, tag, new[] { name, expiry.ToString() })));
    }

    public void Reset()
    {
        _buffLoopStarted = false;
        _activeBuffs.Clear();
        _bonusClaimed = false;
        _bonusCountdown = 0;
    }

    public void ResetLoopGuard()
    {
        _buffLoopStarted = false;
    }

    public void ResetDailyBonus()
    {
        _bonusCountdown = 10;
        _bonusClaimed = false;
    }

    public void HandleDailyBonusClaim(Dictionary<ItemType, long> inventory)
    {
        var rewardItem = _pendingBonusItem;
        var rewardAmount = _pendingBonusAmount;

        inventory.TryGetValue(rewardItem, out long current);
        long newQty = current + rewardAmount;
        inventory[rewardItem] = newQty;

        _onReceived.Invoke(new ServerPacket(new InventoryPacket(
            new Dictionary<ItemType, long> { { rewardItem, newQty } })));

        _bonusClaimed = true;
    }

    public void StartDailyBonusLoop(int lifecycleVersion)
    {
        _operations.Run(
            "dummy_daily_bonus_loop",
            cancellationToken => SendDailyBonusMock(lifecycleVersion, cancellationToken));
    }

    private async UniTask SendDailyBonusMock(int lifecycleVersion, CancellationToken cancellationToken)
    {
        while (LoopAlive(lifecycleVersion))
        {
            _bonusClaimed = false;
            _bonusCountdown = Math.Max(_bonusCountdown, 10);

            while (_bonusCountdown > 0 && !_bonusClaimed && LoopAlive(lifecycleVersion))
            {
                await _clock.Delay(1000, cancellationToken);
                _bonusCountdown--;
            }

            if (!LoopAlive(lifecycleVersion))
            {
                break;
            }

            _pendingBonusItem = DummyCellConfigurationUtilities.PickRandomBonusItem(_clock.Random);
            _pendingBonusAmount = (int)DummyCellConfigurationUtilities.PickRandomAmount(
                _pendingBonusItem,
                _clock.Random);
            _onReceived.Invoke(new ServerPacket(new DailyBonusStatePacket(true)));

            while (!_bonusClaimed && LoopAlive(lifecycleVersion))
            {
                await _clock.Delay(500, cancellationToken);
            }

            if (!LoopAlive(lifecycleVersion))
            {
                break;
            }

            _bonusCountdown = 10;
            _onReceived.Invoke(new ServerPacket(new DailyBonusStatePacket(false)));
        }
    }
    public void SendStatusPackets()
    {
        foreach (var kvp in _activeBuffs)
        {
            var (color, name) = kvp.Key switch
            {
                "xp3" => (System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3"),
                "freeup" => (System.Drawing.Color.Cyan, "Freeup"),
                "x4" => (System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4"),
                "battery" => (System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор"),
                _ => (System.Drawing.Color.White, kvp.Key),
            };
            _onReceived.Invoke(new ServerPacket(new AddStatusLinePacket(0, color, kvp.Key, new[] { name, kvp.Value.ToString() })));
        }
    }

    private async UniTask CheckBuffsLoop(int lifecycleVersion, CancellationToken cancellationToken)
    {
        while (LoopAlive(lifecycleVersion))
        {
            await _clock.Delay(1000, cancellationToken);
            long now = DummyClockTime.UnixSeconds(_clock);
            _expiredBuffTags.Clear();
            foreach (KeyValuePair<string, long> active in _activeBuffs)
            {
                if (active.Value <= now)
                {
                    _expiredBuffTags.Add(active.Key);
                }
            }

            foreach (string tag in _expiredBuffTags)
            {
                _activeBuffs.Remove(tag);
                _onReceived.Invoke(new ServerPacket(new ClearStatusLinePacket(tag)));
            }
        }
    }

    private bool LoopAlive(int lifecycleVersion)
    {
        return _loopAlive(lifecycleVersion);
    }
}
