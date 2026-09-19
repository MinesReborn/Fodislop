#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Managers;
using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Kern.UI
{
    public class FloatingChatManager : MonoBehaviour
    {
        [Inject]
        private RobotManager _robotManager = null!;

        private Camera? _camera;
        private readonly List<FloatingChatBubble> _activeBubbles = new();
        private readonly Queue<FloatingChatBubble> _pool = new();
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ChatEventGateway _chatEvents = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        protected void Start()
        {
            _chatEvents.LocalMessageReceived += ShowLocalChat;
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_camera == null)
            {
                // IGameplayCamera is a required [Inject] registration on the
                // Bootstrap scope; a null camera is a wiring defect, not a
                // transient.
                if (_gameplayCamera == null)
                {
                    throw new InvalidOperationException(
                        "[FloatingChatManager] Required IGameplayCamera injection is missing; " +
                        "FloatingChatManager must be registered in the Game scope.");
                }

                // _gameplayCamera was null-compared above, so the compiler
                // cannot narrow it here without the null-forgiving operator.
                _camera = _gameplayCamera!.Camera;
            }

            if (_sceneObjects == null)
            {
                throw new InvalidOperationException(
                    "[FloatingChatManager] Required ISceneObjectFactory injection is missing; " +
                    "FloatingChatManager must be registered in the Game scope.");
            }
        }

        protected void Update()
        {
            if (_activeBubbles.Count == 0)
            {
                return;
            }

            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                if (_activeBubbles[i] == null || !_activeBubbles[i].gameObject.activeInHierarchy)
                {
                    ReturnToPool(_activeBubbles[i]);
                    _activeBubbles.RemoveAt(i);
                }
            }
        }

        protected void OnDestroy()
        {
            if (_chatEvents != null)
            {
                _chatEvents.LocalMessageReceived -= ShowLocalChat;
            }

            _activeBubbles.Clear();
            while (_pool.Count > 0)
            {
                var bubble = _pool.Dequeue();
                if (bubble != null)
                {
                    Destroy(bubble.gameObject);
                }
            }
        }

        public void ShowLocalChat(LocalChatMessagePacket packet)
        {
            TryInitialize();
            if (_camera == null)
            {
                _camera = _gameplayCamera.Camera;
            }

            ExpireBubbleOf((int)packet.BotId);

            var bubble = GetFromPool();
            if (bubble == null)
            {
                return;
            }

            var robot = _robotManager?.GetOrCreateRobot(packet.BotId);
            if (robot == null)
            {
                ReturnToPool(bubble);
                return;
            }

            bubble.Init((int)packet.BotId, packet.Text, robot.transform);
            _activeBubbles.Add(bubble);
        }

        private void ExpireBubbleOf(int ownerID)
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                FloatingChatBubble bubble = _activeBubbles[i];
                if (bubble != null && bubble.OwnerID == ownerID)
                {
                    bubble.Expire();
                }
            }
        }

        private FloatingChatBubble? GetFromPool()
        {
            while (_pool.Count > 0)
            {
                var bubble = _pool.Dequeue();
                if (bubble != null)
                {
                    bubble.gameObject.SetActive(true);
                    return bubble;
                }
            }

            if (_sceneObjects == null)
            {
                return null;
            }

            var newBubble = _sceneObjects.Create<FloatingChatBubble>("ChatBubble", RuntimeOwner.FloatingUI);
            newBubble.transform.SetParent(transform, false);
            return newBubble;
        }

        private void ReturnToPool(FloatingChatBubble? bubble)
        {
            if (bubble == null)
            {
                return;
            }

            bubble.gameObject.SetActive(false);
            _pool.Enqueue(bubble);
        }

    }
}
