using System.Collections.Generic;
using Fodinae.Scripts.Game;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class RobotManager : MonoBehaviour
    {
        private static RobotManager _instance;
        private static bool _isQuitting = false;

        [SerializeField]
        private GameObject _robotPrefab;

        private Dictionary<uint, Robot> _robots = new();

        public static RobotManager InstanceIfExists => _instance;

        public static RobotManager Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<RobotManager>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[RobotManager]");
                        _instance = go.AddComponent<RobotManager>();

                        // System Grouping
                        if (Application.isPlaying)
                        {
                            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                            UnityEngine.Object.DontDestroyOnLoad(parent);
                            go.transform.SetParent(parent.transform);
                        }
                    }
                }

                return _instance;
            }
        }

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

        /// <summary>
        /// Removes a robot's registry entry only if the stored instance is
        /// still <paramref name="instance"/>. Safe to call from the robot's
        /// own OnDestroy: it will not evict a newer robot that re-registered
        /// under the same botId, and it does not Destroy anything.
        /// </summary>
        public void UnregisterRobot(uint botId, Robot instance)
        {
            if (_robots.TryGetValue(botId, out var robot) && robot == instance)
            {
                _robots.Remove(botId);
            }
        }

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);

                // Ensure parented if created in scene
                var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                UnityEngine.Object.DontDestroyOnLoad(parent);
                transform.SetParent(parent.transform);
            }

            _isQuitting = false;
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }
    }
}
