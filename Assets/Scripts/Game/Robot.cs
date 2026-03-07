using UnityEngine;
using Fodinae.Assets.Scripts;
using Fodinae.Assets.Scripts.Game.Managers;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Assets.Scripts.Game
{
    public class Robot : MonoBehaviour
    {
        [SerializeField] private ushort _botId;
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

        public ushort BotId => _botId;
        public int PlayerId => _playerId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;

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

            transform.localScale = new Vector3(0.5f, 0.5f, 1f);

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.freezeRotation = true;
            }
        }

        private void Start()
        {
            // If pre-configured (like in Player prefab), load skin immediately
            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadSkin();
            }
            _targetAngle = transform.eulerAngles.z;
            _targetPosition = transform.position;

            // Register this robot if it's the player (or has a pre-set botId)
            if (gameObject.CompareTag("Player"))
            {
                // Note: The player's botId might be set later, but for now we register with its current botId
                RobotManager.Instance.RegisterRobot(this);
            }
        }

        private void Update()
        {
            float currentAngle = transform.eulerAngles.z;
            if (!Mathf.Approximately(currentAngle, _targetAngle))
            {
                float newAngle = Mathf.MoveTowardsAngle(currentAngle, _targetAngle, _rotationSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0, 0, newAngle);
            }

            if (Vector3.Distance(transform.position, _targetPosition) > 0.001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, _targetPosition, _moveSpeed * Time.deltaTime);
            }
            else if (transform.position != _targetPosition)
            {
                transform.position = _targetPosition;
            }
        }

        public void Initialize(ushort botId)
        {
            // If we are updating the botId (e.g. for the player)
            if (_botId != botId && _botId != 0)
            {
                // Re-register with the manager under the new ID
                _botId = botId;
                RobotManager.Instance.RegisterRobot(this);
            }
            else
            {
                _botId = botId;
            }

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
            _targetPosition = new Vector3(x + 0.5f, y + 0.5f, 0);
        }

        public void SetRotation(byte rotation)
        {
            // 0: Right (0), 1: Up (90), 2: Left (180), 3: Down (270)
            TargetAngle = rotation switch
            {
                0 => 0f,
                1 => 90f,
                2 => 180f,
                3 => 270f,
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
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 16f);
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
