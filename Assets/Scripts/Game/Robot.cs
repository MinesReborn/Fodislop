using UnityEngine;
using Fodinae.Assets.Scripts;
using Fodinae.Assets.Scripts.Game.Managers;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Assets.Scripts.Game
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
        [SerializeField] private string _tailPath; [SerializeField] private float _rotationSpeed = 1080f;

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

        public uint BotId => _botId;
        public int PlayerId => _playerId;
        public byte ClanId => _clanId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;
        public bool IsLocalPlayer => gameObject.CompareTag("Player");

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

        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = value;
        }

        private void Awake()
        {
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            transform.localScale = Vector3.one;
            _targetPosition = transform.position;
            _serverPosition = transform.position;
            _smoothPosition = transform.position;
            _smoothAngle = transform.eulerAngles.z;

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }

            InitializeVisualElements();
        }

        private void InitializeVisualElements()
        {
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
                RobotManager.Instance.RegisterRobot(this);
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

            UpdateLabelsPosition();

            if (RobotManager.ShowDebugVisuals)
            {
                DrawDebugVisuals();
            }
        }

        private void DrawDebugVisuals()
        {
            // Server Position: Red (size 1.0)
            DrawDebugBox(_serverPosition, 1.0f, Color.red);

            // Client/Target Position: Blue (size 0.9)
            DrawDebugBox(_targetPosition, 0.9f, Color.blue);

            // Visual Position: Cyan (size 0.8)
            DrawDebugBox(transform.position, 0.8f, Color.cyan);
        }

        private void DrawDebugBox(Vector3 center, float size, Color color)
        {
            float halfSize = size * 0.5f;
            Vector3 topLeft = center + new Vector3(-halfSize, halfSize, 0);
            Vector3 topRight = center + new Vector3(halfSize, halfSize, 0);
            Vector3 bottomLeft = center + new Vector3(-halfSize, -halfSize, 0);
            Vector3 bottomRight = center + new Vector3(halfSize, -halfSize, 0);

            Debug.DrawLine(topLeft, topRight, color);
            Debug.DrawLine(topRight, bottomRight, color);
            Debug.DrawLine(bottomRight, bottomLeft, color);
            Debug.DrawLine(bottomLeft, topLeft, color);
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
            RobotManager.Instance.RegisterRobot(this);

            _isMetadataLoaded = false;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1, 1, 1, 0.5f);
            }

            if (_nicknameText != null) _nicknameText.text = "";
            if (_clanRenderer != null) _clanRenderer.sprite = null;
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
            float unityY = (MapManager.Instance.WorldHeight - 1 - y) + 0.5f;
            _serverPosition = new Vector3(x + 0.5f, unityY, 0);

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
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadMetadataAssetsAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid LoadMetadataAssetsAsync(CancellationToken token)
        {
            var skinTask = string.IsNullOrEmpty(_skinPath) ? UniTask.FromResult<Texture2D>(null) : ClientAssetLoader.Instance.GetTextureAsync(_skinPath, token);
            var tailTask = string.IsNullOrEmpty(_tailPath) ? UniTask.FromResult<Texture2D>(null) : ClientAssetLoader.Instance.GetTextureAsync(_tailPath, token);
            var clanTask = _clanId == 0 ? UniTask.FromResult<Texture2D>(null) : ClientAssetLoader.Instance.GetTextureAsync($"/clan/{_clanId}.png", token);

            var (skinTexture, tailTexture, clanTexture) = await UniTask.WhenAll(skinTask, tailTask, clanTask);

            if (token.IsCancellationRequested) return;

            if (skinTexture != null && _spriteRenderer != null)
            {
                var sprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
                _spriteRenderer.sprite = sprite;
            }

            if (clanTexture != null && _clanRenderer != null)
            {
                var sprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
                _clanRenderer.sprite = sprite;
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}