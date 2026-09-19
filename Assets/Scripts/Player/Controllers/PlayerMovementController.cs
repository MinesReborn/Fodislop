#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Game.Managers;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Player.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Kern.Player.Logic
{
    [ExecuteAlways]
    [RequireComponent(typeof(Robot))]
    public class PlayerMovementController : MonoBehaviour, ILocalPlayer
    {
        [Header("Movement Settings")]
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;

        public uint BotID { get; private set; }
        public Vector2Int Position { get; private set; }
        public bool HasServerPosition { get; private set; }
        public bool IsGameplayVisible { get; private set; }
        public Direction LastDirection => _lastSentDirection ?? Direction.Down;
        public event Action<Vector2Int, Vector2Int>? OnPlayerMoved;

        private Robot? _robot;
        private IPlayerInput? _input;
        private PlayerActionDispatcher? _actionDispatcher;
        private SpriteRenderer[] _playerRenderers = Array.Empty<SpriteRenderer>();

        private bool _autoDig = false;
        private bool _aggression = false;
        private bool _ignoreCollision = false;
        private float _lastMoveTime;
        private Direction? _lastSentDirection;
        private bool _movementValidationFailed;
        [Inject]
        private IWorldDataStorage _storage = null!;

        [Inject]
        private INetworkService _networkService = null!;

        [Inject]
        private IMapDataProvider _mapDataProvider = null!;

        [Inject]
        private IConnectionService _connectionService = null!;

        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;

        [Inject]
        private Kern.Core.Interfaces.ILocalPlayerState _localPlayerState = null!;

        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        public void InitializeEditorPreview(IWorldDataStorage storage, IMapDataProvider mapDataProvider)
        {
            // Editor preview has no DI graph: publish only when a state service
            // was assigned explicitly by the preview harness.
            _localPlayerState?.Publish(this);
            _storage = storage;
            _mapDataProvider = mapDataProvider;
            _robot = GetComponent<Robot>();
            _playerRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (var renderer in _playerRenderers)
            {
                renderer.enabled = true;
            }

            _robot?.EnsureEditorPreviewVisual();
            UpdateServerPosition(new Vector2Int(64, 64));
            SetGameplayVisible();
        }

        protected void Awake()
        {
            _robot = GetComponent<Robot>();
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
            if (_localPlayerState != null)
            {
                _localPlayerState.Clear(this);
            }
        }

        protected void Start()
        {
            _lastSentDirection = null;

            // Field injection completes during scope build, before Start runs:
            // this is the first point where publishing is guaranteed to reach
            // the application-tier state service.
            _localPlayerState?.Publish(this);
            if (_input != null)
            {
                _actionDispatcher = new PlayerActionDispatcher(_input, _networkService, ToggleAggression);
            }
        }

        protected void Update()
        {
            // Аура считается до всех досрочных выходов ниже и гасится сама.
            // Иначе она осталась бы гореть на роботе, застывшем в кадре, где
            // открыли меню или потеряли позицию с сервера: выход из Update
            // не снимает того, что уже нарисовано.
            _actionDispatcher?.UpdateAura(
                _robot,
                HasServerPosition && (!Application.isPlaying || IsGameplayVisible),
                _inputBlocker != null && _inputBlocker.IsInputBlocked);

            if (!HasServerPosition || (Application.isPlaying && !IsGameplayVisible))
            {
                return;
            }

            if (_input == null || (_inputBlocker != null && _inputBlocker.IsInputBlocked))
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

            try
            {
                ApplyMovement();
            }
            catch (InvalidOperationException exception)
            {
                _movementValidationFailed = true;
                Debug.LogError(
                    $"[PlayerMovementController] Authoritative movement metadata is invalid: {exception.Message}");
                _connectionService?.TriggerDisconnect(exception.Message);
                return;
            }

            _actionDispatcher?.HandleDig(Position, _lastSentDirection ?? Direction.Down, _mapDataProvider);
            _actionDispatcher?.DispatchHotkeys();
        }

        public void Initialize(uint botID)
        {
            BotID = botID;
            HasServerPosition = false;
            IsGameplayVisible = false;
            _lastSentDirection = null;
            _lastMoveTime = 0f;
            _actionDispatcher?.ResetDigCooldown();
            foreach (SpriteRenderer renderer in _playerRenderers)
            {
                renderer.enabled = false;
            }

            if (_robot != null)
            {
                _robot.Initialize(botID);
            }
        }

        public bool AutoDig
        {
            get => _autoDig;
            set
            {
                _autoDig = value;
                OnAutoDigChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnAutoDigChanged;

        public bool Aggression
        {
            get => _aggression;
            set
            {
                _aggression = value;
                OnAggressionChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnAggressionChanged;

        public void ToggleAggression()
        {
            _aggression = !_aggression;
            _networkService?.SendAction(new ToggleAgressionPacket());
            OnAggressionChanged?.Invoke(_aggression);
        }
        public bool IgnoreCollision
        {
            get => _ignoreCollision;
            set
            {
                _ignoreCollision = value;
                _debugSettings.IgnoreCollision = value;
                OnCollisionChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnCollisionChanged;

        public static bool IsWithinWorldBounds(Vector2Int position, int worldWidth, int worldHeight)
        {
            return PlayerMovementValidator.IsWithinWorldBounds(position, worldWidth, worldHeight);
        }

        public void UpdateServerPosition(Vector2Int position)
        {
            if (_mapDataProvider == null)
            {
                throw new InvalidOperationException(
                    "[PlayerMovementController] IMapDataProvider is required before applying server position.");
            }

            int worldHeight = _mapDataProvider.WorldHeight;
            if (worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"[PlayerMovementController] Cannot apply server position {position}: " +
                    $"world height is {worldHeight}.");
            }

            Vector2Int oldPos = Position;
            Position = position;
            HasServerPosition = true;
            Vector3 targetWorldPos = CoordinateUtils.ServerToUnityPos(position.x, position.y, worldHeight, transform.position.z);
            transform.position = targetWorldPos;
            if (_robot is not null)
            {
                _robot.TargetPosition = targetWorldPos;
            }

            OnPlayerMoved?.Invoke(oldPos, Position);
        }

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

        private void ApplyMovement()
        {
            if (_robot is null || _input is null)
            {
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            Vector2 moveInput = _input.MoveInput;
            if (moveInput == Vector2.zero)
            {
                return;
            }

            Vector2Int direction = PlayerMovementMath.InputToDirection(moveInput);
            if (direction == Vector2Int.zero)
            {
                return;
            }

            // The authoritative dig cooldown gates movement as well as
            // repeated digging. Without this check auto-dig used the
            // current terrain cell's movement delay and could send a
            // BzPacket every movement tick, ignoring ServerConfig.
            if (_actionDispatcher is { IsDigOnCooldown: true })
            {
                return;
            }

            Direction packetDirection = PlayerMovementMath.ToPacketDirection(direction);

            ushort currentX = (ushort)Mathf.Clamp(Position.x, 0, ushort.MaxValue);
            ushort currentServerY = (ushort)Mathf.Clamp(Position.y, 0, ushort.MaxValue);

            var storage = _storage;
            if (storage == null || !storage.IsReady)
            {
                return;
            }

            // Клетка под игроком может быть ещё не получена: карту присылает
            // сервер, и он же решает, можно ли идти. Ранний выход здесь
            // оставлял клиент без MovePacket, пока не придёт регион, а
            // настоящий сервер регион без движения не шлёт.
            CellType currentCellType = storage.TryGetCell(
                currentX,
                currentServerY,
                out CellType residentCurrentCellType)
                ? residentCurrentCellType
                : CellType.Unloaded;

            var mapDataProvider = _mapDataProvider ?? throw new InvalidOperationException(
                "[PlayerMovementController] IMapDataProvider is required for movement validation.");
            float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
                mapDataProvider,
                currentCellType,
                _input.IsCtrlPressed,
                _ignoreCollision);

            if (cooldown > 0)
            {
                _robot.MoveSpeed = 1f / cooldown;
            }

            if (Time.time - _lastMoveTime < cooldown)
            {
                return;
            }

            if (_lastSentDirection != packetDirection)
            {
                _networkService?.SendAction(new RotatePacket(packetDirection));
                _lastSentDirection = packetDirection;
                _lastMoveTime = Time.time;
            }

            _robot.TargetAngle = PlayerMovementMath.DirectionToAngle(direction);

            if (_input.IsShiftPressed)
            {
                return;
            }

            if (!PlayerMovementValidator.TryEvaluateStep(
                Position,
                direction,
                mapDataProvider,
                storage,
                out Vector2Int targetPosition,
                out CellType targetCellType,
                out bool isPassable))
            {
                // Цель не загружена: предсказывать шаг не по чему, но запрос
                // уходит серверу как есть. Позицию он вернёт RobotPositionPacket,
                // и UpdateServerPosition переставит робота.
                if (storage.CellLayer != null &&
                    PlayerMovementValidator.IsWithinWorldBounds(
                        targetPosition,
                        mapDataProvider.WorldWidth,
                        mapDataProvider.WorldHeight) &&
                    (!storage.TryGetCell(
                        (ushort)targetPosition.x,
                        (ushort)targetPosition.y,
                        out CellType residentTargetCellType) ||
                        residentTargetCellType == CellType.Unloaded))
                {
                    _lastMoveTime = Time.time;
                    _networkService?.SendAction(new MovePacket((ushort)targetPosition.x, (ushort)targetPosition.y));
                }

                return;
            }

            ushort targetServerX = (ushort)targetPosition.x;
            ushort targetServerY = (ushort)targetPosition.y;

            if (isPassable || _ignoreCollision)
            {
                _robot.TargetPosition = CoordinateUtils.ServerToUnityPos(
                    targetServerX,
                    targetServerY,
                    mapDataProvider.WorldHeight,
                    transform.position.z);
                Vector2Int oldPos = Position;
                Position = targetPosition;
                OnPlayerMoved?.Invoke(oldPos, Position);
                _lastMoveTime = Time.time;
                _networkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (_autoDig)
            {
                _networkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                _lastMoveTime = Time.time;
                _actionDispatcher?.NotifyDug();
            }
        }

        public void ResetDirection()
        {
            _lastSentDirection = null;
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
            float cooldown = ProjectRuntimeContracts.Gameplay.DefaultDigCooldown)
        {
            return currentTime - lastDigTime < cooldown;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            if (_mapDataProvider == null || _mapDataProvider.WorldHeight <= 0 || !HasServerPosition)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            int worldHeight = _mapDataProvider.WorldHeight;
            Vector3 gridPos = CoordinateUtils.ServerToUnityPos(Position.x, Position.y, worldHeight, transform.position.z);
            Gizmos.DrawWireCube(gridPos, new Vector3(1f, 1f, 0.1f));

            if (Application.isPlaying && _robot != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, _robot.TargetPosition);
                Gizmos.DrawWireSphere(_robot.TargetPosition, 0.2f);
                KernGizmos.DrawLabel(gridPos + (Vector3.down * 0.7f), $"Grid: {Position.x}, {Position.y}", Color.cyan);
            }
        }
#endif
    }
}
