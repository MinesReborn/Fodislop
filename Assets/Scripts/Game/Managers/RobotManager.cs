#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.Game.Managers;

// Чистый сервис контейнера (SCENE_STANDARD.md §1): роботы создаются фабрикой
// под Runtime/Robots, сам сервис объекта на сцене не имеет.
public sealed class RobotManager(
    ISceneObjectFactory sceneObjects,
    ILocalPlayerState localPlayer) : IRobotService
{
    private const string TAG = "[RobotManager]";
    private readonly Dictionary<uint, Robot> _robots = new();
    private readonly HashSet<uint> _overwriteWarningsLogged = [];
    private readonly List<uint> _keysToRemove = [];

    public uint LocalPlayerBotID { get; private set; }

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
            if (ReferenceEquals(kvp.Value, robot) && kvp.Key != robot.BotID)
            {
                staleKey = kvp.Key;
                break;
            }
        }

        if (staleKey.HasValue)
        {
            _robots.Remove(staleKey.Value);
        }

        if (_robots.TryGetValue(robot.BotID, out var existing))
        {
            if (ReferenceEquals(existing, robot))
            {
                return;
            }

            // Server resends can target a bot whose stale instance is still
            // registered. Warn once per bot id so a resend storm cannot
            // flood the console.
            if (_overwriteWarningsLogged.Add(robot.BotID))
            {
                Debug.LogWarning($"{TAG} Robot {robot.BotID} already registered, overwriting");
            }
        }

        _robots[robot.BotID] = concrete;
    }

    public IRobotView GetOrCreateRobot(uint botID)
    {
        if (_robots.TryGetValue(botID, out var robot))
        {
            return robot;
        }

        if (botID != 0 && botID == LocalPlayerBotID)
        {
            var pmc = localPlayer.Current;
            var playerObj = pmc != null ? pmc.gameObject : null;
            if (playerObj != null)
            {
                robot = playerObj.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.Initialize(botID);
                    _robots[botID] = robot;
                    return robot;
                }
            }
        }

        robot = sceneObjects.Create<Robot>($"Robot_{botID}", RuntimeOwner.Robots);

        robot.Initialize(botID);
        _robots[botID] = robot;
        return robot;
    }

    public void UpdateRobotPosition(uint botID, ushort x, ushort y, byte rotation)
    {
        var robot = GetOrCreateRobot(botID);
        robot.SetPosition(x, y);
        robot.SetRotation(rotation);
    }

    public void UpdateRobotMetadata(uint botID, RobotMetadata metadata)
    {
        var robot = GetOrCreateRobot(botID);
        robot.SetMetadata(metadata.PlayerID, metadata.ClanID, metadata.Nickname, metadata.SkinPath, metadata.TailPath);
    }

    public void SetLocalPlayerBotID(uint botID)
    {
        LocalPlayerBotID = botID;

        // Если фабричный бот под этим id был создан до того, как сервер
        // сообщил наш BotID (PlayerInfoPacket), — заменяем его игровым
        // объектом локального игрока, иначе метаданные/визуалы навсегда
        // достанутся фабричному боту и world-readiness gate не сойдётся.
        var pmc = localPlayer.Current;
        var playerRobot = pmc != null ? pmc.GetComponent<Robot>() : null;
        if (playerRobot != null && _robots.TryGetValue(botID, out var existing) &&
            !ReferenceEquals(existing, playerRobot))
        {
            Object.Destroy(existing.gameObject);
            _robots.Remove(botID);
            Debug.Log($"{TAG} Replaced factory bot {botID} with local player robot");
        }
    }

    public void ClearAllRobots()
    {
        int cleared = 0;
        _overwriteWarningsLogged.Clear();
        _keysToRemove.Clear();
        foreach (var kvp in _robots)
        {
            if (kvp.Key == LocalPlayerBotID || (kvp.Value != null && kvp.Value.gameObject.CompareTag("Player")))
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
            cleared++;
        }

        Debug.Log($"{TAG} Cleared {cleared} robots, kept {(_robots.ContainsKey(LocalPlayerBotID) ? "local player" : "none")}");
    }

    public void UnregisterRobot(uint botID)
    {
        _robots.Remove(botID);
        _overwriteWarningsLogged.Remove(botID);
    }
}
