using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Fodinae.Scripts.Effekseer;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game
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

        private EffekseerHandle _effekseerHandle;
        private EffekseerEffectAsset _effekseerAsset;
        private bool _hasEffekseerEffect;

        protected void Awake()
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
            // Clean up previous Effekseer effect if any
            StopEffekseerEffect();

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
            string packName = _packType.ToString();
            string packPath = $"Pack/{packName}/{_variant}";

            // 1. Try loading as a texture (existing behavior — static or animated sprite)
            var packTexture = await ClientAssetLoader.Instance.GetTextureAsync(packPath, token);
            if (token.IsCancellationRequested || _spriteRenderer == null)
            {
                return;
            }

            if (packTexture != null)
            {
                if (_packSprite != null)
                {
                    Destroy(_packSprite);
                }

                // Use central PIXELS_PER_UNIT for consistency
                _packSprite = Sprite.Create(packTexture, new Rect(0, 0, packTexture.width, packTexture.height), new Vector2(0.5f, 0.5f), RenderingConstants.PIXELS_PER_UNIT);
                _spriteRenderer.sprite = _packSprite;

                UpdateClanPosition();
                return;
            }

            // 2. Texture not found — try loading as Effekseer effect (.efk data)
            var efkBytes = await ClientAssetLoader.Instance.GetAssetBytesAsync(packPath, timeoutSeconds: 10);
            if (token.IsCancellationRequested || efkBytes == null || efkBytes.Length < 4)
            {
                return;
            }

            // Verify EFKE header
            if (efkBytes[0] != 'E' || efkBytes[1] != 'F' || efkBytes[2] != 'K' || efkBytes[3] != 'E')
            {
                Debug.LogWarning($"[Pack] File at '{packPath}' is not a valid Effekseer effect (no EFKE header)");
                return;
            }

            var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                efkBytes,
                $"Pack_{packName}_{_variant}",
                texturePathMapper: path => $"{packPath}/{path}",
                textureTimeoutSeconds: 10);

            if (token.IsCancellationRequested || effectAsset == null)
            {
                return;
            }

            _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, transform.position);
            _hasEffekseerEffect = true;
            _effekseerAsset = effectAsset;

            // Hide sprite renderer — the Effekseer effect handles visuals
            if (_spriteRenderer != null)
            {
                _spriteRenderer.enabled = false;
            }

            Debug.Log($"[Pack] Playing Effekseer effect for pack '{packName}' variant {_variant} at {transform.position}");
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_linkedClan == 0)
            {
                if (_clanRenderer != null)
                {
                    _clanRenderer.sprite = null;
                }

                return;
            }

            var clanTexture = await ClientAssetLoader.Instance.GetTextureAsync($"Clan/{_linkedClan}", token);
            if (token.IsCancellationRequested || clanTexture == null || _clanRenderer == null)
            {
                return;
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanRenderer.sprite = _clanSprite;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;

            UpdateClanPosition();
        }

        private void UpdateClanPosition()
        {
            if (_clanRenderer == null)
            {
                return;
            }

            // Position to the right and slightly below the center
            float packWidth = _packSprite != null ? _packSprite.texture.width : RenderingConstants.PIXELS_PER_UNIT;
            float xOffset = (packWidth / (RenderingConstants.PIXELS_PER_UNIT * 2)) + 0.1f; // Right edge + 0.1 gap
            _clanRenderer.transform.localPosition = new Vector3(xOffset, -0.5f, 0);
        }

        protected void Update()
        {
            if (_hasEffekseerEffect && !_effekseerHandle.exists)
            {
                // Effect has finished playing — clean up
                _hasEffekseerEffect = false;
                _effekseerAsset = null;

                if (_spriteRenderer != null)
                {
                    _spriteRenderer.enabled = true;
                }
            }
        }

        private void StopEffekseerEffect()
        {
            if (_hasEffekseerEffect)
            {
                _effekseerHandle.Stop();
                _hasEffekseerEffect = false;
                _effekseerAsset = null;
            }
        }

        protected void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            StopEffekseerEffect();

            if (_packSprite != null)
            {
                Destroy(_packSprite);
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }
        }
    }
}
