using System.Collections.Generic;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Utils;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class RobotManager : SingletonMonoBehaviour<RobotManager>
    {
        [SerializeField]
        private GameObject _robotPrefab;

        private Dictionary<uint, Robot> _robots = new();

        public static bool ShowDebugVisuals { get; set; }

        public uint LocalPlayerBotId { get; set; }

        public void RegisterRobot(Robot robot)
        {
            if (robot == null)
            {
                return;
            }

            _robots[robot.BotId] = robot;
        }

        public Robot GetOrCreateRobot(uint botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                return robot;
            }

            if (botId != 0 && botId == LocalPlayerBotId)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
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

            GameObject robotGo;
            if (_robotPrefab != null)
            {
                robotGo = Instantiate(_robotPrefab, transform);
            }
            else
            {
                robotGo = new GameObject($"Robot_{botId}");
                robotGo.transform.SetParent(transform);
                robotGo.AddComponent<SpriteRenderer>();
            }

            robot = robotGo.GetComponent<Robot>();
            if (robot == null)
            {
                robot = robotGo.AddComponent<Robot>();
            }

            robot.Initialize(botId);
            _robots[botId] = robot;
            return robot;
        }

        public void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetPosition(x, y);
            robot.SetRotation(rotation);
        }

        public void UpdateRobotMetadata(uint botId, int playerId, byte clanId, string nickname, string skinPath, string tailPath)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetMetadata(playerId, clanId, nickname, skinPath, tailPath);
        }

        public void RemoveRobot(uint botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                Destroy(robot.gameObject);
                _robots.Remove(botId);
            }
        }

        public void ClearAllRobots()
        {
            var keysToRemove = new List<uint>();
            foreach (var kvp in _robots)
            {
                if (kvp.Key == LocalPlayerBotId || (kvp.Value != null && kvp.Value.gameObject.CompareTag("Player")))
                {
                    continue;
                }

                if (kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }

                keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                _robots.Remove(key);
            }
        }

        public void UnregisterRobot(uint botId, Robot instance)
        {
            if (_robots.TryGetValue(botId, out var robot) && robot == instance)
            {
                _robots.Remove(botId);
            }
        }
    }
}
