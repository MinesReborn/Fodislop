#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.Game.Managers;

// Чистый сервис контейнера (docs/architecture/SCENE_STANDARD.md §1): роботы создаются фабрикой
// под Runtime/Robots, сам сервис объекта на сцене не имеет.
public sealed class RobotManager(
    ISceneObjectFactory sceneObjects,
    ILocalPlayerState localPlayer) : IRobotService
{
    private const string TAG = "[RobotManager]";
    private readonly Dictionary<uint, Robot> _robots = new();
    private readonly Dictionary<uint, float> _lastSeenAt = new();
    private readonly HashSet<uint> _overwriteWarningsLogged = [];
    private readonly List<uint> _keysToRemove = [];

    public uint LocalPlayerBotId { get; private set; }

    public int RobotCount => _robots.Count;

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

    public IRobotView GetOrCreateRobot(uint botId)
    {
        if (_robots.TryGetValue(botId, out var robot))
        {
            return robot;
        }

        if (botId != 0 && botId == LocalPlayerBotId)
        {
            var pmc = localPlayer.Current;
            var playerObj = pmc != null ? pmc.gameObject : null;
            if (playerObj != null)
            {
                robot = playerObj.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.Initialize(botId);
                    _robots[botId] = robot;
                    return robot;
                }
            }
        }

        robot = sceneObjects.Create<Robot>($"Robot_{botId}", RuntimeOwner.Robots);

        robot.Initialize(botId);
        _robots[botId] = robot;
        return robot;
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
        var robot = GetOrCreateRobot(botId);
        robot.SetPosition(x, y);
        robot.SetRotation(rotation);
        _lastSeenAt[botId] = Time.unscaledTime;
    }

    public void UpdateRobotMetadata(uint botId, RobotMetadata metadata)
    {
        var robot = GetOrCreateRobot(botId);
        robot.SetMetadata(metadata.PlayerId, metadata.ClanId, metadata.Nickname, metadata.SkinPath, metadata.TailPath);
        _lastSeenAt[botId] = Time.unscaledTime;
    }

    public void SetLocalPlayerBotId(uint botId)
    {
        LocalPlayerBotId = botId;

        // Если фабричный бот под этим id был создан до того, как сервер
        // сообщил наш BotId (PlayerInfoPacket), — заменяем его игровым
        // объектом локального игрока, иначе метаданные/визуалы навсегда
        // достанутся фабричному боту и world-readiness gate не сойдётся.
        var pmc = localPlayer.Current;
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
    }
}
