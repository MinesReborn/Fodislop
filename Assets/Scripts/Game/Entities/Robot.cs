#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game.Managers;
using Fodinae.Player.Logic;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;

namespace Fodinae.Game
{
    public class Robot : MonoBehaviour, IRobotView
    {
        private const string TAG = "[Robot]";

        [SerializeField]
        private uint _botId;
        [SerializeField]
        private int _playerId;
        [SerializeField]
        private byte _clanId;
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
        [Header("Dynamic Emission")]
        [SerializeField]
        [Tooltip("Разрешает Robot регистрировать dynamic emission source в LightingEngine.")]
        private bool _emitsDynamicLight;
        [SerializeField]
        [Range(0f, 4f)]
        [Tooltip("Интенсивность dynamic emission. HDR-значение выше 1 усиливает источник.")]
        private float _dynamicLightIntensity;
        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        [Tooltip("HDR-цвет dynamic emission источника Robot.")]
        private Color _dynamicLightColor;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private bool _isMetadataLoaded;
        private bool _visualsLoadCompleted;
        private RobotAssetLoader? _assetLoaderHelper;
        private RobotAssetLoader AssetLoader => _assetLoaderHelper ??= new RobotAssetLoader(_assetLoader, _operations, this.GetCancellationTokenOnDestroy());
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;

        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        private RobotLighting _lighting = null!;
        private RobotVisuals _visuals = null!;
        private readonly RobotNameplate _nameplate = new();
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
            _lighting ??= new RobotLighting(_emitsDynamicLight, _dynamicLightIntensity, _dynamicLightColor);
            _visuals ??= new RobotVisuals(transform, IsLocalPlayer);
            _entityBatchRenderer = entityBatchRenderer;
            InitializeVisualElements();
            _visuals.Initialize(entityBatchRenderer, _visuals.ClanTransform);
        }

        /// <summary>
        /// Lazily creates <see cref="_visuals"/> and <see cref="_lighting"/> when
        /// VContainer resolves [Inject] methods before <see cref="Awake"/> has run.
        /// Safe to call multiple times — Awake re-assigns with the same values.
        /// </summary>
        private void EnsureVisuals()
        {
            _lighting ??= new RobotLighting(_emitsDynamicLight, _dynamicLightIntensity, _dynamicLightColor);
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

        public void SetClanBadge(ushort clanId)
        {
            _clanId = (byte)clanId;
            if (_clanId == 0)
            {
                ClearClanBadge();
                return;
            }

            _assetLoaderHelper?.LoadClanBadge(_clanId, clanSprite =>
            {
                if (clanSprite != null)
                {
                    _visuals.SetClanSprite(clanSprite);
                }
            });
        }

        public void ClearClanBadge()
        {
            _clanId = 0;
            _visuals.SetClanSprite(null);
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

            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }
        }

        protected void OnEnable()
        {
            ApplyWorldUILayer();
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
            _nameplate.Initialize(transform, _botId, _nickname, IsLocalPlayer, _sceneObjects);
            _visuals.EnsureClanIcon(_sceneObjects, _botId);
        }

        public void SetBatchedBodyVisible(bool visible) => _visuals.SetBodyVisible(visible);

        public void SetAuraWanted(bool wanted) => _visuals?.SetAuraWanted(wanted, _sceneObjects);

        private void ApplyWorldUILayer() => _nameplate.ApplyLayer();

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

        public void SetDynamicLightIntensity(float intensity) => _lighting.SetIntensity(intensity, _lightingEngine);

        public void SetDynamicLightColor(Color color) => _lighting.SetColor(color, _lightingEngine);

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
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            _visuals.SetColor(Color.white);
            _nameplate.SetText(string.Empty, IsLocalPlayer);
            _visuals.SetClanSprite(null);
        }

        public void SetMetadata(int playerId, byte clanid, string nickname, string skinPath, string tailPath)
        {
            if (_isMetadataLoaded &&
                _playerId == playerId &&
                _clanId == clanid &&
                string.Equals(_nickname, nickname, global::System.StringComparison.Ordinal) &&
                string.Equals(_skinPath, skinPath, global::System.StringComparison.Ordinal) &&
                string.Equals(_tailPath, tailPath, global::System.StringComparison.Ordinal))
            {
                return;
            }

            _playerId = playerId;
            _clanId = clanid;
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
            AssetLoader.LoadMetadataAssets(
                _skinPath,
                _tailPath,
                _clanId,
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

        /// <summary>
        /// Заглушка вида робота для редактора, пока не приехал настоящий скин.
        /// </summary>
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
                _botId,
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
            _robotService?.UnregisterRobot(_botId);
            _nameplate.Destroy();
            _visuals?.Destroy();
        }
    }
}
