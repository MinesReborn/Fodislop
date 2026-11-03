#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.Game.Entities.Robots;

// Чистый сервис контейнера (docs/architecture/SCENE_STANDARD.md §1): роботы создаются фабрикой
// под Runtime/Robots, сам сервис объекта на сцене не имеет.
public sealed class RobotManager : IRobotService
{
    private const string TAG = "[RobotManager]";
    private const int MaximumRobotsWithTails = 50;
    private readonly ISceneObjectFactory _sceneObjects;
    private readonly ILocalPlayerState _localPlayer;
    private readonly Dictionary<uint, Robot> _robots = new();
    private readonly Dictionary<uint, float> _lastSeenAt = new();
    private readonly HashSet<uint> _overwriteWarningsLogged = new();
    private readonly List<uint> _keysToRemove = new();
    private readonly HashSet<uint> _robotsWithTailSlots = new();
    private uint? _priorityTailBotId;
    private bool _robotLimitWarningLogged;

    public RobotManager(ISceneObjectFactory sceneObjects, ILocalPlayerState localPlayer)
    {
        _sceneObjects = sceneObjects;
        _localPlayer = localPlayer;
    }

    public uint LocalPlayerBotId { get; private set; }

    public int RobotCount => _robots.Count;

    public bool TryReserveTailSlot(uint botId, bool prioritize)
    {
        if (_robotsWithTailSlots.Contains(botId))
        {
            if (prioritize)
            {
                _priorityTailBotId = botId;
            }

            return true;
        }

        if (_robotsWithTailSlots.Count < MaximumRobotsWithTails)
        {
            _robotsWithTailSlots.Add(botId);
            if (prioritize)
            {
                _priorityTailBotId = botId;
            }

            return true;
        }

        if (!prioritize)
        {
            return false;
        }

        uint? victimBotId = null;
        foreach (uint candidateId in _robotsWithTailSlots)
        {
            if (candidateId != botId &&
                candidateId != LocalPlayerBotId &&
                candidateId != _priorityTailBotId)
            {
                victimBotId = candidateId;
                break;
            }
        }

        if (!victimBotId.HasValue)
        {
            return false;
        }

        _robotsWithTailSlots.Remove(victimBotId.Value);
        if (_robots.TryGetValue(victimBotId.Value, out Robot? victim))
        {
            victim.ReleaseTailSlotFromManager();
        }

        _robotsWithTailSlots.Add(botId);
        if (prioritize)
        {
            _priorityTailBotId = botId;
        }

        return true;
    }

    public void ReleaseTailSlot(uint botId)
    {
        _robotsWithTailSlots.Remove(botId);
        if (_priorityTailBotId == botId)
        {
            _priorityTailBotId = null;
        }
    }

    public void RegisterRobot(IRobotView robot)
    {
        if (robot is not Robot concrete)
        {
            Debug.LogWarning($"{TAG} RegisterRobot called with non-Robot view");
            return;
        }

        // Same instance re-registered (e.g. Start() + Initialize()) — idempotent.
        uint? staleKey = null;
        foreach (var kvp in _robots)
        {
            if (ReferenceEquals(kvp.Value, robot) && kvp.Key != robot.BotId)
            {
                staleKey = kvp.Key;
                break;
            }
        }

        if (staleKey.HasValue)
        {
            _robots.Remove(staleKey.Value);
        }

        if (_robots.TryGetValue(robot.BotId, out var existing))
        {
            if (ReferenceEquals(existing, robot))
            {
                return;
            }

            // Server resends can target a bot whose stale instance is still
            // registered. Warn once per bot id so a resend storm cannot
            // flood the console.
            if (_overwriteWarningsLogged.Add(robot.BotId))
            {
                Debug.LogWarning($"{TAG} Robot {robot.BotId} already registered, overwriting");
            }
        }

        _robots[robot.BotId] = concrete;
    }

    private bool TryGetOrCreateRobot(uint botId, [NotNullWhen(true)] out Robot? robot)
    {
        if (_robots.TryGetValue(botId, out robot))
        {
            return true;
        }

        if (botId != 0 && botId == LocalPlayerBotId)
        {
            var pmc = _localPlayer.Current;
            var playerObj = pmc != null ? pmc.gameObject : null;
            if (playerObj != null)
            {
                robot = playerObj.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.Initialize(botId);
                    _robots[botId] = robot;
                    return true;
                }
            }
        }

