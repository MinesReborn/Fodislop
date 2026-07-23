using System;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.World;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts.Player
{
    /// <summary>
    /// Foundation for player movement.
    /// Currently moves the transform directly to test map loading and rendering.
    /// Structured so it can be easily extended to use Rigidbody2D by modifying the ApplyMovement method.
    /// </summary>
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

        [Header("Input Dependencies")]
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField]
        private InputActionReference _moveActionReference;

        private Vector2 _moveInput;
        private bool _autoDig = false;
        private bool _aggression = false;
        private bool _ignoreCollision = false;
        private float _lastMoveTime;
        private float _lastDigTime;
        private Direction? _lastSentDirection;

        protected void OnEnable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Enable();
            }
        }

        protected void OnDisable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Disable();
            }
        }

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
            // Align to grid center on start
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
            ReadInput();
            ApplyMovement();
            HandleDigInput();

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                AutoDig = !_autoDig;
            }

            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
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
                if (_aggression == value)
                {
                    return;
                }

                _aggression = value;
                Debug.Log($"[PlayerMovementController] Aggression set to {value}");
                OnAggressionChanged?.Invoke(value);
            }
        }

        public event Action<bool> OnAggressionChanged;

        public void ToggleAggression()
        {
            _aggression = !_aggression;
            Debug.Log($"[PlayerMovementController] Sending ToggleAgressionPacket: {_aggression}");
            NetworkService.Instance.SendAction(new ToggleAgressionPacket());
            OnAggressionChanged?.Invoke(_aggression);
        }

        public bool IsMoving => _moveInput != Vector2.zero;

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

        private void ReadInput()
        {
            // Prefer the Input Action Reference if assigned in the inspector
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveInput = _moveActionReference.action.ReadValue<Vector2>();
            }
            else
            {
                // Fallback direct polling of Keyboard for immediate testing without needing inspector setup
                _moveInput = Vector2.zero;

                if (Keyboard.current != null)
                {
                    if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                    {
                        _moveInput.y += 1f;
                    }

                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                    {
                        _moveInput.y -= 1f;
                    }

                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                    {
                        _moveInput.x -= 1f;
                    }

                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                    {
                        _moveInput.x += 1f;
                    }
                }
            }

            // Normalize to prevent faster diagonal movement
            if (_moveInput.sqrMagnitude > 1f)
            {
                _moveInput.Normalize();
            }
        }

        private void ApplyMovement()
        {
            if (_robot == null)
            {
                return;
            }

            if (PacketHandler.IsInputBlocked)
            {
                return;
            }

            if (Time.time - _lastDigTime < ServerConfig.Instance.DigCooldown)
            {
                return;
            }

            var mm = MapManager.Instance;
            var ns = NetworkService.Instance;
            if (mm == null || ns == null)
            {
                return;
            }

            if (_moveInput != Vector2.zero)
            {
                Vector2Int direction = Vector2Int.zero;
                if (Mathf.Abs(_moveInput.x) > Mathf.Abs(_moveInput.y))
                {
                    direction.x = _moveInput.x > 0 ? 1 : -1;
                }
                else
                {
                    direction.y = _moveInput.y > 0 ? 1 : -1;
                }

                if (direction != Vector2Int.zero)
                {
                    // Map direction to Data Direction
                    Direction packetDirection = direction.x switch
                    {
                        1 => Direction.Right,
                        -1 => Direction.Left,
                        _ => direction.y > 0 ? Direction.Up : Direction.Down
                    };

                    ushort currentX = (ushort)Mathf.Clamp(Position.x, 0, ushort.MaxValue);
                    ushort currentServerY = (ushort)Mathf.Clamp(Position.y, 0, ushort.MaxValue);

                    var currentCellType = MapStorage.Instance.GetCell(currentX, currentServerY);
                    float cooldown = mm.GetMoveCooldown(currentCellType);
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

                    bool isShiftPressed = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                    if (isShiftPressed)
                    {
                        return;
                    }

                    int deltaServerX = direction.x;
                    int deltaServerY = direction.y > 0 ? -1 : (direction.y < 0 ? 1 : 0);

                    int targetServerXInt = Position.x + deltaServerX;
                    int targetServerYInt = Position.y + deltaServerY;

                    // Fetch world bounds from MapStorage
                    var layer = MapStorage.Instance.CellLayer;
                    if (layer == null)
                    {
                        return;
                    }

                    int mapWidth = mm.WorldWidth;
                    int mapHeight = mm.WorldHeight;

                    // Strict boundary enforcement using clamping
                    if (targetServerXInt < 0 || targetServerXInt >= mapWidth || targetServerYInt < 0 || targetServerYInt >= mapHeight)
                    {
                        return;
                    }

                    ushort targetServerX = (ushort)targetServerXInt;
                    ushort targetServerY = (ushort)targetServerYInt;

                    var cellType = MapStorage.Instance.GetCell(targetServerX, targetServerY);
                    var cellConfig = mm.GetCellConfig(cellType);

                    bool isPassable = ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable);

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

        /// <summary>
        /// Allows injecting movement input externally (e.g. from an on-screen joystick or UI buttons)
        /// </summary>
        public void SetMovementInput(Vector2 input)
        {
            _moveInput = input;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            // Draw current grid cell
            Gizmos.color = Color.cyan;
            int worldHeight = MapManager.Instance != null ? MapManager.Instance.WorldHeight : 0;
            Vector3 gridPos = CoordinateUtils.ServerToUnityPos(Position.x, Position.y, worldHeight, transform.position.z);
            Gizmos.DrawWireCube(gridPos, new Vector3(1f, 1f, 0.1f));

            if (Application.isPlaying && _robot != null)
            {
                // Draw path to target
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, _robot.TargetPosition);
                Gizmos.DrawWireSphere(_robot.TargetPosition, 0.2f);

                FodinaeGizmos.DrawLabel(gridPos + (Vector3.down * 0.7f), $"Grid: {Position.x}, {Position.y}", Color.cyan);
            }
        }
#endif

        private void HandleDigInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (!Keyboard.current.zKey.isPressed)
            {
                return;
            }

            if (_lastSentDirection == null)
            {
                return;
            }

            if (Time.time - _lastDigTime < ServerConfig.Instance.DigCooldown)
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
    }
}
