#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Player.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Programmator;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

using Kern.Game.Entities.Robots;
using Kern.Game.Player.Actions;
using Kern.Game.Player.Pathfinding;
using Kern.Networking.Processing;

using Kern.Diagnostics.World;

namespace Kern.Game.Player.Movement
{
    [ExecuteAlways]
    [RequireComponent(typeof(Robot))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Player.Logic", sourceAssembly: null, sourceClassName: "PlayerMovementController")]
    public class PlayerMovementController : MonoBehaviour, ILocalPlayer, IClickPathWalker, IPlayerMovementActionContext
    {
        [Header("Movement Settings")]
        [SerializeField]
        private float _moveSpeed = MovementDefaults.RobotMoveSpeed;

        public uint BotId { get; private set; }
        public Vector2Int Position { get; private set; }
        public bool HasServerPosition { get; private set; }
        public bool IsGameplayVisible { get; private set; }
        public Direction LastDirection => _lastSentDirection ?? Direction.Down;
        public event Action<Vector2Int, Vector2Int>? OnPlayerMoved;

        // Телепорт по команде сервера (респаун, ТП-свиток, админ-перенос):
        // камера подписывается и щёлкает на новое место мгновенно.
        public event Action? OnPlayerTeleported;

        private readonly PlayerClickPathState _clickPathState = new();

        public bool IsPathActive => _clickPathState.Path != null;

        public IReadOnlyList<Vector2Int>? Path => _clickPathState.Path;

        public int PathIndex => _clickPathState.Index;

        public event Action<IReadOnlyList<Vector2Int>?>? OnPathChanged
        {
            add => _clickPathState.PathChanged += value;
            remove => _clickPathState.PathChanged -= value;
        }

        public bool TryStartPath(Vector2Int target) => _clickPathState.TryStart(
            target,
            _movementLockedByProgram,
            HasServerPosition,
            _storage,
            _mapDataProvider,
            Position,
            CancelClickPath,
            TryRotate,
            _sceneObjects,
            transform);

        public void CancelPath() => CancelClickPath();

        private void CancelClickPath() => _clickPathState.Cancel();

        private Robot? _robot;
        private IPlayerInput? _input;
        private PlayerActionDispatcher? _actionDispatcher;
        private SpriteRenderer[] _playerRenderers = Array.Empty<SpriteRenderer>();

        private readonly PlayerMovementOptions _movementOptions = new();
        private readonly PlayerStepSelectorState _stepState = new();
        private float _nextMoveTime;
        private float _lastRotateTime;
        private Direction? _lastSentDirection;
        private Direction _lastServerRotation = Direction.Down;
        private bool _movementValidationFailed;
        private bool _awaitingMoveConfirmation;
        [Inject]
        private IWorldDataStorage _storage = null!;

        [Inject]
        private IServerVisualEffectService _visualEffect = null!;

        [Inject]
        private IServerAudioService _audio = null!;

        [Inject]
        private INetworkService _networkService = null!;

        [Inject]
        private IMapDataProvider _mapDataProvider = null!;

        [Inject]
        private IWorldPresentationState _presentationState = null!;

        [Inject]
        private IConnectionService _connectionService = null!;

        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;

        [Inject]
        private Kern.Core.Interfaces.ILocalPlayerState _localPlayerState = null!;

        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        [Inject]
        private Kern.Core.Interfaces.IClientConfigManager _clientConfig = null!;

        [Inject]
        private Kern.Core.Lifecycle.ISceneObjectFactory _sceneObjects = null!;

        [Inject]
        private Kern.Networking.Processing.ProgrammatorProcessor _programmator = null!;

        // Пока выполняется программа (и не HandMode), ручное движение
        // закрыто: ни WASD, ни клик-путь — сервер их всё равно отвергнет,
        // а клиент без гейта рисовал путь и делал предсказанный шаг.
        private bool _movementLockedByProgram;
        private readonly PlayerMovementActionExecutor _movementExecutor = new();
        private readonly PlayerServerPositionUpdater _serverPositionUpdater = new();

        protected void Awake()
        {
            _robot = GetComponent<Robot>();
            _clickPathState.BindRenderer(GetComponent<ClickPathRenderer>());
            // ExecuteAlways: в редакторе Awake срабатывает при открытии сцены,
            // и AddComponent записал бы компонент в сцену. Добавлять только в игре.
            if (_clickPathState.Renderer == null && Application.isPlaying)
            {
                _clickPathState.BindRenderer(gameObject.AddComponent<ClickPathRenderer>());
            }

            if (_robot is not null)
            {
                _robot.MoveSpeed = _moveSpeed;
            }

            _playerRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            if (Application.isPlaying)
            {
                foreach (SpriteRenderer renderer in _playerRenderers)
                {
                    renderer.enabled = false;
                }
            }

            _input = GetComponent<IPlayerInput>() ??
                throw new InvalidOperationException(
                    "PlayerMovementController requires an IPlayerInput component on the player prefab.");
        }

        protected void OnDestroy()
        {
            if (_programmator != null)
            {
                _programmator.StateChanged -= OnProgramStateChanged;
            }

            if (_localPlayerState != null)
            {
                _localPlayerState.Clear(this);
            }
        }

        protected void Start()
        {
            // ExecuteAlways: в режиме редактирования DI-поля не заполнены,
            // а сборка игрока загружает сцену и вызывает Start.
            if (!Application.isPlaying)
            {
                return;
            }

            if (_presentationState == null)
            {
                throw new InvalidOperationException(
                    "PlayerMovementController requires IWorldPresentationState before Start.");
            }

            _lastSentDirection = null;
            _movementOptions.BindDebugSettings(_debugSettings);

            // Field injection completes during scope build, before Start runs:
            // this is the first point where publishing is guaranteed to reach
            // the application-tier state service.
            _localPlayerState?.Publish(this);
            if (_input != null)
            {
                _actionDispatcher = new PlayerActionDispatcher(_input, _networkService, _audio, _visualEffect);
            }
            _programmator.StateChanged += OnProgramStateChanged;
        }

        private void OnProgramStateChanged(
            MinesServer.Networking.Server.Packets.Programmator.ProgramStatePacket packet)
        {
            bool locked = packet.State != ProgramState.Stopped && !packet.IsMovementAllowed;
            if (_movementLockedByProgram == locked)
            {
                return;
            }

            _movementLockedByProgram = locked;
            if (locked)
            {
                CancelPath();
            }
        }

        protected void Update()
        {
            // Удержание хила считается до всех досрочных выходов ниже.
            // При открытии меню или потере серверной позиции эффект должен
            // выключаться, даже если дальнейшая обработка ввода не выполняется.
            _actionDispatcher?.UpdateHealEffect(
                _robot,
                HasServerPosition && (!Application.isPlaying || IsGameplayVisible),
                _inputBlocker != null && _inputBlocker.IsInputBlockedExcludingMapMode);

            if (_presentationState == null || !_presentationState.IsStable)
            {
                return;
            }

            if (!HasServerPosition || (Application.isPlaying && !IsGameplayVisible))
            {
                return;
            }

            // Тот же принцип, что и во вводе: открытая карта мира не останавливает
            // робота — движение и действия работают поверх карты.
            if (_input == null || (_inputBlocker != null && _inputBlocker.IsInputBlockedExcludingMapMode))
            {
                return;
            }

            // The player object can exist during the connection/world-init gap.
            // Input is intentionally ignored until the authoritative map layer
            // is ready; movement validation must never probe an uninitialized map.
            if (_storage == null || !_storage.IsReady)
            {
                return;
            }

            if (_movementValidationFailed)
            {
                return;
            }

            // Block all manual gameplay input together: movement, digging and hotkeys.
            // TryStartPath keeps its own guard because callers can invoke that public API directly.
            if (_movementLockedByProgram)
            {
                return;
            }

            try
            {
                _movementExecutor.ApplyMovement(this);
            }
            catch (InvalidOperationException exception)
            {
                _movementValidationFailed = true;
                Debug.LogError(
                    $"[PlayerMovementController] Authoritative movement metadata is invalid: {exception.Message}");
                _connectionService?.TriggerDisconnect(exception.Message);
                return;
            }

            if (!_awaitingMoveConfirmation)
            {
                _actionDispatcher?.HandleDig(Position, _lastSentDirection ?? Direction.Down, _mapDataProvider);
            }

            _actionDispatcher?.DispatchHotkeys();
        }

        private void TryRotate(Vector2Int direction) => _movementExecutor.TryRotate(this, direction);

        Robot? IPlayerMovementActionContext.Robot => _robot;
        IPlayerInput? IPlayerMovementActionContext.Input => _input;
        Kern.Core.Interfaces.IInputBlocker IPlayerMovementActionContext.InputBlocker => _inputBlocker;
        IReadOnlyList<Vector2Int>? IPlayerMovementActionContext.ClickPath => _clickPathState.Path;
        int IPlayerMovementActionContext.ClickPathIndex
        {
            get => _clickPathState.Index;
            set => _clickPathState.Index = value;
        }
        ClickPathRenderer? IPlayerMovementActionContext.ClickPathRenderer => _clickPathState.Renderer;
        IMapDataProvider IPlayerMovementActionContext.MapDataProvider => _mapDataProvider;
        IWorldDataStorage IPlayerMovementActionContext.Storage => _storage;
        bool IPlayerMovementActionContext.AwaitingMoveConfirmation
        {
            get => _awaitingMoveConfirmation;
            set => _awaitingMoveConfirmation = value;
        }
        PlayerActionDispatcher? IPlayerMovementActionContext.ActionDispatcher => _actionDispatcher;
        bool IPlayerMovementActionContext.AutoDig => _movementOptions.AutoDig;
        bool IPlayerMovementActionContext.IgnoreCollision => _movementOptions.IgnoreCollision;
        // Живой конфиг: тумблер на вкладке «Управление» действует сразу.
        bool IPlayerMovementActionContext.MovementAssist => _clientConfig.Config.Interface.MovementAssist;
        PlayerStepSelectorState IPlayerMovementActionContext.StepState => _stepState;
        float IPlayerMovementActionContext.NextMoveTime
        {
            get => _nextMoveTime;
            set => _nextMoveTime = value;
        }
        Direction? IPlayerMovementActionContext.LastSentDirection
        {
            get => _lastSentDirection;
            set => _lastSentDirection = value;
        }
        Direction IPlayerMovementActionContext.LastServerRotation
        {
            get => _lastServerRotation;
            set => _lastServerRotation = value;
        }
        float IPlayerMovementActionContext.LastRotateTime
        {
            get => _lastRotateTime;
            set => _lastRotateTime = value;
        }
        INetworkService IPlayerMovementActionContext.NetworkService => _networkService;
        Transform IPlayerMovementActionContext.OwnerTransform => transform;
        bool IPlayerMovementActionContext.HasServerPosition
        {
            get => HasServerPosition;
            set => HasServerPosition = value;
        }
        bool IPlayerMovementActionContext.IsGameplayVisible
        {
            get => IsGameplayVisible;
            set => IsGameplayVisible = value;
        }
        SpriteRenderer[] IPlayerMovementActionContext.PlayerRenderers => _playerRenderers;
        IWorldPresentationState IPlayerMovementActionContext.PresentationState => _presentationState;
        void IPlayerMovementActionContext.PublishPlayerTeleported() => OnPlayerTeleported?.Invoke();
        Vector2Int IPlayerMovementActionContext.Position
        {
            get => Position;
            set => Position = value;
        }
        void IPlayerMovementActionContext.CancelClickPath() => CancelClickPath();
        void IPlayerMovementActionContext.PublishPlayerMoved(Vector2Int oldPosition, Vector2Int newPosition) =>
            OnPlayerMoved?.Invoke(oldPosition, newPosition);

        public void Initialize(uint botId)
        {
            BotId = botId;
            HasServerPosition = false;
            IsGameplayVisible = false;
            _awaitingMoveConfirmation = false;
            _lastSentDirection = null;
            _lastServerRotation = Direction.Down;
            _nextMoveTime = 0f;
            _lastRotateTime = 0f;
            CancelClickPath();
            PlayerStepSelector.Reset(_stepState);
            _actionDispatcher?.ResetDigCooldown();
            foreach (SpriteRenderer renderer in _playerRenderers)
            {
                renderer.enabled = false;
            }

            if (_robot != null)
            {
                _robot.Initialize(botId);
            }
        }

        public bool AutoDig
        {
            get => _movementOptions.AutoDig;
            set => _movementOptions.AutoDig = value;
        }

        public event Action<bool>? OnAutoDigChanged
        {
            add => _movementOptions.AutoDigChanged += value;
            remove => _movementOptions.AutoDigChanged -= value;
        }

        public bool IgnoreCollision
        {
            get => _movementOptions.IgnoreCollision;
            set => _movementOptions.IgnoreCollision = value;
        }

        public event Action<bool>? OnCollisionChanged
        {
            add => _movementOptions.CollisionChanged += value;
            remove => _movementOptions.CollisionChanged -= value;
        }

        public static bool IsWithinWorldBounds(Vector2Int position, int worldWidth, int worldHeight) =>
            PlayerMovementRules.IsWithinWorldBounds(position, worldWidth, worldHeight);

        public void ResetServerPosition()
        {
            HasServerPosition = false;
            Position = default;
            _awaitingMoveConfirmation = false;
            IsGameplayVisible = false;
            CancelClickPath();
            PlayerStepSelector.Reset(_stepState);
            ResetDirection();
            foreach (SpriteRenderer renderer in _playerRenderers)
            {
                renderer.enabled = false;
            }

            _robot?.SetBatchedBodyVisible(false);
        }

        public void UpdateServerPosition(Vector2Int position, bool teleport = false, Direction? rotation = null) =>
            _serverPositionUpdater.UpdateServerPosition(this, position, teleport, rotation);

        public void SetGameplayVisible()
        {
            if (!HasServerPosition)
            {
                throw new InvalidOperationException(
                    "[PlayerMovementController] Cannot show player before server position is synchronized.");
            }

            if (IsGameplayVisible)
            {
                return;
            }

            IsGameplayVisible = true;
            foreach (SpriteRenderer renderer in _playerRenderers)
            {
                renderer.enabled = true;
            }

            _robot?.SetBatchedBodyVisible(true);
        }

        public void ConfirmDigAction(ushort x, ushort y) =>
            _actionDispatcher?.ConfirmDigAction(x, y);

        public bool TryGetDigDirection(ushort x, ushort y, out Direction direction)
        {
            direction = default;
            return _actionDispatcher != null && _actionDispatcher.TryGetDigDirection(x, y, out direction);
        }

        public void ResetDirection()
        {
            _lastSentDirection = null;
            _lastServerRotation = Direction.Down;
        }

        public void SetMovementInput(Vector2 input)
        {
            if (_input != null)
            {
                _input.SetMovementInput(input);
            }
        }

        public static bool IsDigCooldownActive(
            float currentTime,
            float lastDigTime,
            float cooldown = GameplayActionDefaults.DefaultDigCooldown) =>
            PlayerMovementRules.IsDigCooldownActive(currentTime, lastDigTime, cooldown);

#if UNITY_EDITOR
        protected void OnDrawGizmos() => PlayerMovementGizmoRenderer.Draw(
            transform,
            _mapDataProvider,
            HasServerPosition,
            Position,
            _robot);
#endif
    }
}
