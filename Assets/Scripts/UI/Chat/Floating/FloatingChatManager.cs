#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Managers;
using Kern.Networking;
using Kern.World;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class FloatingChatManager : MonoBehaviour
    {
        [Inject]
        private RobotManager _robotManager = null!;

        [Inject]
        private IMapDataProvider _mapData = null!;

        private Camera? _camera;
        private readonly List<FloatingChatBubble> _activeBubbles = new();
        private readonly Queue<FloatingChatBubble> _pool = new();
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ChatEventGateway _chatEvents = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private UIDocument _uiDocument = null!;
        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;
        [Inject]
        private UIInputManager _uiInput = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        private LocalChatInput? _localInput;

        protected void Start()
        {
            _chatEvents.LocalMessageReceived += ShowLocalChat;
            TryInitialize();
            PrewarmBubble();

            // Школа (одна дорога): [Inject]-методы и панель UIDocument создаются
            // до Start, один вызов без ретраев из Update.
            _localInput = new LocalChatInput(
                _uiDocument,
                _networkService,
                _inputBlocker,
                _uiInput,
                _operations,
                destroyCancellationToken);
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
            _localInput?.Tick();

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

            _localInput?.Dispose();
            _localInput = null;

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

            long poolStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var bubble = GetFromPool();
            RecordIfSlow("облако чата из пула", poolStart);
            if (bubble == null)
            {
                return;
            }

            long showStart = System.Diagnostics.Stopwatch.GetTimestamp();

            // GetOrCreateRobot здесь материализовал бы призрака: сервер шлёт
            // локальный чат игрокам из чанков вокруг отправителя, а клиент
            // видит не весь квадрат. Сообщение от незнакомого botId создавало
            // робота в нулевой координате без метаданных, и тот висел до
            // prune. Поэтому ищем только существующего, а координаты от
            // отправителя используем как якорь.
            if (_robotManager != null && _robotManager.TryGetRobot(packet.BotId, out var robot) && robot != null)
            {
                bubble.Init((int)packet.BotId, packet.Text, robot.transform);
            }
            else
            {
                bubble.Init((int)packet.BotId, packet.Text, ResolveFallbackPosition(packet));
            }

            RecordIfSlow("облако чата: показ", showStart);
            _activeBubbles.Add(bubble);
        }

        // Первое облако вместе с подписью создаётся при старте сцены. Пул
        // начинался пустым, и первый пакет локального чата создавал объект,
        // внедрял зависимости и строил подпись прямо в разборе сетевой
        // очереди: кадр провисал на ~31 мс, и камера дёргалась.
        private void PrewarmBubble()
        {
            FloatingChatBubble? bubble = GetFromPool();
            if (bubble == null)
            {
                return;
            }

            bubble.Prewarm();
            ReturnToPool(bubble);
        }

        private static void RecordIfSlow(string what, long startTimestamp)
        {
            double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            if (milliseconds >= 2.0)
            {
                Kern.Core.Interfaces.Diagnostics.FrameEventLog.Record($"{what} {milliseconds:F1} мс");
            }
        }

        private Vector3 ResolveFallbackPosition(LocalChatMessagePacket packet)
        {
            int worldHeight = _mapData != null ? _mapData.WorldHeight : 0;
            if (worldHeight <= 0)
            {
                // Мир ещё не отдал размеры: лучше пустой якорь, чем облако
                // в начале координат. ResolveTargetPosition вернёт текущую
                // позицию пузыря, а он откатится в пул по окончании жизни.
                return Vector3.zero;
            }

            return CoordinateUtils.ServerToUnityPos(packet.FallbackX, packet.FallbackY, worldHeight);
        }

        private void ExpireBubbleOf(int ownerId)
        {
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                FloatingChatBubble bubble = _activeBubbles[i];
                if (bubble != null && bubble.OwnerId == ownerId)
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
