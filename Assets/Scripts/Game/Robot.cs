using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Utils;
using UnityEngine;

namespace Fodinae.Scripts.Game
{
    public class Robot : MonoBehaviour
    {
        [SerializeField] private uint _botId;
        [SerializeField] private int _playerId;
        [SerializeField] private byte _clanId;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        private SpriteRenderer _clanRenderer;
        private TextMesh _nicknameText;
        [SerializeField] private string _nickname;
        [SerializeField] private string _skinPath;
        [SerializeField] private string _tailPath;
        [SerializeField] private float _rotationSpeed = 1080f;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private const float MIN_SMOOTH_TIME = 0.05f;
        private const float MAX_SMOOTH_TIME = 0.15f;
        private const float REFERENCE_MOVE_SPEED = 25f;

        private bool _isMetadataLoaded = false;
        private CancellationTokenSource _cts;
        private float _targetAngle = 0f;
        private float _smoothAngle = 0f;
        private Vector3 _targetPosition;
        private Vector3 _serverPosition;
        private Vector3 _smoothPosition;
        private Vector3 _currentVelocity;
        private float _currentAngularVelocity;
        [SerializeField] private float _moveSpeed = 15f;
        private float _tremor = 0f;

        private Tentacle[] _tentacles;
        private GameObject _tailContainer;
        private Sprite _skinSprite;
        private Sprite _clanSprite;

        public uint BotId => _botId;
        public int PlayerId => _playerId;
        public byte ClanId => _clanId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;
        public bool IsLocalPlayer => gameObject.CompareTag("Player");

        /// <summary>
        /// The logical facing angle in Unity degrees (raw <c>_targetAngle</c>),
        /// without visual smoothing or tremor. Use this when positioning effects
        /// that need to align with the bot's true facing direction at creation.
        /// </summary>
        public float LogicalFacingAngle => _targetAngle;

        public float TargetAngle
        {
            get => _targetAngle - VISUAL_ROTATION_OFFSET;
            set => _targetAngle = value + VISUAL_ROTATION_OFFSET;
        }

        public Vector3 TargetPosition
        {
            get => _targetPosition;
            set => _targetPosition = value;
        }

        public void SetClanBadge(ushort clanId)
        {
            _clanId = (byte)clanId;
            if (_clanId == 0)
            {
                ClearClanBadge();
                return;
            }

            LoadClanAsync(_cts.Token).Forget();
        }

