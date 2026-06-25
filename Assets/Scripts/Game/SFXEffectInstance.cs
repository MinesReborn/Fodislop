using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Utils;
using Fodinae.Scripts.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using Effekseer;

namespace Fodinae.Scripts.Game
{
    /// <summary>
    /// Manages a single server-driven SFX visual+audio effect.
    /// Created by SFXEffectManager in response to an SFXPacket.
    /// Loads visual and audio assets from the server asset pipeline,
    /// plays them at the world position, and auto-disposes on completion.
    /// </summary>
    public sealed class SFXEffectInstance : IDisposable
    {
        private readonly SFX _effectType;
        private readonly ushort _sourceX;
        private readonly ushort _sourceY;
        private readonly ushort _targetBotId;
        private readonly IReadOnlyList<StringPairPacket> _parameters;

        private GameObject _gameObject;
        private SpriteRenderer _spriteRenderer;
        private AudioSource _audioSource;

        // Parsed dynamic parameters from StringPairPacket list
        private Color _primaryColor = Color.white;
        private float _speed = 1f;

        // Sprite animation state
        private Sprite[] _animationFrames;
        private int _currentFrame;
        private float _frameTimer;
        private float _frameDuration = 0.1f;
        private bool _isAnimated;

        // Lifecycle
        private float _lifeTimer;
        private float _maxLifetime = 5f;
        private bool _isDisposed;

        // Cached world position computed from server coords — captured before parenting
        // to avoid async drift when the parent (e.g. Player) moves during asset loading.
        private Vector3 _intendedWorldPosition;

        private EffekseerHandle _effekseerHandle;
        private bool _hasEffekseerEffect;

        public SFXEffectInstance(SFXPacket packet)
        {
            _effectType = packet.EffectType;
            _sourceX = packet.X;
            _sourceY = packet.Y;
            _targetBotId = packet.TargetBotId;
            _parameters = packet.Parameters;

            ParseParameters();
            CreateGameObject();
            LoadVisualAsync().Forget();
            LoadAudioAsync().Forget();
        }

        public bool IsDisposed => _isDisposed;

        private void ParseParameters()
        {
            if (_parameters == null)
            {
                return;
            }

            foreach (var param in _parameters)
            {
                switch (param.Key)
                {
                    case "primary_color":
                        if (ColorUtility.TryParseHtmlString(param.Value, out var primary))
                        {
                            _primaryColor = primary;
                        }

                        break;
                    case "speed":
                        if (float.TryParse(
                                param.Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var speed)
                            && speed > 0.01f)
                        {
                            _speed = speed;
                        }

                        break;
                }
            }
        }

        private void CreateGameObject()
        {
            var worldHeight = MapManager.Instance?.WorldHeight ?? 128;
            var pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, worldHeight);

            _gameObject = new GameObject($"SFX_{_effectType}_{_sourceX}_{_sourceY}");
            _gameObject.transform.position = pos;
            _intendedWorldPosition = pos;

            // Attach to target bot if a specific robot is targeted
            if (_targetBotId != 0)
            {
                var bot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_targetBotId);
                if (bot != null)
                {
                    // Apply the bot's logical facing direction to sprite-based VFX (PNG/GIF/WebP)
                    // at creation time — once, not updated as the bot rotates.
                    // Uses LogicalFacingAngle (raw _targetAngle) rather than transform.rotation
                    // to avoid visual smoothing lag and random tremor.
                    // Effekseer effects are not affected: they use _intendedWorldPosition
                    // independently (see TryLoadEffekseer).
                    // VFX sprite faces DOWN at 0°, while robot sprite faces UP at 0°
                    // (due to VISUAL_ROTATION_OFFSET = -90). The +180 compensates for this 180° offset.
                    _gameObject.transform.rotation = Quaternion.Euler(0, 0, bot.LogicalFacingAngle + 180f);
                }
            }

