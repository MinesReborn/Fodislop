#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine;
using VContainer;

using Kern.Game.Events.Playback;
using Kern.Game.Player.Movement;
using Kern.Game.Rendering;

namespace Kern.Game.Entities.Robots
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Game", sourceAssembly: null, sourceClassName: "Robot")]
    public class Robot : MonoBehaviour, IRobotView
    {
        private const string TAG = "[Robot]";

        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("_botID")]
        private uint _botId;
        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("_playerID")]
        private int _playerId;
        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("_clanID")]
        private byte _clanId;
        [SerializeField]
        private SpriteRenderer? _spriteRenderer;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ServerEventPlaybackManager _eventPlayback = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IWorldPresentationState _presentationState = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [SerializeField]
        private string _nickname = string.Empty;
        [SerializeField]
        private string _skinPath = string.Empty;
        [SerializeField]
        private string _tailPath = string.Empty;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private bool _isMetadataLoaded;
        private bool _visualsLoadCompleted;
        private RobotAssetLoader? _assetLoaderHelper;
        private bool _watchingAssets;
        private RobotAssetLoader AssetLoader => _assetLoaderHelper ??= new RobotAssetLoader(_assetLoader, _operations, this.GetCancellationTokenOnDestroy());
        [SerializeField]
        private float _moveSpeed = MovementDefaults.RobotMoveSpeed;

        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        private RobotLighting _lighting = null!;
        private RobotVisuals _visuals = null!;
        private readonly RobotNameplate _nameplate = new();

        [Inject]
        private IWorldLabels _worldLabels = null!;
        private readonly RobotMovement _movement = new();

        private bool _visualElementsInitialized;
        private bool _hasPendingServerPosition;
        private ushort _pendingServerX = 0;
        private ushort _pendingServerY = 0;
        private readonly RobotCuller _culler = new();
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private RobotManager _robotManager = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        public uint BotId => _botId;
        public int PlayerId => _playerId;
        public byte ClanId => _clanId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;
        public bool IsVisualsLoaded => _isMetadataLoaded && _visualsLoadCompleted;
        public bool IsLocalPlayer => gameObject.CompareTag("Player");

        public float DynamicLightIntensity => _lighting.DynamicLightIntensity;
        public Color DynamicLightColor => _lighting.DynamicLightColor;

        [Inject]
        private void InitializeEntityBatch(WorldEntityBatchRenderer entityBatchRenderer)
        {
            _lighting ??= new RobotLighting();
            _visuals ??= new RobotVisuals(transform, IsLocalPlayer);
            _entityBatchRenderer = entityBatchRenderer;
            InitializeVisualElements();
            _visuals.Initialize(entityBatchRenderer, _visuals.ClanTransform, _sceneObjects);
        }

        private void EnsureVisuals()
        {
            _lighting ??= new RobotLighting();
            _visuals ??= new RobotVisuals(transform, IsLocalPlayer);
        }

        [Inject]
        private void SubscribeToPresentationState(IWorldPresentationState presentationState)
        {
            if (_presentationState != null)
            {
                _presentationState.TeleportCommitting -= HandleTeleportCommitting;
            }

            _presentationState = presentationState;
            _presentationState.TeleportCommitting -= HandleTeleportCommitting;
            _presentationState.TeleportCommitting += HandleTeleportCommitting;
        }

        public float LogicalFacingAngle => _movement.TargetAngle;

        // Точка, за которой следует камера: уже сглаженная позиция без дрожи.
        // transform.position для этого не годится — в него подмешан случайный
        // тремор, и туда же PlayerMovementController ставит сырую клетку.
        public Vector3 CameraAnchor => _movement.SmoothPosition;

        public bool TryGetServerPosition(out Vector3 position)
        {
            position = _movement.ServerPosition;
            return _movement.HasReceivedInitialPosition;
        }

        public float TargetAngle
        {
            get => _movement.TargetAngle - VISUAL_ROTATION_OFFSET;
            set => _movement.TargetAngle = value + VISUAL_ROTATION_OFFSET;
        }

        public Vector3 TargetPosition
        {
            get => _movement.TargetPosition;
            set => _movement.TargetPosition = value;
        }

        public void SnapTo(Vector3 position, float? angle = null)
        {
            transform.position = position;
            _movement.SnapTo(position, angle ?? transform.eulerAngles.z);
            _visuals.SnapTentacles(position);
        }

        /// <summary>
        /// Последняя серверная (логическая) позиция робота в мировых координатах:
        /// приходит пакетами позиции и не зависит от интерполяции визуала.
        /// Нужна эффектам, привязанным к серверным событиям (луч C-190 и т.п.).
        /// </summary>
        public Vector3 ServerPosition => _movement.ServerPosition;

        /// <summary>Приходила ли хотя бы одна серверная позиция (иначе ServerPosition не осмысленна).</summary>
        public bool HasServerPosition => _movement.HasReceivedInitialPosition;

        public float MoveSpeed
        {
            get => _moveSpeed;
            set
            {
                _moveSpeed = value;
            }
        }

        protected void Awake()
        {
            EnsureVisuals();

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (_spriteRenderer != null && Application.isPlaying)
            {
                _spriteRenderer.enabled = false;
            }

            transform.localScale = Vector3.one;
            _movement.InitializePose(transform.position, transform.eulerAngles.z);
        }

        protected void OnEnable()
        {
            _nameplate.SetEnabled(!IsLocalPlayer);
            _nameplate.InvalidatePosition();
            if (!Application.isPlaying ||
                (IsLocalPlayer ?
                    _localPlayer != null && _localPlayer.Current is { HasServerPosition: true } :
                    _isMetadataLoaded && _movement.HasReceivedInitialPosition))
            {
                _visuals.SetTentaclesActive(true);
            }
            else
            {
                _visuals.SetTentaclesActive(false);
            }
        }

        protected void OnDisable()
        {
            if (IsLocalPlayer && _eventPlayback != null)
            {
                _eventPlayback.SetLocalHealHeld(false);
            }

            _nameplate.SetEnabled(false);
            _lighting.Remove(_lightingEngine);
            _visuals.SetTentaclesActive(false);
        }

        private void InitializeVisualElements()
        {
            if (_visualElementsInitialized)
            {
                return;
            }

            _visualElementsInitialized = true;
            _nameplate.Initialize(transform, _botId, _nickname, IsLocalPlayer, _sceneObjects, _worldLabels);
            _visuals.EnsureClanIcon(_sceneObjects, _botId);
        }

        public void SetBatchedBodyVisible(bool visible) => _visuals.SetBodyVisible(visible);

        public void SetHealHeld(bool held) => _eventPlayback.SetLocalHealHeld(held);


        protected void Start()
        {
            TryInitializeDynamicLightSettings();

            // A packet may arrive before Start. Never replace its position or
            // mark a scene pose as an already received server coordinate.
            if (!_movement.HasReceivedInitialPosition)
            {
                Vector3 snappedPos = new Vector3(
                    Mathf.Floor(transform.position.x) + 0.5f,
                    Mathf.Floor(transform.position.y) + 0.5f,
                    transform.position.z);
                transform.position = snappedPos;
                _movement.InitializePose(snappedPos, transform.eulerAngles.z);
            }

            if (string.IsNullOrEmpty(_skinPath) && IsLocalPlayer && !Application.isPlaying)
            {
                _skinPath = "Visual/Skin/bee.png";
                _tailPath = "Visual/Tail/default.png";
            }

            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadMetadataAssets();
            }

            _movement.TargetAngle = transform.eulerAngles.z;

            if (gameObject.CompareTag("Player"))
            {
                _robotService?.RegisterRobot(this);
            }
        }

        protected void Update()
        {
            if (Application.isPlaying)
            {
                if (IsLocalPlayer && _localPlayer is not { Current: { HasServerPosition: true } })
                {
                    return;
                }

                if (!IsLocalPlayer && (!_isMetadataLoaded || !_movement.HasReceivedInitialPosition))
                {
                    return;
                }
            }

            TryInitializeDynamicLightSettings();
            ApplyPendingServerPosition();

            if (_presentationState.IsTeleportPreparing)
            {
                return;
            }

            if (!IsLocalPlayer && _culler.CheckAndApply(
                transform,
                _gameplayCamera?.Camera,
                _visuals,
                _nameplate,
                _lighting,
                _movement,
                _lightingEngine))
            {
                return;
            }

            if (_movement.IsSettled(_visuals.TentaclesSettled))
            {
                // The last moving frame may leave the rendered Transform at
                // the visual tremor offset. The light source is anchored to
                // SmoothPosition, so settle the sprite on that same position.
                transform.position = _movement.SmoothPosition;
                _visuals.UpdateMotion(
                    transform.position,
                    0f,
                    Time.deltaTime,
                    true,
                    _debugSettings.TailMotionDebugMode);
                _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);
                _lighting.Update(_movement.SmoothPosition, _lightingEngine);
                return;
            }

            var (finalPosition, nowRotationAngle, movementFactor) = _movement.Step(Time.deltaTime);

            transform.position = finalPosition;
            transform.rotation = Quaternion.Euler(0, 0, nowRotationAngle);

            _visuals.UpdateMotion(
                finalPosition,
                movementFactor,
                Time.deltaTime,
                false,
                _debugSettings.TailMotionDebugMode);
            _nameplate.UpdatePosition(finalPosition, _visuals.SkinSprite, transform, _visuals.ClanTransform);
            _lighting.Update(_movement.SmoothPosition, _lightingEngine);
        }

        public void ResetDynamicLightPreferences()
        {
            if (IsLocalPlayer)
            {
                _lighting.ResetPreferences(_lightingEngine);
            }
        }

        private void TryInitializeDynamicLightSettings() => _lighting.InitializeSettings(_lightingEngine);

        public void Initialize(uint botId)
        {
            TryInitializeDynamicLightSettings();
            _botId = botId;
            _robotManager.RegisterRobot(this);

            _isMetadataLoaded = false;
            _visualsLoadCompleted = false;
            RobotMetadataPresentation.ResetColor(_spriteRenderer, _visuals);
            _nameplate.SetText(string.Empty, IsLocalPlayer);
            _visuals.SetClanSprite(null);
        }

        public void SetMetadata(int playerId, byte clanId, string nickname, string skinPath, string tailPath)
        {
            if (_isMetadataLoaded &&
                _playerId == playerId &&
                _clanId == clanId &&
                string.Equals(_nickname, nickname, global::System.StringComparison.Ordinal) &&
                string.Equals(_skinPath, skinPath, global::System.StringComparison.Ordinal) &&
                string.Equals(_tailPath, tailPath, global::System.StringComparison.Ordinal))
            {
                return;
            }

            _playerId = playerId;
            _clanId = clanId;
            _nickname = nickname;
            _skinPath = skinPath;
            _tailPath = tailPath;
            _isMetadataLoaded = true;
            _visualsLoadCompleted = string.IsNullOrEmpty(_skinPath);

            RobotMetadataPresentation.ResetColor(_spriteRenderer, _visuals);
            InitializeVisualElements();
            _nameplate.SetText(nickname, IsLocalPlayer);
            _nameplate.InvalidatePosition();
            _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);

            LoadMetadataAssets();
        }

        public void SetPosition(ushort x, ushort y) => ApplyServerPosition(x, y);

        // Мгновенный снап визуала робота в текущую TargetPosition (респаун, ТП):
        // без него сегменты тела "плывут" к точке на скорости обычного движения.
        public void SnapVisualToTarget()
        {
            _movement.TeleportToTarget();
            transform.position = _movement.SmoothPosition;
            _visuals.SnapTentacles(_movement.SmoothPosition);
            _visuals.UpdateMotion(
                transform.position,
                0f,
                Time.deltaTime,
                true,
                _debugSettings.TailMotionDebugMode);
            _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);
            _lighting.Update(_movement.SmoothPosition, _lightingEngine);
        }

        private void HandleTeleportCommitting(ulong transitionId)
        {
            if (_presentationState.Current.TransitionId != transitionId)
            {
                return;
            }

            SnapVisualToTarget();
        }

        private void ApplyPendingServerPosition()
        {
            if (!_hasPendingServerPosition)
            {
                return;
            }

            ApplyServerPosition(_pendingServerX, _pendingServerY);
            _hasPendingServerPosition = false;
        }

        private void ApplyServerPosition(ushort x, ushort y)
        {
            if (_movement.ApplyServerPosition(x, y, _mapManager.WorldHeight, IsLocalPlayer, out bool isInitial) && isInitial)
            {
                transform.position = _movement.ServerPosition;
                _visuals.SnapTentacles(_movement.SmoothPosition);
                _visuals.SetTentaclesActive(true);
            }
        }

        public void SetRotation(byte rotation) => TargetAngle = PlayerDirectionMapping.RotationByteToAngle(rotation);

        private void LoadMetadataAssets()
        {
            if (_assetLoader != null && _operations != null)
            {
                if (!_watchingAssets)
                {
                    _assetLoader.AssetsChanged += OnAssetsChanged;
                    _watchingAssets = true;
                }

                RobotMetadataPresentation.Load(AssetLoader, _skinPath, _tailPath, _clanId,
                    IsLocalPlayer, _visuals, _nameplate, transform, () => _visualsLoadCompleted = true);
            }
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            RobotGizmos.DrawGizmosIfEnabled(
                _debugSettings,
                transform,
                _botId,
                IsLocalPlayer,
                _isMetadataLoaded,
                _moveSpeed,
                _movement.ServerPosition,
                _movement.TargetPosition);
        }
#endif

        // Сервер заменил файлы посреди игры: перезагружаем облик, только если среди них скин, хвост или значок клана
        private void OnAssetsChanged(IReadOnlyCollection<string> changed)
        {
            if (!_isMetadataLoaded)
            {
                return;
            }

            string? clanPath = _clanId != 0 && !IsLocalPlayer ? RobotAssetLoader.ClanPath(_clanId) : null;
            foreach (string filename in changed)
            {
                if (string.Equals(filename, _skinPath, StringComparison.Ordinal) ||
                    string.Equals(filename, _tailPath, StringComparison.Ordinal) ||
                    string.Equals(filename, clanPath, StringComparison.Ordinal))
                {
                    LoadMetadataAssets();
                    return;
                }
            }
        }

        protected void OnDestroy()
        {
            if (_watchingAssets && _assetLoader != null)
            {
                _assetLoader.AssetsChanged -= OnAssetsChanged;
            }

            if (_presentationState != null)
            {
                _presentationState.TeleportCommitting -= HandleTeleportCommitting;
            }

            _assetLoaderHelper?.Cancel();
            _robotService?.UnregisterRobot(_botId);
            _nameplate.Destroy();
            _visuals?.Destroy();
        }
    }
}