        if (_robots.Count >= RobotPopulationLimits.MaximumRobots)
        {
            if (!_robotLimitWarningLogged)
            {
                _robotLimitWarningLogged = true;
                Debug.LogWarning(
                    $"{TAG} Robot limit {RobotPopulationLimits.MaximumRobots} reached; " +
                    "updates for new bot ids are dropped until stale robots are pruned.");
            }

            robot = null;
            return false;
        }

        robot = _sceneObjects.Create<Robot>($"Robot_{botId}", RuntimeOwner.Robots);

        robot.Initialize(botId);
        _robots[botId] = robot;
        return true;
    }

    public bool TryGetRobot(uint botId, out IRobotView? robot)
    {
        if (_robots.TryGetValue(botId, out var existing))
        {
            robot = existing;
            return true;
        }

        robot = null;
        return false;
    }

    public void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation)
    {
        if (!TryGetOrCreateRobot(botId, out Robot? robot))
        {
            return;
        }

        robot.SetPosition(x, y);
        robot.SetRotation(rotation);
        _lastSeenAt[botId] = Time.unscaledTime;
    }

    public void UpdateRobotMetadata(uint botId, RobotMetadata metadata)
    {
        if (!TryGetOrCreateRobot(botId, out Robot? robot))
        {
            return;
        }

        robot.SetMetadata(metadata.PlayerId, metadata.ClanId, metadata.Nickname, metadata.SkinPath, metadata.TailPath);
        _lastSeenAt[botId] = Time.unscaledTime;
    }

    public void SetLocalPlayerBotId(uint botId)
    {
        LocalPlayerBotId = botId;
        _priorityTailBotId = _robotsWithTailSlots.Contains(botId) ? botId : null;

        // Если фабричный бот под этим id был создан до того, как сервер
        // сообщил наш BotId (PlayerInfoPacket), — заменяем его игровым
        // объектом локального игрока, иначе метаданные/визуалы навсегда
        // достанутся фабричному боту и world-readiness gate не сойдётся.
        var pmc = _localPlayer.Current;
        var playerRobot = pmc != null ? pmc.GetComponent<Robot>() : null;
        if (playerRobot != null && _robots.TryGetValue(botId, out var existing) &&
            !ReferenceEquals(existing, playerRobot))
        {
            Object.Destroy(existing.gameObject);
            _robots.Remove(botId);
            _lastSeenAt.Remove(botId);
            Debug.Log($"{TAG} Replaced factory bot {botId} with local player robot");
        }
    }

    public void ClearAllRobots()
    {
        int cleared = 0;
        _overwriteWarningsLogged.Clear();
        _robotLimitWarningLogged = false;
        _keysToRemove.Clear();
        foreach (var kvp in _robots)
        {
            if (kvp.Key == LocalPlayerBotId || (kvp.Value != null && kvp.Value.gameObject.CompareTag("Player")))
            {
                continue;
            }

            if (kvp.Value != null)
            {
                Object.Destroy(kvp.Value.gameObject);
            }

            _keysToRemove.Add(kvp.Key);
        }

        foreach (uint key in _keysToRemove)
        {
            _robots.Remove(key);
            _lastSeenAt.Remove(key);
            cleared++;
        }

        Debug.Log($"{TAG} Cleared {cleared} robots, kept {(_robots.ContainsKey(LocalPlayerBotId) ? "local player" : "none")}");
    }

    public void PruneStaleRobots(float timeoutSeconds = 2.5f)
    {
        float now = Time.unscaledTime;
        _keysToRemove.Clear();
        foreach (var pair in _lastSeenAt)
        {
            if (pair.Key == LocalPlayerBotId || now - pair.Value <= timeoutSeconds)
            {
                continue;
            }

            if (_robots.TryGetValue(pair.Key, out Robot? robot) && robot != null)
            {
                Object.Destroy(robot.gameObject);
            }

            _keysToRemove.Add(pair.Key);
        }

        foreach (uint key in _keysToRemove)
        {
            _lastSeenAt.Remove(key);
            _robots.Remove(key);
        }
    }

    public void UnregisterRobot(uint botId)
    {
        _robots.Remove(botId);
        _lastSeenAt.Remove(botId);
        _overwriteWarningsLogged.Remove(botId);
        ReleaseTailSlot(botId);
    }
}
