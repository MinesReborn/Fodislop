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
        [SerializeField] private string _tailPath;
        [SerializeField] private float _rotationSpeed = 1080f;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private bool _isMetadataLoaded = false;
        private CancellationTokenSource _cts;
        private float _targetAngle = 0f;
        private Vector3 _targetPosition;
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

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }

            InitializeVisualElements();
        }

        private void InitializeVisualElements()
        {
            // Create Nickname TextMesh
            var textGo = new GameObject("Nickname");
            textGo.transform.SetParent(transform);
            _nicknameText = textGo.AddComponent<TextMesh>();
            _nicknameText.anchor = TextAnchor.MiddleLeft;
            _nicknameText.alignment = TextAlignment.Left;
            _nicknameText.fontSize = 64;
            _nicknameText.characterSize = 0.1f;
            _nicknameText.color = Color.white;

            // Ensure text is rendered on top
            var textRenderer = textGo.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 100;

            // Create Clan Icon SpriteRenderer
            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            _clanRenderer.sortingOrder = 100;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;
        }

        private void Start()
        {
            // Snap to grid center on start to prevent misalignment
            Vector3 snappedPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z
            );
            transform.position = snappedPos;
            _targetPosition = snappedPos;

            // If pre-configured (like in Player prefab), load skin immediately
            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadMetadataAssets();
            }
            _targetAngle = transform.eulerAngles.z;

            // Register this robot if it's the player (or has a pre-set botId)
            if (gameObject.CompareTag("Player"))
            {
                // Note: The player's botId might be set later, but for now we register with its current botId
                RobotManager.Instance.RegisterRobot(this);
            }
        }

        private void Update()
        {
            Vector3 position = transform.position;
            float renderDistance = Vector2.Distance(position, _targetPosition);
            float num = 0.9f - renderDistance * 0.01f;
            if (num < 0.5f)
            {
                num = 0.5f;
            }

            if (renderDistance > 28f)
            {
                position = _targetPosition;
            }
            else
            {
                float num2 = 1f - num;
                position.x = num * position.x + num2 * _targetPosition.x;
                position.y = num * position.y + num2 * _targetPosition.y;
            }

            if (_tremor > 0.01f)
            {
                _tremor *= 0.8f;
                position.x += _tremor * (Random.value - 0.5f);
                position.y += _tremor * (Random.value - 0.5f);
            }
            transform.position = position;

            float currentAngle = transform.eulerAngles.z;
            float targetAngle = _targetAngle;

            if (currentAngle - targetAngle > 180f)
            {
                targetAngle += 360f;
            }
            if (currentAngle - targetAngle < -180f)
            {
                targetAngle -= 360f;
            }

            float num4 = 12f * Time.unscaledDeltaTime;
            if (num4 > 1f) num4 = 1f;

            float nowRotationAngle = (1f - num4) * currentAngle + num4 * targetAngle;

            if (_skinPath != "1")
            {
                nowRotationAngle += 6.6f * renderDistance * (0.5f - Random.value);
            }

            transform.rotation = Quaternion.Euler(0, 0, nowRotationAngle);

            UpdateLabelsPosition();
        }

        private void UpdateLabelsPosition()
        {
            if (_nicknameText != null)
            {
                // Position: to the right of the body, slightly higher than center
                _nicknameText.transform.position = transform.position + new Vector3(0.6f, 0.5f, 0);
                _nicknameText.transform.rotation = Quaternion.identity;
            }

            if (_clanRenderer != null)
            {
                // Position: to the right of the body, slightly lower than center
                _clanRenderer.transform.position = transform.position + new Vector3(0.6f, -0.5f, 0);
                _clanRenderer.transform.rotation = Quaternion.identity;
            }
        }

        public void Initialize(uint botId)
        {
            _botId = botId;
            RobotManager.Instance.RegisterRobot(this);

            _isMetadataLoaded = false;
            // Set to a "loading" or default state if needed
            // Maybe dim the sprite or show a placeholder
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
            // Align to 1.0 unit grid (centers)
            // Invert Y: Server Y 0 is at the top, Unity Y increases upwards
            float unityY = (MapManager.Instance.WorldHeight - 1 - y) + 0.5f;
            _targetPosition = new Vector3(x + 0.5f, unityY, 0);
        }

        public void SetRotation(byte rotation)
        {
            // 0: Down (270), 1: Left (180), 2: Up (90), 3: Right (0)
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
