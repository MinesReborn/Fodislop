using Fodinae.Assets.Scripts.Game;
using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Networking.Connection;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Assets.Scripts.Player
{
    /// <summary>
    /// Foundation for player movement.
    /// Currently moves the transform directly to test map loading and rendering.
    /// Structured so it can be easily extended to use Rigidbody2D by modifying the ApplyMovement method.
    /// </summary>
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 15f;
        
        public uint BotId { get; private set; }
        public Vector2Int ServerPosition { get; private set; }
        public Vector2Int ClientPosition { get; private set; }

        private Robot _robot;

        [Header("Input Dependencies")]
        [Tooltip("Optional: Drag the Move action from the Input Action asset here. If empty, falls back to direct keyboard polling.")]
        [SerializeField] private InputActionReference _moveActionReference;

        private Vector2 _moveInput;
        private bool _isMoving = false;
        private float _lastMoveTime;
        private Direction? _lastSentDirection;

        private void OnEnable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Disable();
            }
        }

        private void Awake()
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }
            _robot = GetComponent<Robot>();
            if (_robot != null)
            {
                _robot.MoveSpeed = _moveSpeed;
            }
        }

        private void Start()
        {
            // Align to grid center on start
            Vector3 targetGridPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z
            );
            transform.position = targetGridPos;
            if (_robot != null)
            {
                _robot.TargetPosition = targetGridPos;
            }
            ClientPosition = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
        }

        private void Update()
        {
            ReadInput();
            ApplyMovement();
        }

        public void Initialize(uint botId)
        {
            BotId = botId;
        }

        public void UpdateServerPosition(Vector2Int position)
        {
            ServerPosition = position;
            // Reconcile ClientPosition with ServerPosition (converted to Unity grid coordinates)
            ClientPosition = new Vector2Int(position.x, MapManager.Instance.WorldHeight - 1 - position.y);
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
                    if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) _moveInput.y += 1f;
                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) _moveInput.y -= 1f;
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) _moveInput.x -= 1f;
                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) _moveInput.x += 1f;
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
            if (_robot == null) return;

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

                    int currentUnityX = ClientPosition.x;
                    int currentUnityY = ClientPosition.y;

                    ushort currentX = (ushort)Mathf.Clamp(currentUnityX, 0, ushort.MaxValue);
                    ushort currentServerY = (ushort)Mathf.Clamp(MapManager.Instance.WorldHeight - 1 - currentUnityY, 0, ushort.MaxValue);

                    var currentCellType = MapStorage.Instance.GetCell(currentX, currentServerY);
                    float cooldown = MapManager.Instance.GetMoveCooldown(currentCellType);
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
                        ConnectionManager.Instance.SendPacket(new ActionClientPacket(currentX, currentServerY, new RotatePacket(packetDirection)));
                        _lastSentDirection = packetDirection;
                        _lastMoveTime = Time.time;
                    }

                    if (direction.x != 0)
                        _robot.TargetAngle = direction.x > 0 ? 0f : 180f;
                    else
                        _robot.TargetAngle = direction.y > 0 ? 90f : 270f;

                    bool isShiftPressed = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                    if (isShiftPressed)
                    {
                        return;
                    }

                    // Y axis in Unity increases upwards. 
                    // Data Y usually increases downwards (0 at top).
                    // If the user says Y increases when going up, this is inverted relative to standard screen space.
                    // We will keep Y change relative to Unity transform (positive is up) and clamp against map dimensions.
                    int targetUnityX = currentUnityX + direction.x;
                    int targetUnityY = currentUnityY + direction.y; // Match Unity's movement

                    // Fetch world bounds from MapStorage
                    var layer = MapStorage.Instance.cellLayer;
                    if (layer == null) return;

                    int mapWidth = MapManager.Instance.WorldWidth;
                    int mapHeight = MapManager.Instance.WorldHeight;

                    // Strict boundary enforcement using clamping
                    if (targetUnityX < 0 || targetUnityX >= mapWidth || targetUnityY < 0 || targetUnityY >= mapHeight)
                    {
                        return;
                    }

                    ushort targetServerX = (ushort)targetUnityX;
                    ushort targetServerY = (ushort)(MapManager.Instance.WorldHeight - 1 - targetUnityY);

                    var cellType = MapStorage.Instance.GetCell(targetServerX, targetServerY);
                    var cellConfig = MapManager.Instance.GetCellConfig(cellType);

                    bool isPassable = ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable);

                    if (isPassable)
                    {
                        // Movement animation in Unity (Y is positive going up)
                        // Use absolute grid coordinates to ensure alignment
                        _robot.TargetPosition = new Vector3(targetUnityX + 0.5f, targetUnityY + 0.5f, transform.position.z);
                        ClientPosition = new Vector2Int(targetUnityX, targetUnityY);
                        _lastMoveTime = Time.time;
                        ConnectionManager.Instance.SendPacket(new ActionClientPacket(currentX, currentServerY, new MovePacket(targetServerX, targetServerY)));
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
    }
}
