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
        [SerializeField] private SpriteRenderer _spriteRenderer;
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
                LoadSkin();
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
        }

        public void SetMetadata(int playerId, string nickname, string skinPath, string tailPath)
        {
            _playerId = playerId;
            _nickname = nickname;
            _skinPath = skinPath;
            _tailPath = tailPath;
            _isMetadataLoaded = true;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            LoadSkin();
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

        private void LoadSkin()
        {
            if (string.IsNullOrEmpty(_skinPath)) return;

            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadSkinAsync(_skinPath, _cts.Token).Forget();
        }

        private async UniTaskVoid LoadSkinAsync(string skinPath, CancellationToken token)
        {
            var texture = await ClientAssetLoader.Instance.GetTextureAsync(skinPath, token);
            if (token.IsCancellationRequested || texture == null) return;

            if (_spriteRenderer != null)
            {
                // Create sprite with center pivot and PPU matching texture width to occupy exactly 1x1 units
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
                _spriteRenderer.sprite = sprite;
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