        public void ClearClanBadge()
        {
            _clanId = 0;
            if (_clanRenderer != null)
            {
                _clanRenderer.sprite = null;
            }

            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
                _clanSprite = null;
            }
        }

        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = value;
        }

        private void Awake()
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            transform.localScale = Vector3.one;
            _targetPosition = transform.position;
            _serverPosition = transform.position;
            _smoothPosition = transform.position;
            _smoothAngle = transform.eulerAngles.z;

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }

            InitializeVisualElements();
        }

        private void InitializeVisualElements()
        {
            _tailContainer = new GameObject("TailContainer");
            _tailContainer.transform.SetParent(transform);
            _tailContainer.transform.localPosition = Vector3.zero;

            var textGo = new GameObject("Nickname");
            textGo.transform.SetParent(transform);
            _nicknameText = textGo.AddComponent<TextMesh>();
            _nicknameText.anchor = TextAnchor.MiddleLeft;
            _nicknameText.alignment = TextAlignment.Left;
            _nicknameText.fontSize = 64;
            _nicknameText.characterSize = 0.1f;
            _nicknameText.color = Color.white;

            var textRenderer = textGo.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 100;

            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            _clanRenderer.sortingOrder = 100;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;
        }

        private void Start()
        {
            Vector3 snappedPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z
            );
            transform.position = snappedPos;
            _targetPosition = snappedPos;
            _serverPosition = snappedPos;
            _smoothPosition = snappedPos;
            _smoothAngle = transform.eulerAngles.z;

            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadMetadataAssets();
            }
            _targetAngle = transform.eulerAngles.z;

            if (gameObject.CompareTag("Player"))
            {
                RobotManager.Instance?.RegisterRobot(this);
            }
        }

        private void Update()
        {
            float renderDistance = Vector2.Distance(_smoothPosition, _targetPosition);

            // 1. Base smooth time now scales PROPORTIONALLY with speed.
            // Slower = snappier/tighter (low smooth time). Faster = momentum/curves (higher smooth time).
            float speedRatio = Mathf.Clamp01(_moveSpeed / REFERENCE_MOVE_SPEED);
            float targetSmoothTime = Mathf.Lerp(MIN_SMOOTH_TIME, MAX_SMOOTH_TIME, speedRatio);

            // 2. Distance factor: get extra snappy when very close to the target (e.g. moving exactly 1 cell and stopping)
            float distanceRatio = Mathf.Clamp01(renderDistance / 2f);
            float smoothTime = Mathf.Lerp(MIN_SMOOTH_TIME, targetSmoothTime, distanceRatio);

            if (renderDistance > 28f)
            {
                _smoothPosition = _targetPosition;
                _smoothAngle = _targetAngle;
                _currentVelocity = Vector3.zero;
                _currentAngularVelocity = 0f;

                if (_tentacles != null)
                {
                    foreach (var tentacle in _tentacles)
                    {
                        tentacle.Snap(_smoothPosition);
                    }
                }
            }
            else
            {
                // 3. Max Visual Speed limits the catch-up rate. 
                // Setting it to 1.25x of logical speed allows it to easily catch up without wildly slingshotting,
                // bridging the gaps between server "ticks" smoothly when running continuously.
                float maxVisualSpeed = Mathf.Max(_moveSpeed * 1.25f, 5f);
                _smoothPosition = Vector3.SmoothDamp(_smoothPosition, _targetPosition, ref _currentVelocity, smoothTime, maxVisualSpeed, Time.deltaTime);
            }

            // Apply tremor logic
            Vector3 finalPosition = _smoothPosition;
            if (_tremor > 0.01f)
            {
                _tremor *= Mathf.Pow(0.8f, Time.deltaTime / 0.016f);
                finalPosition.x += _tremor * (Random.value - 0.5f);
                finalPosition.y += _tremor * (Random.value - 0.5f);
            }
            transform.position = finalPosition;

            // Apply rotation smoothing (now limits turning rate using your previously unused _rotationSpeed field)
            float targetAngle = _targetAngle;
            _smoothAngle = Mathf.SmoothDampAngle(_smoothAngle, targetAngle, ref _currentAngularVelocity, smoothTime, _rotationSpeed, Time.deltaTime);

            float nowRotationAngle = _smoothAngle;
            if (_skinPath != "1")
            {
                nowRotationAngle += 6.6f * renderDistance * (0.5f - Random.value);
            }

            transform.rotation = Quaternion.Euler(0, 0, nowRotationAngle);

            float movementFactor = Mathf.Clamp01(_currentVelocity.magnitude / 5f);
            UpdateTentacles(finalPosition, nowRotationAngle, movementFactor, Time.deltaTime);

            UpdateLabelsPosition();
        }

        private void CreateTentacles(Texture2D tailTexture)
        {
            ClearTentacles();
            _tentacles = new Tentacle[4];
            float[] offsets = { -45f, -15f, 15f, 45f };
            for (int i = 0; i < 4; i++)
            {
                _tentacles[i] = new Tentacle(_tailContainer, tailTexture, offsets[i], -1, i, 4);
            }
        }

        private void ClearTentacles()
        {
            if (_tentacles != null)
            {
                foreach (var tentacle in _tentacles)
                {
                    tentacle?.Destroy();
                }
                _tentacles = null;
            }
        }

        private void UpdateTentacles(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
        {
            if (_tentacles == null) return;
            foreach (var tentacle in _tentacles)
            {
                tentacle.Update(rootPosition, rotationAngle, movementFactor, deltaTime);
            }
        }

        private void UpdateLabelsPosition()
        {
            if (_nicknameText != null)
            {
                _nicknameText.transform.position = transform.position + new Vector3(0.6f, 0.5f, 0);
                _nicknameText.transform.rotation = Quaternion.identity;
            }

            if (_clanRenderer != null)
            {
                _clanRenderer.transform.position = transform.position + new Vector3(0.6f, -0.5f, 0);
                _clanRenderer.transform.rotation = Quaternion.identity;
            }
        }

        public void Initialize(uint botId)
        {
            _botId = botId;
            RobotManager.Instance?.RegisterRobot(this);

            _isMetadataLoaded = false;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1, 1, 1, 0.5f);
            }

            if (_nicknameText != null)
            {
                _nicknameText.text = "";
            }

            if (_clanRenderer != null)
            {
                _clanRenderer.sprite = null;
            }
        }

        public void SetMetadata(int playerId, byte clanid, string nickname, string skinPath, string tailPath)
        {
            _playerId = playerId;
            _clanId = clanid;
            _nickname = nickname;
            _skinPath = skinPath;
            _tailPath = tailPath;
            _isMetadataLoaded = true;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            if (_nicknameText != null)
            {
                _nicknameText.text = nickname;
            }

            LoadMetadataAssets();
        }

        public void SetPosition(ushort x, ushort y)
        {
            var mm = MapManager.Instance;
            if (mm != null)
            {
                _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, mm.WorldHeight);
            }

            // Only update target position from server for remote robots.
            // Local player manages its own target position via PlayerMovementController.
            // If the local player is too far from server position, we should snap.
            if (!IsLocalPlayer || Vector3.Distance(_targetPosition, _serverPosition) > 2.0f)
            {
                _targetPosition = _serverPosition;
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
                _ => 0f
            };
        }

        private void LoadMetadataAssets()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadMetadataAssetsAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid LoadMetadataAssetsAsync(CancellationToken token)
        {
            LoadSkinAsync(token).Forget();
            LoadTailAsync(token).Forget();
            if (!IsLocalPlayer)
            {
                LoadClanAsync(token).Forget();
            }

            await UniTask.CompletedTask;
        }

        private async UniTaskVoid LoadSkinAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_skinPath))
            {
                return;
            }

            var loader = ClientAssetLoader.Instance;
            if (loader == null)
            {
                return;
            }

            var skinTexture = await loader.GetTextureAsync(_skinPath, token);
            if (token.IsCancellationRequested || skinTexture == null || _spriteRenderer == null)
            {
                return;
            }

            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            _skinSprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
            _spriteRenderer.sprite = _skinSprite;
        }

        private async UniTaskVoid LoadTailAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_tailPath))
            {
                ClearTentacles();
                return;
            }

            var loader = ClientAssetLoader.Instance;
            if (loader == null)
            {
                return;
            }

            var tailTexture = await loader.GetTextureAsync(_tailPath, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (tailTexture != null)
            {
                CreateTentacles(tailTexture);
            }
            else
            {
                ClearTentacles();
            }
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_clanId == 0)
            {
                return;
            }

            var loader = ClientAssetLoader.Instance;
            if (loader == null)
            {
                return;
            }

            var clanTexture = await loader.GetTextureAsync($"/clan/{_clanId}", token);
            if (token.IsCancellationRequested || clanTexture == null || _clanRenderer == null)
            {
                return;
            }

            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanRenderer.sprite = _clanSprite;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !RobotManager.ShowDebugVisuals) return;

            // Server Position: Red Square
            Utils.FodislopGizmos.DrawBounds(_serverPosition, Vector2.one * 1.0f, Color.red);

            // Client/Target Position: Blue Square
            Utils.FodislopGizmos.DrawBounds(_targetPosition, Vector2.one * 0.9f, Color.blue);

            // Visual Position: Cyan Square
            Utils.FodislopGizmos.DrawBounds(transform.position, Vector2.one * 0.8f, Color.cyan);

            // Draw Rotation Arrow
            float angleRad = (transform.eulerAngles.z + VISUAL_ROTATION_OFFSET) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0);
            Utils.FodislopGizmos.DrawArrow(transform.position, direction, Color.yellow, 1.2f);

            // Metadata Status
            string status = $"ID: {_botId}\n{(IsLocalPlayer ? "LOCAL PLAYER" : "REMOTE ROBOT")}\n" +
                            $"Meta: {(_isMetadataLoaded ? "OK" : "PENDING")}\n" +
                            $"Speed: {_moveSpeed:F1}";
            Utils.FodislopGizmos.DrawLabel(transform.position + Vector3.up * 1.5f, status, _isMetadataLoaded ? Color.green : Color.orange);

            if (!IsLocalPlayer)
            {
                // Draw line to server position if it's lagging
                float lag = Vector3.Distance(_serverPosition, transform.position);
                if (lag > 0.5f)
                {
                    Utils.FodislopGizmos.DrawDottedLine(transform.position, _serverPosition, Color.red, 4f);
                }
            }
        }