            _spriteRenderer = _gameObject.AddComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = -500;
            _spriteRenderer.color = _primaryColor;
        }

        private async UniTaskVoid LoadVisualAsync()
        {
            try
            {
                var filename = $"vfx/{_effectType.ToString().ToLowerInvariant()}";
                var bytes = await ClientAssetLoader.Instance.GetAssetBytesAsync(filename, timeoutSeconds: 10);

                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogWarning($"[SFXEffectInstance] No visual data for {filename}");
                    Dispose();
                    return;
                }

                var containerType = AnimationContainerDecoder.DetectType(bytes);
                switch (containerType)
                {
                    case AnimationContainerDecoder.ContainerType.PNG:
                        LoadStaticSprite(bytes);
                        break;
                    case AnimationContainerDecoder.ContainerType.GIF:
                        LoadAnimatedSprite(bytes, AnimationContainerDecoder.ContainerType.GIF);
                        break;
                    case AnimationContainerDecoder.ContainerType.WebP:
                        LoadAnimatedSprite(bytes, AnimationContainerDecoder.ContainerType.WebP);
                        break;
                    default:
                        TryLoadEffekseer(bytes);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SFXEffectInstance] Failed to load visual for {_effectType}: {ex.Message}");
                Dispose();
            }
        }

        private void LoadStaticSprite(byte[] bytes)
        {
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
            {
                _spriteRenderer.sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    RenderingConstants.PixelsPerUnit);
                _maxLifetime = 1f;
            }
            else
            {
                Debug.LogWarning("[SFXEffectInstance] Failed to decode PNG texture");
                Dispose();
            }
        }

        private void LoadAnimatedSprite(byte[] bytes, AnimationContainerDecoder.ContainerType type)
        {
            try
            {
                AnimationContainerDecoder.DecodedAnimation anim;
                if (type == AnimationContainerDecoder.ContainerType.GIF)
                {
                    anim = AnimationContainerDecoder.DecodeGif(bytes);
                }
                else
                {
                    anim = AnimationContainerDecoder.DecodeWebP(bytes);
                }

                if (anim.Atlas == null || anim.FrameCount <= 0)
                {
                    Debug.LogWarning($"[SFXEffectInstance] Decoded {type} has no frames");
                    Dispose();
                }

                _animationFrames = AnimationContainerDecoder.Decode(
                    anim.Atlas, anim.Atlas.width, anim.FrameHeight, anim.FrameCount);

                if (_animationFrames == null || _animationFrames.Length == 0)
                {
                    Debug.LogWarning($"[SFXEffectInstance] Failed to slice {type} atlas into frames");
                    Dispose();
                }

                _currentFrame = 0;
                _frameDuration = 1f / Mathf.Max(1f, anim.FPS * _speed);
                _isAnimated = true;
                _spriteRenderer.sprite = _animationFrames[0];

                // Total animation duration plus a small buffer
                _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SFXEffectInstance] Failed to decode {type}: {ex.Message}");
                Dispose();
            }
        }

        private void TryLoadEffekseer(byte[] bytes)
        {
            try
            {
                var effectAsset = EffekseerSystem.LoadEffect(bytes, _effectType.ToString());
                if (effectAsset == null)
                {
                    Debug.LogWarning("[SFXEffectInstance] EffekseerSystem.LoadEffect returned null");
                    Dispose();
                    return;
                }

                _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, _intendedWorldPosition);
                Debug.Log($"[SFX] Effect handle=NONE(inaccessible field), exists={_effekseerHandle.exists}, intendedPos={_intendedWorldPosition}");
                _effekseerHandle.SetDynamicInput(0, _speed);
                _effekseerHandle.SetDynamicInput(1, _primaryColor.r);
                _effekseerHandle.SetDynamicInput(2, _primaryColor.g);
                _effekseerHandle.SetDynamicInput(3, _primaryColor.b);
                _hasEffekseerEffect = true;

                Debug.Log($"[SFX] worldPos={_gameObject.transform.position}, " +
                  $"localPos={_gameObject.transform.localPosition}, " +
                  $"parent={_gameObject.transform.parent?.name ?? "none"}");

                // Remove SpriteRenderer since Effekseer renders independently
                UnityEngine.Object.Destroy(_spriteRenderer);
                _spriteRenderer = null;

                // Effekseer effects have their own lifetime; set generous fallback
                _maxLifetime = 10f;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SFXEffectInstance] Failed to load Effekseer effect: {ex.Message}");
                Dispose();
            }
        }

        private async UniTaskVoid LoadAudioAsync()
        {
            try
            {
                var filename = $"audio/{_effectType.ToString().ToLowerInvariant()}";
                var bytes = await ClientAssetLoader.Instance.GetAssetBytesAsync(filename, timeoutSeconds: 10);

                if (bytes == null || bytes.Length == 0)
                {
                    // No dedicated audio for this SFX variant — audio-only effects
                    // still play through the existing AudioManager path
                    return;
                }

                var clip = WavUtility.ToAudioClip(bytes, $"SFX_{_effectType}");
                if (clip == null)
                {
                    return;
                }

                _audioSource = _gameObject.AddComponent<AudioSource>();
                _audioSource.clip = clip;
                _audioSource.spatialBlend = 1f; // Full 3D spatial
                _audioSource.volume = AudioManager.Instance?.SfxVolume ?? 1f;
                _audioSource.Play();

                // Keep the effect alive long enough for audio to finish
                float audioDuration = (float)clip.samples / clip.frequency;
                _maxLifetime = Mathf.Max(_maxLifetime, audioDuration + 1f);
            }
            catch
            {
                // Audio loading failure is non-fatal — visual still plays
            }
        }

        public void Update()
        {
            if (_isDisposed)
            {
                return;
            }

            _lifeTimer += Time.deltaTime;

            // Advance sprite animation frame if applicable
            if (_isAnimated && _animationFrames != null && _animationFrames.Length > 0)
            {
                _frameTimer += Time.deltaTime;
                while (_frameTimer >= _frameDuration && _currentFrame < _animationFrames.Length)
                {
                    _frameTimer -= _frameDuration;
                    _currentFrame++;
                }

                if (_currentFrame < _animationFrames.Length)
                {
                    _spriteRenderer.sprite = _animationFrames[_currentFrame];
                }
            }

            // Check if Effekseer effect has finished
            if (_hasEffekseerEffect && !_effekseerHandle.exists)
            {
                _isDisposed = true;
                CleanupGameObject();
                return;
            }

            // Enforce max lifetime as a safety net
            if (_lifeTimer >= _maxLifetime)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_hasEffekseerEffect)
            {
                _effekseerHandle.Stop();
            }

            CleanupGameObject();
        }

        private void CleanupGameObject()
        {
            if (_gameObject != null)
            {
                if (_audioSource != null)
                {
                    _audioSource.Stop();
                }

                UnityEngine.Object.Destroy(_gameObject);
                _gameObject = null;
                _spriteRenderer = null;
                _audioSource = null;
            }
        }
    }
}
