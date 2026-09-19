#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Managers;
using Kern.Player.Logic;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine;
using VContainer;

namespace Kern.Game
{
    public class Robot : MonoBehaviour, IRobotView
    {
        private const string TAG = "[Robot]";

        [SerializeField]
        private uint _botID;
        [SerializeField]
        private int _playerID;
        [SerializeField]
        private byte _clanID;
        [SerializeField]
        private SpriteRenderer? _spriteRenderer;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [SerializeField]
        private string _nickname = string.Empty;
        [SerializeField]
        private string _skinPath = string.Empty;
        [SerializeField]
        private string _tailPath = string.Empty;
        [SerializeField]
        private float _rotationSpeed = ProjectRuntimeContracts.Movement.RobotRotationSpeed;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private bool _isMetadataLoaded;
        private bool _visualsLoadCompleted;
        private RobotAssetLoader? _assetLoaderHelper;
        private RobotAssetLoader _AssetLoader => _assetLoaderHelper ??= new RobotAssetLoader(_assetLoader, _operations, this.GetCancellationTokenOnDestroy());
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;

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
        private ushort _pendingServerX;
        private ushort _pendingServerY;
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

        public uint BotID => _botID;
        public int PlayerID => _playerID;
        public byte ClanID => _clanID;
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
            _visuals.Initialize(entityBatchRenderer, _visuals.ClanTransform);
        }

        private void EnsureVisuals()
        {
            _lighting ??= new RobotLighting();
            _visuals ??= new RobotVisuals(transform, IsLocalPlayer);
        }

        public float LogicalFacingAngle => _movement.TargetAngle;

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

        public float MoveSpeed
        {
            get => _moveSpeed;
            set
            {
                _moveSpeed = value;
                _movement.MoveSpeed = value;
            }
        }

        protected void Awake()
        {
            EnsureVisuals();
            _movement.MoveSpeed = _moveSpeed;
            _movement.RotationSpeed = _rotationSpeed;

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (_spriteRenderer != null && Application.isPlaying)
            {
                _spriteRenderer.enabled = false;
            }

            transform.localScale = Vector3.one;
            _movement.SnapTo(transform.position, transform.eulerAngles.z);
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
            _nameplate.Initialize(transform, _botID, _nickname, IsLocalPlayer, _sceneObjects, _worldLabels);
            _visuals.EnsureClanIcon(_sceneObjects, _botID);
        }

        public void SetBatchedBodyVisible(bool visible) => _visuals.SetBodyVisible(visible);

        public void SetAuraWanted(bool wanted) => _visuals?.SetAuraWanted(wanted, _sceneObjects);


        protected void Start()
        {
            TryInitializeDynamicLightSettings();

            Vector3 snappedPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z);
            transform.position = snappedPos;
            _movement.SnapTo(snappedPos, transform.eulerAngles.z);

            if (string.IsNullOrEmpty(_skinPath) && IsLocalPlayer && !Application.isPlaying)
            {
                _skinPath = "Skin/bee.png";
                _tailPath = "Tail/default.png";
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
            _visuals.TickAura(Time.deltaTime);

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
                _visuals.UpdateMotion(transform.position, transform.eulerAngles.z, 0f, Time.deltaTime, true);
                _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);
                _lighting.Update(_movement.SmoothPosition, _lightingEngine);
                return;
            }

            var (finalPosition, nowRotationAngle, movementFactor, snapped) = _movement.Step(Time.deltaTime);

            if (snapped)
            {
                _visuals.SnapTentacles(_movement.SmoothPosition);
            }

            transform.position = finalPosition;
            transform.rotation = Quaternion.Euler(0, 0, nowRotationAngle);

            _visuals.UpdateMotion(finalPosition, nowRotationAngle, movementFactor, Time.deltaTime, false);
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

        public void Initialize(uint botID)
        {
            TryInitializeDynamicLightSettings();
            _botID = botID;
            _robotManager.RegisterRobot(this);

            _isMetadataLoaded = false;
            _visualsLoadCompleted = false;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            _visuals.SetColor(Color.white);
            _nameplate.SetText(string.Empty, IsLocalPlayer);
            _visuals.SetClanSprite(null);
        }

        public void SetMetadata(int playerID, byte clanID, string nickname, string skinPath, string tailPath)
        {
            if (_isMetadataLoaded &&
                _playerID == playerID &&
                _clanID == clanID &&
                string.Equals(_nickname, nickname, global::System.StringComparison.Ordinal) &&
                string.Equals(_skinPath, skinPath, global::System.StringComparison.Ordinal) &&
                string.Equals(_tailPath, tailPath, global::System.StringComparison.Ordinal))
            {
                return;
            }

            _playerID = playerID;
            _clanID = clanID;
            _nickname = nickname;
            _skinPath = skinPath;
            _tailPath = tailPath;
            _isMetadataLoaded = true;
            _visualsLoadCompleted = string.IsNullOrEmpty(_skinPath);

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            _visuals.SetColor(Color.white);
            InitializeVisualElements();
            _nameplate.SetText(nickname, IsLocalPlayer);
            _nameplate.InvalidatePosition();
            _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);

            LoadMetadataAssets();
        }

        public void SetPosition(ushort x, ushort y) => ApplyServerPosition(x, y);

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

        public void SetRotation(byte rotation) => TargetAngle = PlayerMovementMath.RotationByteToAngle(rotation);

        private void LoadMetadataAssets()
        {
            if (_assetLoader == null || _operations == null)
            {
                return;
            }

            _AssetLoader.LoadMetadataAssets(
                _skinPath,
                _tailPath,
                _clanID,
                IsLocalPlayer,
                onSkinLoaded: skinSprite =>
                {
                    if (skinSprite != null)
                    {
                        _visuals.SetSkinSprite(skinSprite);
                        _nameplate.InvalidatePosition();
                        _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);
                    }

                    _visualsLoadCompleted = true;
                },
                onTailLoaded: tailTexture =>
                {
                    if (tailTexture != null)
                    {
                        _visuals.CreateTentacles(tailTexture, transform.position);
                    }
                    else
                    {
                        _visuals.ClearTentacles();
                    }
                },
                onClanLoaded: clanSprite =>
                {
                    if (clanSprite != null)
                    {
                        _visuals.SetClanSprite(clanSprite);
                    }
                });
        }

        public void EnsureEditorPreviewVisual()
        {
            if (_spriteRenderer == null || _spriteRenderer.sprite != null)
            {
                return;
            }

            Sprite previewSprite = RobotAssetLoader.CreateEditorPreviewSprite();
            _visuals.SetSkinSprite(previewSprite);
            _spriteRenderer.sprite = previewSprite;
            _spriteRenderer.color = new Color(0.2f, 0.65f, 0.95f, 1f);
            _spriteRenderer.enabled = true;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            if (!Application.isPlaying ||
                _debugSettings == null ||
                !_debugSettings.ShowRobotDebugVisuals)
            {
                return;
            }

            RobotGizmos.DrawGizmos(
                transform,
                _botID,
                IsLocalPlayer,
                _isMetadataLoaded,
                _moveSpeed,
                _movement.ServerPosition,
                _movement.TargetPosition);
        }
#endif

        protected void OnDestroy()
        {
            _assetLoaderHelper?.Cancel();
            _robotService?.UnregisterRobot(_botID);
            _nameplate.Destroy();
            _visuals?.Destroy();
        }
    }
}
