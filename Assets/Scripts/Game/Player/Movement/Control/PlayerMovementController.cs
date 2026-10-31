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
    public class PlayerMovementController : MonoBehaviour, ILocalPlayer, IClickPathWalker
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

        // ═══ Клик-маршрут (ЛКМ): путь до цели + авто-движение по нему ═══
        // Остаток пути в серверных координатах (от следующего шага до цели)
        // и индекс следующей клетки. null - маршрут не активен.
        private List<Vector2Int>? _clickPath;
        private int _clickPathIndex;
        private ClickPathRenderer? _clickPathRenderer;

        public bool IsPathActive => _clickPath != null;

        public IReadOnlyList<Vector2Int>? Path => _clickPath;

        public int PathIndex => _clickPathIndex;

        public event Action<IReadOnlyList<Vector2Int>?>? OnPathChanged;

        public bool TryStartPath(Vector2Int target)
        {
            if (_movementLockedByProgram)
            {
                return false;
            }

            if (!HasServerPosition ||
                _storage == null || !_storage.IsReady ||
                _mapDataProvider == null)
            {
                return false;
            }

            if (!PlayerMovementValidator.IsWithinWorldBounds(
                    target, _mapDataProvider.WorldWidth, _mapDataProvider.WorldHeight))
            {
                Vector2Int delta = target - Position;
                Vector2Int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? new Vector2Int(Math.Sign(delta.x), 0)
                    : new Vector2Int(0, -Math.Sign(delta.y));
                CancelClickPath();
                TryRotate(direction);
                return false;
            }

            List<Vector2Int>? path;
            try
            {
                path = ClickPathfinder.FindPath(_storage, _mapDataProvider, Position, target);
            }
            catch (Exception)
            {
                return false;
            }

            if (path == null || path.Count == 0)
            {
                return false;
            }

            _clickPath = path;
            _clickPathIndex = 0;
            OnPathChanged?.Invoke(_clickPath);
            _clickPathRenderer?.EnsureView(_sceneObjects);
            _clickPathRenderer?.Show(_clickPath, 0, _mapDataProvider, transform.position.z);
            return true;
        }

        public void CancelPath()
        {
            CancelClickPath();
        }

        private void CancelClickPath()
        {
            if (_clickPath == null)
            {
                return;
            }

            _clickPath = null;
            _clickPathIndex = 0;
            OnPathChanged?.Invoke(null);
            _clickPathRenderer?.Hide();
        }

        private Robot? _robot;
        private IPlayerInput? _input;
        private PlayerActionDispatcher? _actionDispatcher;
        private SpriteRenderer[] _playerRenderers = Array.Empty<SpriteRenderer>();

        private bool _autoDig = false;
        private bool _ignoreCollision = false;
        private float _lastMoveTime;
        private float _lastRotateTime;
        private Direction? _lastSentDirection;
        private bool _movementValidationFailed;
        private bool _awaitingMoveConfirmation;
        [Inject]
        private IWorldDataStorage _storage = null!;

        [Inject]
        private IServerVFXService _vfx = null!;

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

        [Inject]
        private Kern.Core.Lifecycle.ISceneObjectFactory _sceneObjects = null!;

        [Inject]
        private Kern.Networking.Processing.ProgrammatorProcessor _programmator = null!;

        // Пока выполняется программа (флаг из ProgramStatePacket 43), ручной
        // ввод закрыт целиком: ни WASD, ни клик-путь, ни копание, ни хоткеи —
        // сервер их всё равно отвергнет, а клиент без гейта рисовал путь и
        // делал предсказанный шаг (G3-C). Живёт в PlayerMovementController:
        // Update() гейтит всё одной точкой выхода.
        private bool _movementLockedByProgram;

        protected void Awake()
        {
            _robot = GetComponent<Robot>();
            _clickPathRenderer = GetComponent<ClickPathRenderer>();
            // ExecuteAlways: в редакторе Awake срабатывает при открытии сцены,
            // и AddComponent записал бы компонент в сцену. Добавлять только в игре.
            if (_clickPathRenderer == null && Application.isPlaying)
            {
                _clickPathRenderer = gameObject.AddComponent<ClickPathRenderer>();
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
            _lastSentDirection = null;

            // Field injection completes during scope build, before Start runs:
            // this is the first point where publishing is guaranteed to reach
            // the application-tier state service.
            _localPlayerState?.Publish(this);
            if (_input != null)
            {
                _actionDispatcher = new PlayerActionDispatcher(_input, _networkService, _vfx);
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

            // Пока выполняется программа, ручной ввод закрыт целиком: WASD,
            // click-маршрут, копание и хоткеи уходят в один gating единственный
            // ранний выход (G3-C). TryStartPath держит собственный барьер:
            // он внешнее API, его зовёт PlayerInteractionController.
            if (_movementLockedByProgram)
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

            if (!_awaitingMoveConfirmation)
            {
                _actionDispatcher?.HandleDig(Position, _lastSentDirection ?? Direction.Down, _mapDataProvider);
            }

            _actionDispatcher?.DispatchHotkeys();
        }

        public void Initialize(uint botId)
        {
            BotId = botId;
            HasServerPosition = false;
            IsGameplayVisible = false;
            _awaitingMoveConfirmation = false;
            _lastSentDirection = null;
            _lastMoveTime = 0f;
            _lastRotateTime = 0f;
            CancelClickPath();
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
            get => _autoDig;
            set
            {
                _autoDig = value;
                OnAutoDigChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnAutoDigChanged;

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

        public void ResetServerPosition()
        {
            HasServerPosition = false;
            Position = default;
            _awaitingMoveConfirmation = false;
        }

        public void UpdateServerPosition(Vector2Int position, bool teleport = false)
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

            bool shouldSnap = !HasServerPosition ||
                Mathf.Abs(Position.x - position.x) > 1 ||
                Mathf.Abs(Position.y - position.y) > 1;
            Vector2Int oldPos = Position;
            if (shouldSnap && HasServerPosition)
            {
                // Рывок робота (а за ним и камеры): сервер прислал клетку
                // дальше соседней. Источник ищется по этой строке в логе.
                Debug.LogWarning(
                    $"[PlayerMovementController] Server moved the player {oldPos} -> {position} " +
                    $"(frame {Time.frameCount}).");
            }

            _awaitingMoveConfirmation = false;
            Position = position;
            HasServerPosition = true;
            Vector3 targetWorldPos = WorldCoordinateConverter.ServerToUnityPos(position.x, position.y, worldHeight, transform.position.z);
            transform.position = targetWorldPos;
            if (_robot is not null)
            {
                if (shouldSnap || teleport)
                {
                    _robot.SnapTo(targetWorldPos);
                    if (teleport)
                    {
                        // Телепорт: визуал робота (тело + сегменты-щупальца) снапится
                        // в точку мгновенно, иначе Robot.Update тянет transform назад
                        // к сглаженной позиции, и камера медленно "плывёт" за ним.
                        _robot.SnapVisualToTarget();
                    }
                }
                else
                {
                    _robot.TargetPosition = targetWorldPos;
                }
            }

            if (oldPos != Position)
            {
                OnPlayerMoved?.Invoke(oldPos, Position);
            }

            if (teleport)
            {
                // Респаун/ТП: маршрут до старой цели теряет смысл.
                CancelClickPath();
                OnPlayerTeleported?.Invoke();
            }
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

        public void ConfirmDigAction(ushort x, ushort y) =>
            _actionDispatcher?.ConfirmDigAction(x, y);

        public bool TryGetDigDirection(ushort x, ushort y, out Direction direction)
        {
            direction = default;
            return _actionDispatcher != null && _actionDispatcher.TryGetDigDirection(x, y, out direction);
        }

        private void ApplyMovement()
        {
            if (_robot is null || _input is null)
            {
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlockedExcludingMapMode)
            {
                return;
            }

            Vector2 moveInput = _input.MoveInput;
            if (moveInput != Vector2.zero && _clickPath != null)
            {
                CancelClickPath();
            }

            Vector2Int direction = PlayerDirectionMapping.InputToDirection(moveInput);

            // Отдельный RotatePacket по смене ввода не отправляется: сервер
            // доворачивает робота при исполнении любого действия (шаг - Rotate
            // внутри TryMove, бур - ClickCellHandler), а поворот по факту ввода
            // опережал кулдаун шага: миллисекундный тап боковой клавиши на ходу
            // поворачивал робота без шага, обратный доворот гасился
            // MinRotateInterval, и робот шёл наверх, глядя вбок, до следующего
            // шага. Направление коммитится локально ровно в момент отправки
            // шага/бура (см. _lastSentDirection ниже). Исключение - явный
            // поворот на месте: Shift гасит отправку шага, повернуться иначе
            // нельзя.
            if (direction != Vector2Int.zero && _input.IsShiftPressed && !_awaitingMoveConfirmation)
            {
                TryRotate(direction);
            }

            // Поворот к грани мира не должен ждать кулдауна шага или бура:
            // шага за границу всё равно не будет, но робот обязан смотреть
            // на видимую WorldEdge-клетку.
            if (direction != Vector2Int.zero && _mapDataProvider != null &&
                !PlayerMovementValidator.IsWithinWorldBounds(
                    Position + PlayerDirectionMapping.MovementToDeltaServer(direction),
                    _mapDataProvider.WorldWidth,
                    _mapDataProvider.WorldHeight))
            {
                TryRotate(direction);
                if (_autoDig && !_input.IsShiftPressed && !_awaitingMoveConfirmation)
                {
                    _actionDispatcher?.HandleBoundaryDig(Position, PlayerDirectionMapping.ToPacketDirection(direction));
                }

                return;
            }

            if (_awaitingMoveConfirmation)
            {
                return;
            }

            if (moveInput == Vector2.zero)
            {
                // Ручного ввода нет - ведём робота по клик-маршруту (ЛКМ).
                if (_clickPath != null)
                {
                    StepClickPath();
                }

                return;
            }

            // The authoritative dig cooldown gates movement as well as
            // repeated digging. Without this check auto-dig used the
            // current terrain cell's movement delay and could send a
            // BzPacket every movement tick, ignoring ServerConfig.
            if (_actionDispatcher is { IsDigOnCooldown: true } or { IsDigAwaitingConfirmation: true })
            {
                return;
            }

            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);

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
                        residentTargetCellType is CellType.Unloaded or CellType.Pregener))
                {
                    _lastMoveTime = Time.time;
                    _lastSentDirection = packetDirection;
                    _robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                    _awaitingMoveConfirmation = true;
                    _networkService?.SendAction(new MovePacket((ushort)targetPosition.x, (ushort)targetPosition.y));
                }

                return;
            }

            ushort targetServerX = (ushort)targetPosition.x;
            ushort targetServerY = (ushort)targetPosition.y;

            if (isPassable || _ignoreCollision)
            {
                _robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                _robot.TargetPosition = WorldCoordinateConverter.ServerToUnityPos(
                    targetServerX,
                    targetServerY,
                    mapDataProvider.WorldHeight,
                    transform.position.z);
                Vector2Int oldPos = Position;
                Position = targetPosition;
                OnPlayerMoved?.Invoke(oldPos, Position);
                _lastMoveTime = Time.time;
                _lastSentDirection = packetDirection;
                _awaitingMoveConfirmation = true;
                _networkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (_autoDig)
            {
                // Сервер доворачивает к буримой клетке сам (ClickCellHandler);
                // локальный коммит нужен визуалу и HandleDig, чтобы направление
                // соответствовало отправленному действию.
                _lastSentDirection = packetDirection;
                _robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                _actionDispatcher?.NotifyDug(targetPosition, packetDirection);
                _networkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                _lastMoveTime = Time.time;
            }
            else
            {
                // Сплошная клетка по направлению при выключенном автокопании:
                // ни шага, ни бура не будет, но повернуться к породе нужно -
                // иначе робот упорно смотрит мимо неё, если изначально был
                // развёрнут в другую сторону, и ручной бур (HandleDig идёт по
                // направлению взгляда) уходит в старую клетку. Поворот на
                // месте - тот же троттлённый путь, что Shift+направление.
                TryRotate(direction);
            }
        }

        // Поворот на месте (Shift + направление): пакет уходит сразу, не
        // ожидая кулдауна клетки, и не трогает _lastMoveTime, так что шаг в
        // новую сторону происходит в обычном ритме клетки. _lastRotateTime
        // гасит дрожь аналогового ввода у диагонали квантования (|x| == |y|),
        // чтобы смены направления туда-обратно не флудили сервером.
        private void TryRotate(Vector2Int direction)
        {
            if (_robot is null)
            {
                return;
            }

            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);
            if (_lastSentDirection == packetDirection ||
                Time.time - _lastRotateTime < MovementDefaults.MinRotateInterval)
            {
                return;
            }

            _lastRotateTime = Time.time;
            _lastSentDirection = packetDirection;
            // Предсказание угла, как при шаге движения: сервер подтвердит
            // поворот ответным снимком позиции, но визуал не должен ждать
            // его до следующего шага.
            _robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
            _networkService?.SendAction(new RotatePacket(packetDirection));
        }

        // Один тик клик-маршрута: та же механика, что у ручного движения -
        // поворот (тратит такт), затем шаг MovePacket или бур BzPacket
        // сплошной клетки. Вызывается из ApplyMovement при нулевом вводе.
        private void StepClickPath()
        {
            if (_robot is null || _clickPath is null)
            {
                return;
            }

            if (_actionDispatcher is { IsDigOnCooldown: true })
            {
                return;
            }

            // Продвигаем индекс мимо уже пройденных клеток: предсказание
            // клиента и коррекции сервера сходятся здесь.
            int previousIndex = _clickPathIndex;
            while (_clickPathIndex < _clickPath.Count && _clickPath[_clickPathIndex] == Position)
            {
                _clickPathIndex++;
            }

            if (_clickPathIndex >= _clickPath.Count)
            {
                CancelClickPath();
                return;
            }

            // Пройденные клетки гаснут: перерисовываем линию по остатку.
            if (_clickPathIndex != previousIndex)
            {
                _clickPathRenderer?.Show(_clickPath, _clickPathIndex, _mapDataProvider, transform.position.z);
            }

            Vector2Int next = _clickPath[_clickPathIndex];
            Vector2Int serverDelta = next - Position;
            if (Mathf.Abs(serverDelta.x) + Mathf.Abs(serverDelta.y) != 1)
            {
                // Робот не рядом с ожидаемой клеткой (коррекция сервера) -
                // остаток маршрута недействителен.
                CancelClickPath();
                return;
            }

            var storage = _storage;
            var mapDataProvider = _mapDataProvider;
            if (storage == null || !storage.IsReady || mapDataProvider == null)
            {
                return;
            }

            ushort currentX = (ushort)Mathf.Clamp(Position.x, 0, ushort.MaxValue);
            ushort currentServerY = (ushort)Mathf.Clamp(Position.y, 0, ushort.MaxValue);
            CellType currentCellType = storage.GetCell(currentX, currentServerY);

            float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
                mapDataProvider,
                currentCellType,
                isCtrlPressed: false,
                _ignoreCollision);
            if (cooldown > 0)
            {
                _robot.MoveSpeed = 1f / cooldown;
            }

            if (Time.time - _lastMoveTime < cooldown)
            {
                return;
            }

            // Дельта маршрута в серверных координатах -> направление в Unity.
            Vector2Int direction = new Vector2Int(serverDelta.x, -serverDelta.y);
            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);

            if (_lastSentDirection != packetDirection)
            {
                _networkService?.SendAction(new RotatePacket(packetDirection));
                _lastSentDirection = packetDirection;
                _lastMoveTime = Time.time;
                return;
            }

            _robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);

            if (!PlayerMovementValidator.TryEvaluateStep(
                    Position,
                    direction,
                    mapDataProvider,
                    storage,
                    out Vector2Int targetPosition,
                    out CellType targetCellType,
                    out bool isPassable))
            {
                // Клетка маршрута ещё не загружена - ждём данные региона.
                return;
            }

            ushort targetServerX = (ushort)targetPosition.x;
            ushort targetServerY = (ushort)targetPosition.y;

            if (isPassable || _ignoreCollision)
            {
                _robot.TargetPosition = WorldCoordinateConverter.ServerToUnityPos(targetServerX, targetServerY, mapDataProvider.WorldHeight, transform.position.z);
                Vector2Int oldPos = Position;
                Position = targetPosition;
                OnPlayerMoved?.Invoke(oldPos, Position);
                _lastMoveTime = Time.time;
                _networkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (_autoDig)
            {
                // Сплошная клетка на маршруте: бурим, как WASD при включённом
                // автокопании.
                _networkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                _lastMoveTime = Time.time;
                _actionDispatcher?.NotifyDug(targetPosition, packetDirection);
            }
            else
            {
                // Автокопание выключили посреди маршрута: вскрывать породу
                // нельзя - остаток маршрута недействителен, робот останавливается.
                CancelClickPath();
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
            float cooldown = GameplayActionDefaults.DefaultDigCooldown)
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
            Vector3 gridPos = WorldCoordinateConverter.ServerToUnityPos(Position.x, Position.y, worldHeight, transform.position.z);
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