#endif

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            RobotManager.InstanceIfExists?.UnregisterRobot(_botId, this);

            if (_skinSprite != null) Object.Destroy(_skinSprite);
            if (_clanSprite != null) Object.Destroy(_clanSprite);
            ClearTentacles();
        }

        private class Tentacle
        {
            private readonly LineRenderer _line;
            private readonly Material _material;
            private readonly Vector3[] _positions;
            private readonly Vector3[] _velocities;
            private readonly float _wiggleOffset;
            private const int POINT_COUNT = 5; // root + 4 segments
            private const float SMOOTH_TIME = 0.08f;
            private const float MAX_SEGMENT_DIST = 0.2f;

            public Tentacle(GameObject container, Texture2D texture, float wiggleOffset, int sortingOrder, int sliceIndex, int totalSlices)
            {
                _wiggleOffset = wiggleOffset;
                _positions = new Vector3[POINT_COUNT];
                _velocities = new Vector3[POINT_COUNT];

                var go = new GameObject($"Tentacle_{wiggleOffset}");
                go.transform.SetParent(container.transform);
                _line = go.AddComponent<LineRenderer>();

                _material = new Material(Shader.Find("Sprites/Default"));
                _material.mainTexture = texture;

                // Apply texture slicing
                float sliceHeight = 1.0f / totalSlices;
                _material.mainTextureScale = new Vector2(1, sliceHeight);
                _material.mainTextureOffset = new Vector2(0, sliceIndex * sliceHeight);

                _line.material = _material;

                _line.startWidth = 0.15f;
                _line.endWidth = 0.02f;
                _line.positionCount = POINT_COUNT;
                _line.sortingOrder = sortingOrder;
                _line.textureMode = LineTextureMode.Stretch;

                // Set initial positions
                for (int i = 0; i < POINT_COUNT; i++)
                {
                    _positions[i] = container.transform.position;
                    _line.SetPosition(i, _positions[i]);
                }
            }

            public void Snap(Vector3 position)
            {
                for (int i = 0; i < POINT_COUNT; i++)
                {
                    _positions[i] = position;
                    _velocities[i] = Vector3.zero;
                    _line.SetPosition(i, position);
                }
            }

            public void Update(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
            {
                _positions[0] = rootPosition;
                _line.SetPosition(0, _positions[0]);

                Vector3 lastPos = rootPosition;
                float angleRad = rotationAngle * Mathf.Deg2Rad;
                Vector3 baseOffset = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0) * -0.2f * movementFactor;
                float spreadAngle = (rotationAngle + _wiggleOffset) * Mathf.Deg2Rad;
                baseOffset += new Vector3(Mathf.Cos(spreadAngle), Mathf.Sin(spreadAngle), 0) * 0.15f * movementFactor;

                Vector3 targetPos = rootPosition + baseOffset;

                for (int i = 1; i < POINT_COUNT; i++)
                {
                    // Spring movement
                    _positions[i] = Vector3.SmoothDamp(_positions[i], targetPos, ref _velocities[i], SMOOTH_TIME, 50f, deltaTime);

                    // Wiggle logic
                    float wiggle = Mathf.Sin(Time.time * 15f + i * 1.5f + _wiggleOffset) * 0.1f * movementFactor;
                    Vector3 direction = (_positions[i] - lastPos).normalized;
                    if (direction == Vector3.zero)
                    {
                        // Default to pointing backwards from the robot's rotation
                        direction = new Vector3(-Mathf.Cos(angleRad), -Mathf.Sin(angleRad), 0);
                    }
                    Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0);

                    _line.SetPosition(i, _positions[i] + perpendicular * wiggle);

                    // Set target for next segment (moving further along the chain)
                    lastPos = _positions[i];
                    targetPos = _positions[i] + direction * MAX_SEGMENT_DIST * movementFactor;
                }
            }

            public void Destroy()
            {
                if (_line != null) Object.Destroy(_line.gameObject);
                if (_material != null) Object.Destroy(_material);
            }
        }
    }
}
