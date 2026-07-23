using System;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Player.Interfaces;
using Fodinae.Scripts.Player.Input;
using Fodinae.Scripts.World;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.Player.Logic
{
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField]
        private float _moveSpeed = 15f;

        public uint BotId { get; private set; }
        public Vector2Int Position { get; private set; }
        public Direction LastDirection => _lastSentDirection ?? Direction.Up;
        public event Action<Vector2Int, Vector2Int> OnPlayerMoved;

        private Robot _robot;
        private IPlayerInput _input;

        private bool _autoDig = false;
        private bool _aggression = false;
        private bool _ignoreCollision = false;
        private float _lastMoveTime;
        private float _lastDigTime;
        private Direction? _lastSentDirection;

        public static PlayerMovementController LocalPlayer { get; private set; }
        public static event Action<PlayerMovementController> OnLocalPlayerSpawned;

        protected void Awake()
        {
            LocalPlayer = this;
            OnLocalPlayerSpawned?.Invoke(this);

            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }

            _robot = GetComponent<Robot>();
            if (_robot != null)
            {
                _robot.MoveSpeed = _moveSpeed;
            }

            _input = GetComponent<IPlayerInput>();
            if (_input == null)
            {
                _input = gameObject.AddComponent<PlayerInputHandler>();
            }
        }

        protected void OnDestroy()
        {
            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }
        }

        protected void Start()
        {
            Vector3 targetGridPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z);
            transform.position = targetGridPos;
            if (_robot != null)
            {
                _robot.TargetPosition = targetGridPos;
            }

            Position = CoordinateUtils.UnityToServerPos(transform.position, MapManager.Instance != null ? MapManager.Instance.WorldHeight : 0);
        }

        protected void Update()
        {
            if (_input == null) return;

            ApplyMovement();
            HandleDigInput();

            if (_input.WantsToToggleAutoDig)
            {
                AutoDig = !_autoDig;
            }

            if (_input.WantsToToggleAggression)
            {
                ToggleAggression();
            }
        }

        public void Initialize(uint botId)
        {
            BotId = botId;
        }

        public bool AutoDig
        {
            get => _autoDig;
            set
            {
                _autoDig = value;
                NetworkService.Instance.SendAction(new ToggleAutoDigPacket());
                OnAutoDigChanged?.Invoke(value);
            }
        }

        public event Action<bool> OnAutoDigChanged;

        public bool Aggression
        {
            get => _aggression;
            set
            {
                if (_aggression == value) return;
                _aggression = value;
                OnAggressionChanged?.Invoke(value);
            }
        }

        public event Action<bool> OnAggressionChanged;

        public void ToggleAggression()
        {
            _aggression = !_aggression;
            NetworkService.Instance.SendAction(new ToggleAgressionPacket());
            OnAggressionChanged?.Invoke(_aggression);
        }

        public bool IsMoving => _input != null && _input.MoveInput != Vector2.zero;

        public bool IgnoreCollision
        {
            get => _ignoreCollision;
            set
            {
                _ignoreCollision = value;
                DummyConnection.IgnoreCollision = value;
            }
        }

        public void UpdateServerPosition(Vector2Int position)
        {
            Vector2Int oldPos = Position;
            Position = position;

            var mm = MapManager.Instance;
            if (mm != null)
            {
                Vector3 targetWorldPos = CoordinateUtils.ServerToUnityPos(position.x, position.y, mm.WorldHeight, transform.position.z);
                transform.position = targetWorldPos;
                if (_robot != null)
                {
                    _robot.TargetPosition = targetWorldPos;
                }
            }

            OnPlayerMoved?.Invoke(oldPos, Position);
        }

        private void ApplyMovement()
        {
            if (_robot == null || PacketHandler.IsInputBlocked) return;
            if (Time.time - _lastDigTime < ServerConfig.Instance.DigCooldown) return;

            var mm = MapManager.Instance;
            var ns = NetworkService.Instance;
            if (mm == null || ns == null) return;

            Vector2 moveInput = _input.MoveInput;
            if (moveInput != Vector2.zero)
            {
                Vector2Int direction = Vector2Int.zero;
                if (Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y))
                {
                    direction.x = moveInput.x > 0 ? 1 : -1;
                }
                else
                {
                    direction.y = moveInput.y > 0 ? 1 : -1;
                }

                if (direction != Vector2Int.zero)
                {
                    Direction packetDirection = direction.x switch
                    {
                        1 => Direction.Right,
                        -1 => Direction.Left,
                        _ => direction.y > 0 ? Direction.Up : Direction.Down
                    };

                    ushort currentX = (ushort)Mathf.Clamp(Position.x, 0, ushort.MaxValue);
                    ushort currentServerY = (ushort)Mathf.Clamp(Position.y, 0, ushort.MaxValue);

                    var currentCellType = ServiceLocator.Resolve<IWorldDataStorage>().GetCell(currentX, currentServerY);
                    float cooldown = mm.GetMoveCooldown(currentCellType);
                    if (cooldown > 0)
                    {
                        _robot.MoveSpeed = 1f / cooldown;
                    }

                    if (Time.time - _lastMoveTime < cooldown) return;

                    if (_lastSentDirection != packetDirection)
                    {
                        ns.SendAction(new RotatePacket(packetDirection));
                        _lastSentDirection = packetDirection;
                        _lastMoveTime = Time.time;
                    }

                    if (direction.x != 0)
                    {
                        _robot.TargetAngle = direction.x > 0 ? 0f : 180f;
                    }
                    else
                    {
                        _robot.TargetAngle = direction.y > 0 ? 90f : 270f;
                    }

                    if (_input.IsShiftPressed) return;

                    int deltaServerX = direction.x;
                    int deltaServerY = direction.y > 0 ? -1 : (direction.y < 0 ? 1 : 0);

                    int targetServerXInt = Position.x + deltaServerX;
                    int targetServerYInt = Position.y + deltaServerY;

                    var layer = ServiceLocator.Resolve<IWorldDataStorage>().CellLayer;
                    if (layer == null) return;

                    int mapWidth = mm.WorldWidth;
                    int mapHeight = mm.WorldHeight;

                    if (targetServerXInt < 0 || targetServerXInt >= mapWidth || targetServerYInt < 0 || targetServerYInt >= mapHeight)
                    {
                        return;
                    }

                    ushort targetServerX = (ushort)targetServerXInt;
                    ushort targetServerY = (ushort)targetServerYInt;

                    var cellType = ServiceLocator.Resolve<IWorldDataStorage>().GetCell(targetServerX, targetServerY);
                    var cellConfig = mm.GetCellConfig(cellType);

                    bool isPassable = cellType == CellType.Empty || ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable);

                    if (isPassable || _ignoreCollision)
                    {
                        _robot.TargetPosition = CoordinateUtils.ServerToUnityPos(targetServerX, targetServerY, mm.WorldHeight, transform.position.z);
                        Vector2Int oldPos = Position;
                        Position = new Vector2Int(targetServerX, targetServerY);
                        OnPlayerMoved?.Invoke(oldPos, Position);
                        _lastMoveTime = Time.time;
                        ns.SendAction(new MovePacket(targetServerX, targetServerY));
                    }
                    else if (_autoDig)
                    {
                        NetworkService.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                        _lastMoveTime = Time.time;
                        _lastDigTime = Time.time;
                    }
                }
            }
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
            if (!_input.WantsToDig || _lastSentDirection == null || Time.time - _lastDigTime < ServerConfig.Instance.DigCooldown)
            {
                return;
            }

            Vector2Int digOffset = _lastSentDirection.Value switch
            {
                Direction.Down => new Vector2Int(0, 1),
                Direction.Up => new Vector2Int(0, -1),
                Direction.Left => new Vector2Int(-1, 0),
                Direction.Right => new Vector2Int(1, 0),
                _ => Vector2Int.zero
            };

            ushort serverX = (ushort)(Position.x + digOffset.x);
            ushort serverY = (ushort)(Position.y + digOffset.y);

            NetworkService.Send(new ActionClientPacket(serverX, serverY, new BzPacket()));
            _lastDigTime = Time.time;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            int worldHeight = MapManager.Instance != null ? MapManager.Instance.WorldHeight : 0;
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
