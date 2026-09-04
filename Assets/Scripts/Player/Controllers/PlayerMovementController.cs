#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.Player.Logic
{
    [ExecuteAlways]
    [RequireComponent(typeof(Robot))]
    public class PlayerMovementController : MonoBehaviour, ILocalPlayer
    {
        [Header("Movement Settings")]
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;

        public uint BotId { get; private set; }
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
        private float _lastDigTime;
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
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;

        [Inject]
        private Fodinae.Core.Interfaces.ILocalPlayerState _localPlayerState = null!;

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
            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }

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

            HandleDigInput();
            _actionDispatcher?.DispatchHotkeys();
        }

        public void Initialize(uint botId)
        {
            BotId = botId;
            HasServerPosition = false;
            IsGameplayVisible = false;
            _lastSentDirection = null;
            _lastMoveTime = 0f;
            _lastDigTime = 0f;
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

        public bool IsMoving => _input != null && _input.MoveInput != Vector2.zero;

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
            return worldWidth > 0 && worldHeight > 0 &&
                   position.x >= 0 && position.x < worldWidth &&
                   position.y >= 0 && position.y < worldHeight;
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
            if (IsDigCooldownActive())
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

            var currentCellType = storage.GetCell(currentX, currentServerY);
            var mapDataProvider = _mapDataProvider ?? throw new InvalidOperationException(
                "[PlayerMovementController] IMapDataProvider is required for movement validation.");
            float cooldown = mapDataProvider.GetMoveCooldown(currentCellType);
            if (_input.IsCtrlPressed)
            {
                cooldown = mapDataProvider.GetMoveCooldown(CellType.Empty);
            }

            if (_ignoreCollision)
            {
                cooldown = Mathf.Max(0.01f, cooldown / 10f);
            }

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

            Vector2Int deltaServer = PlayerMovementMath.MovementToDeltaServer(direction);
            int targetServerXInt = Position.x + deltaServer.x;
            int targetServerYInt = Position.y + deltaServer.y;

            if (storage.CellLayer == null)
            {
                return;
            }

            if (!IsWithinWorldBounds(new Vector2Int(targetServerXInt, targetServerYInt), mapDataProvider.WorldWidth, mapDataProvider.WorldHeight))
            {
                return;
            }

            ushort targetServerX = (ushort)targetServerXInt;
            ushort targetServerY = (ushort)targetServerYInt;

            var cellType = storage.GetCell(targetServerX, targetServerY);
            var cellConfig = mapDataProvider.GetCellConfig(cellType);

            bool isPassable = cellType == CellType.Empty || ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable);

            if (isPassable || _ignoreCollision)
            {
                _robot.TargetPosition = CoordinateUtils.ServerToUnityPos(targetServerX, targetServerY, mapDataProvider.WorldHeight, transform.position.z);
                Vector2Int oldPos = Position;
                Position = new Vector2Int(targetServerX, targetServerY);
                OnPlayerMoved?.Invoke(oldPos, Position);
                _lastMoveTime = Time.time;
                _networkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (_autoDig)
            {
                _networkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                _lastMoveTime = Time.time;
                _lastDigTime = Time.time;
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

        private void HandleDigInput()
        {
            if (_input == null || !_input.WantsToDig || IsDigCooldownActive())
            {
                return;
            }

            Vector2Int digOffset = PlayerMovementMath.DirectionToDigOffset(_lastSentDirection ?? Direction.Down);
            Vector2Int digTarget = Position + digOffset;
            if (_mapDataProvider == null ||
                !IsWithinWorldBounds(digTarget, _mapDataProvider.WorldWidth, _mapDataProvider.WorldHeight))
            {
                return;
            }

            ushort serverX = (ushort)digTarget.x;
            ushort serverY = (ushort)digTarget.y;

            _networkService?.Send(new ActionClientPacket(serverX, serverY, new BzPacket()));
            _lastDigTime = Time.time;
        }

        private bool IsDigCooldownActive()
        {
            return IsDigCooldownActive(Time.time, _lastDigTime, ProjectRuntimeContracts.Gameplay.DefaultDigCooldown);
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
                FodinaeGizmos.DrawLabel(gridPos + (Vector3.down * 0.7f), $"Grid: {Position.x}, {Position.y}", Color.cyan);
            }
        }
#endif
    }
}
