using UnityEngine;
using System.Collections.Generic;
using Fodinae.Assets.Scripts.Game;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class RobotManager : MonoBehaviour
    {
        private static RobotManager _instance;
        public static RobotManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<RobotManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[RobotManager]");
                        _instance = go.AddComponent<RobotManager>();
                    }
                }
                return _instance;
            }
        }

        [SerializeField] private GameObject _robotPrefab;
        private Dictionary<uint, Robot> _robots = new();
        public uint LocalPlayerBotId { get; set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void RegisterRobot(Robot robot)
        {
            if (robot == null) return;
            _robots[robot.BotId] = robot;
        }

        public Robot GetOrCreateRobot(uint botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                return robot;
            }

            // If this is the local player, try to find the existing robot in the scene
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
    }
}
