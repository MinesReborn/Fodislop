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
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using InvalidOperationException = System.InvalidOperationException;
using OperationCanceledException = System.OperationCanceledException;

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
        private Transform? _clanTransform;
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
        private static bool s_previewFallbackWarningLogged;

        private bool _isMetadataLoaded;
        private bool _visualsLoadCompleted;
        private CancellationTokenSource? _cts;
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;

        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private IProjectDefaults _projectDefaults = null!;
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
        private bool _isCulled;
        private const float OffscreenCullDistance = 35f;
        private const float OffscreenCullSqrDistance = OffscreenCullDistance * OffscreenCullDistance;
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
            _visuals.Initialize(entityBatchRenderer, _clanTransform);
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

            if (_cts != null)
            {
                CancellationToken entityToken = _cts.Token;
                _operations.Run(
                    "load_robot_clan_badge",
                    supervisorToken => RunWithLinkedCancellationAsync(
                        LoadClanAsync,
                        entityToken,
                        supervisorToken));
            }
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

            if (_clanTransform == null)
            {
                Transform? existingClan = transform.Find("ClanIcon");
                GameObject clanGo = existingClan != null
                    ? existingClan.gameObject
                    : (_sceneObjects != null
                        ? _sceneObjects.Create("ClanIcon", RuntimeOwner.Robots)
                        : throw new InvalidOperationException(
                            $"{TAG} ISceneObjectFactory was not injected before creating ClanIcon for bot {_botId}."));
                clanGo.transform.SetParent(transform, worldPositionStays: false);
                _clanTransform = clanGo.transform;
                _clanTransform.localScale = Vector3.one * 0.8f;
            }
        }

        public void SetBatchedBodyVisible(bool visible)
        {
            _visuals.SetBodyVisible(visible);
        }

        private void ApplyWorldUILayer()
        {
            if (_clanTransform == null)
            {
                _clanTransform = transform.Find("ClanIcon");
            }

            _nameplate.ApplyLayer();
        }

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

            if (!IsLocalPlayer)
            {
                Camera? cam = _gameplayCamera?.Camera;
                Vector2 diff = cam != null
                    ? new Vector2(transform.position.x - cam.transform.position.x, transform.position.y - cam.transform.position.y)
                    : Vector2.zero;
                float sqrDistToCam = diff.sqrMagnitude;
                bool shouldCull = sqrDistToCam > OffscreenCullSqrDistance;

                if (shouldCull)
                {
                    if (!_isCulled)
                    {
                        _isCulled = true;
                        _visuals.SetBodyVisible(false);
                        _nameplate.SetEnabled(false);
                        _visuals.SetTentaclesActive(false);
                        _lighting.Remove(_lightingEngine);
                    }

                    transform.position = _movement.TargetPosition;
                    _movement.TeleportToTarget();
                    transform.rotation = Quaternion.Euler(0, 0, _movement.TargetAngle);
                    return;
                }

                if (_isCulled)
                {
                    _isCulled = false;
                    _visuals.SetBodyVisible(true);
                    _nameplate.SetEnabled(true);
                    _visuals.SetTentaclesActive(true);
                    _visuals.SnapTentacles(transform.position);
                }
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

        public void SetDynamicLightIntensity(float intensity)
        {
            _lighting.SetIntensity(intensity, _lightingEngine);
        }

        public void SetDynamicLightColor(Color color)
        {
            _lighting.SetColor(color, _lightingEngine);
        }

        public void ResetDynamicLightPreferences()
        {
            if (IsLocalPlayer)
            {
                _lighting.ResetPreferences(_projectDefaults, _lightingEngine);
            }
        }

        private void TryInitializeDynamicLightSettings()
        {
            _lighting.InitializeSettings(_projectDefaults, _lightingEngine);
        }

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

        public void SetPosition(ushort x, ushort y)
        {
            ApplyServerPosition(x, y);
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
            if (_movement.ApplyServerPosition(x, y, _mapManager.WorldHeight, IsLocalPlayer, out bool isInitial))
            {
                if (isInitial)
                {
                    transform.position = _movement.ServerPosition;
                    _visuals.SnapTentacles(_movement.SmoothPosition);
                    _visuals.SetTentaclesActive(true);
                }
            }
        }

        public void SetRotation(byte rotation)
        {
            TargetAngle = rotation switch
            {
                0 => 270f,
                1 => 180f,
                2 => 90f,
                3 => 0f,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(rotation),
                    rotation,
                    $"[{TAG}] Unsupported robot rotation value for bot {_botId}."),
            };
        }

        private void LoadMetadataAssets()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            CancellationToken entityToken = _cts.Token;

            _operations.Run(
                "load_robot_metadata_assets",
                supervisorToken => LoadMetadataAssetsAsync(
                    entityToken,
                    supervisorToken));
        }

        private async UniTask LoadMetadataAssetsAsync(
            CancellationToken entityToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                entityToken,
                supervisorToken);
            CancellationToken token = linkedCancellation.Token;
            UniTask clanTask = IsLocalPlayer
                ? UniTask.CompletedTask
                : LoadClanAsync(token);
            await UniTask.WhenAll(
                LoadSkinAsync(token),
                LoadTailAsync(token),
                clanTask);
        }

        private static async UniTask RunWithLinkedCancellationAsync(
            Func<CancellationToken, UniTask> operation,
            CancellationToken entityToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                entityToken,
                supervisorToken);
            await operation(linkedCancellation.Token);
        }

        private async UniTask LoadSkinAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_skinPath))
            {
                return;
            }

            Texture2D? skinTexture = await TryLoadOptionalTextureAsync(_assetLoader, _skinPath, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (skinTexture == null)
            {
                _visualsLoadCompleted = true;
                return;
            }

            Sprite skinSprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
            _visuals.SetSkinSprite(skinSprite);
            _nameplate.InvalidatePosition();
            _nameplate.UpdatePosition(transform.position, _visuals.SkinSprite, transform, _visuals.ClanTransform);
            _visualsLoadCompleted = true;
        }

        public void EnsureEditorPreviewVisual()
        {
            if (_spriteRenderer != null && _spriteRenderer.sprite == null)
            {
                Texture2D? botTex = Resources.Load<Texture2D>(
                    ProjectRuntimeContracts.ResourcePaths.RobotPreviewTexture);
                if (botTex == null)
                {
                    botTex = Resources.Load<Texture2D>(
                        ProjectRuntimeContracts.ResourcePaths.LegacyRobotPreviewTexture);
                    LogPreviewFallbackWarning(botTex != null);
                }

                Sprite previewSprite;
                if (botTex != null)
                {
                    previewSprite = Sprite.Create(
                        botTex,
                        new Rect(0, 0, botTex.width, botTex.height),
                        new Vector2(0.5f, 0.5f),
                        ProjectRuntimeContracts.PreviewVisuals.RobotPixelsPerUnit);
                }
                else
                {
                    var tex = Texture2D.whiteTexture;
                    previewSprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        ProjectRuntimeContracts.PreviewVisuals.RobotPixelsPerUnit);
                }

                _visuals.SetSkinSprite(previewSprite);
                _spriteRenderer.sprite = previewSprite;
                _spriteRenderer.color = new Color(0.2f, 0.65f, 0.95f, 1f);
                _spriteRenderer.enabled = true;
            }
        }

        private static void LogPreviewFallbackWarning(bool legacyPreviewFound)
        {
            if (s_previewFallbackWarningLogged)
            {
                return;
            }

            s_previewFallbackWarningLogged = true;
            string fallback = legacyPreviewFound
                ? $"legacy resource '{ProjectRuntimeContracts.ResourcePaths.LegacyRobotPreviewTexture}'"
                : "the generated white placeholder";
            Debug.LogWarning(
                $"[Robot] Required preview resource " +
                $"'{ProjectRuntimeContracts.ResourcePaths.RobotPreviewTexture}' was not found; using {fallback}.");
        }

        private async UniTask LoadTailAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_tailPath))
            {
                _visuals.ClearTentacles();
                return;
            }

            Texture2D? tailTexture = await TryLoadOptionalTextureAsync(_assetLoader, _tailPath, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (tailTexture == null)
            {
                _visuals.ClearTentacles();
                return;
            }

            _visuals.CreateTentacles(tailTexture, transform.position);
        }

        private async UniTask LoadClanAsync(CancellationToken token)
        {
            if (_clanId == 0)
            {
                return;
            }

            string clanPath = $"/Clan/{_clanId}";
            Texture2D? clanTexture = await TryLoadOptionalTextureAsync(_assetLoader, clanPath, token);
            if (token.IsCancellationRequested || clanTexture == null)
            {
                return;
            }

            Sprite clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _visuals.SetClanSprite(clanSprite);
        }

        private static async UniTask<Texture2D?> TryLoadOptionalTextureAsync(
            IAssetLoader loader,
            string filename,
            CancellationToken cancellationToken)
        {
            try
            {
                return await loader.GetTextureAsync(filename, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"{TAG} Optional texture '{filename}' was skipped: {exception.Message}");
                return null;
            }
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

            Fodinae.World.FodinaeGizmos.DrawBounds(_movement.ServerPosition, Vector2.one * 1.0f, Color.red);
            Fodinae.World.FodinaeGizmos.DrawBounds(_movement.TargetPosition, Vector2.one * 0.9f, Color.blue);
            Fodinae.World.FodinaeGizmos.DrawBounds(transform.position, Vector2.one * 0.8f, Color.cyan);

            float angleRad = (transform.eulerAngles.z + VISUAL_ROTATION_OFFSET) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0);
            Fodinae.World.FodinaeGizmos.DrawArrow(transform.position, direction, Color.yellow, 1.2f);

            string status = $"ID: {_botId}\n{(IsLocalPlayer ? "LOCAL PLAYER" : "REMOTE ROBOT")}\n" +
                            $"Meta: {(_isMetadataLoaded ? "OK" : "PENDING")}\n" +
                            $"Speed: {_moveSpeed:F1}";
            Fodinae.World.FodinaeGizmos.DrawLabel(transform.position + (Vector3.up * 1.5f), status, _isMetadataLoaded ? Color.green : Color.orange);

            if (!IsLocalPlayer)
            {
                float lag = Vector3.Distance(_movement.ServerPosition, transform.position);
                if (lag > 0.5f)
                {
                    Fodinae.World.FodinaeGizmos.DrawDottedLine(transform.position, _movement.ServerPosition, Color.red, 4f);
                }
            }
        }
#endif

        protected void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _robotService?.UnregisterRobot(_botId);
            _nameplate.Destroy();
            _visuals?.Destroy();
        }
    }
}
