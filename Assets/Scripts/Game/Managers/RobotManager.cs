using UnityEngine;
using System.Collections.Generic;

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
        private Dictionary<ushort, Robot> _robots = new();

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

        public Robot GetOrCreateRobot(ushort botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                return robot;
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

        public void UpdateRobotPosition(ushort botId, ushort x, ushort y, byte rotation)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetPosition(x, y);
            robot.SetRotation(rotation);
        }

        public void UpdateRobotMetadata(ushort botId, int playerId, string nickname, string skinPath, string tailPath)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetMetadata(playerId, nickname, skinPath, tailPath);
        }

        public void RemoveRobot(ushort botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                Destroy(robot.gameObject);
                _robots.Remove(botId);
            }
        }
    }
}
