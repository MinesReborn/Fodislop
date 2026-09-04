#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Fodinae;
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
    private readonly Func<int, bool> _loopAlive;
    private readonly Dictionary<string, long> _activeBuffs = new();
    private bool _buffLoopStarted;
    private bool _bonusClaimed;
    private int _bonusCountdown;
    private ItemType _pendingBonusItem;
    private int _pendingBonusAmount;
    private int _activeLifecycleVersion;

    public DummyBuffManager(
        Action<ServerPacket> onReceived,
        IAsyncOperationSupervisor operations,
        Func<int, bool> loopAlive)
    {
        _onReceived = onReceived;
        _operations = operations;
        _loopAlive = loopAlive;
    }

    public bool HasActiveBuffs => _activeBuffs.Count > 0;

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
            _ => CheckBuffsLoop(lifecycleVersion));
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

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
            _ => SendDailyBonusMock(lifecycleVersion));
    }

    private async UniTask SendDailyBonusMock(int lifecycleVersion)
    {
        while (LoopAlive(lifecycleVersion))
        {
            _bonusClaimed = false;
            _bonusCountdown = Math.Max(_bonusCountdown, 10);

            while (_bonusCountdown > 0 && !_bonusClaimed && LoopAlive(lifecycleVersion))
            {
                await UniTask.Delay(1000);
                _bonusCountdown--;
            }

            if (!LoopAlive(lifecycleVersion))
            {
                break;
            }

            _pendingBonusItem = DummyCellConfigurationUtilities.PickRandomBonusItem(_rng);
            _pendingBonusAmount = (int)DummyCellConfigurationUtilities.PickRandomAmount(
                _pendingBonusItem,
                _rng);
            _onReceived.Invoke(new ServerPacket(new DailyBonusStatePacket(true)));

            while (!_bonusClaimed && LoopAlive(lifecycleVersion))
            {
                await UniTask.Delay(500);
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

    private async UniTask CheckBuffsLoop(int lifecycleVersion)
    {
        while (LoopAlive(lifecycleVersion))
        {
            await UniTask.Delay(1000);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expired = _activeBuffs.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
            foreach (var tag in expired)
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

    private static readonly System.Random _rng = new();
}
