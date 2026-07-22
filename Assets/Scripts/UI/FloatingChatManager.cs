using Fodinae.Scripts.Core;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.UI
{
    public class FloatingChatManager : MonoBehaviour
    {
        private static FloatingChatManager _instance;
        public static FloatingChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<FloatingChatManager>();
                }

                return _instance;
            }
        }

        private Camera _camera;
        private FloatingChatBubble _bubblePrefab;
        private readonly System.Collections.Generic.List<FloatingChatBubble> _activeBubbles = new();

        protected void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        protected void Start()
        {
            _camera = Camera.main;

            var prefabGo = new GameObject("ChatBubblePrefab");
            prefabGo.transform.SetParent(transform);
            _bubblePrefab = prefabGo.AddComponent<FloatingChatBubble>();
            prefabGo.SetActive(false);
        }

        protected void Update()
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (_activeBubbles[i] == null || !_activeBubbles[i].gameObject.activeInHierarchy)
                {
                    _activeBubbles.RemoveAt(i);
                }
            }
        }

        public void ShowLocalChat(LocalChatMessagePacket packet)
        {
            var robot = Game.Managers.RobotManager.Instance.GetOrCreateRobot(packet.BotId);
            if (robot == null)
            {
                return;
            }

            if (!IsInCameraView(robot.transform.position))
            {
                return;
            }

            var go = Instantiate(_bubblePrefab.gameObject, transform);
            go.transform.position = robot.transform.position + (Vector3.up * 1.8f);
            var bubble = go.GetComponent<FloatingChatBubble>();
            bubble.Init(packet.Text);
            _activeBubbles.Add(bubble);
        }

        private bool IsInCameraView(Vector3 worldPos)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return false;
            }

            Vector3 vp = _camera.WorldToViewportPoint(worldPos);
            return vp.x >= -0.15f && vp.x <= 1.15f && vp.y >= -0.15f && vp.y <= 1.15f;
        }
    }
}
