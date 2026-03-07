using UnityEngine;
using Fodinae.Assets.Scripts;
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

        private bool _isMetadataLoaded = false;
        private CancellationTokenSource _cts;

        public ushort BotId => _botId;
        public int PlayerId => _playerId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;

        private void Awake()
        {
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            // If pre-configured (like in Player prefab), load skin immediately
            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadSkin();
            }
        }

        public void Initialize(ushort botId)
        {
            _botId = botId;
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
            transform.position = new Vector3(x + 0.5f, y + 0.5f, 0);
        }

        public void SetRotation(byte rotation)
        {
            // 0: Right (0), 1: Up (90), 2: Left (180), 3: Down (270)
            float angle = rotation switch
            {
                0 => 0f,
                1 => 90f,
                2 => 180f,
                3 => 270f,
                _ => 0f
            };
            transform.rotation = Quaternion.Euler(0, 0, angle);
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
