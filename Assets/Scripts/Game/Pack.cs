using UnityEngine;
using MinesServer.Data;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.Game
{
    public class Pack : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;
        private SpriteRenderer _clanRenderer;
        private PackType _packType;
        private byte _variant;
        private byte _linkedClan;
        private CancellationTokenSource _cts;
        private Sprite _packSprite;
        private Sprite _clanSprite;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
            {
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            clanGo.transform.localPosition = new Vector3(0.6f, -0.5f, 0);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            _clanRenderer.sortingOrder = 10; // Ensure it's on top of the pack
        }

        public void Initialize(PackType packType, byte variant, byte linkedClan)
        {
            _packType = packType;
            _variant = variant;
            _linkedClan = linkedClan;

            LoadAssets();
        }

        private void LoadAssets()
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadAssetsAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid LoadAssetsAsync(CancellationToken token)
        {
            LoadPackAsync(token).Forget();
            LoadClanAsync(token).Forget();
            await UniTask.CompletedTask;
        }

        private async UniTaskVoid LoadPackAsync(CancellationToken token)
        {
            string packName = _packType.ToString().ToLowerInvariant();
            string packPath = $"pack/{packName}/{_variant}.png";

            var packTexture = await ClientAssetLoader.Instance.GetTextureAsync(packPath, token);
            if (token.IsCancellationRequested || packTexture == null || _spriteRenderer == null) return;

            if (_packSprite != null) Destroy(_packSprite);
            // 32 pixels per unit as requested
            _packSprite = Sprite.Create(packTexture, new Rect(0, 0, packTexture.width, packTexture.height), new Vector2(0.5f, 0.5f), 16);
            _spriteRenderer.sprite = _packSprite;

            UpdateClanPosition();
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_linkedClan == 0)
            {
                if (_clanRenderer != null) _clanRenderer.sprite = null;
                return;
            }

            var clanTexture = await ClientAssetLoader.Instance.GetTextureAsync($"clan/{_linkedClan}.png", token);
            if (token.IsCancellationRequested || clanTexture == null || _clanRenderer == null) return;

            if (_clanSprite != null) Destroy(_clanSprite);
            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanRenderer.sprite = _clanSprite;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;

            UpdateClanPosition();
        }

        private void UpdateClanPosition()
        {
            if (_clanRenderer == null) return;

            // Position to the right and slightly below the center
            float packWidth = _packSprite != null ? _packSprite.texture.width : 16;
            float xOffset = (packWidth / 32f) + 0.1f; // Right edge + 0.1 gap
            _clanRenderer.transform.localPosition = new Vector3(xOffset, -0.5f, 0);
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            if (_packSprite != null) Destroy(_packSprite);
            if (_clanSprite != null) Destroy(_clanSprite);
        }
    }
}
