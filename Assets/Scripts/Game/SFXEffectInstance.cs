using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Utils;
using Fodinae.Scripts.Effekseer;
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
    /// Acquires a <see cref="SFXPool.PooledSlot"/> from <see cref="SFXPool"/>,
    /// loads visual assets from the server asset pipeline,
    /// plays them at the world position, and returns the slot to the pool
    /// when both audio and visual have completed.
    /// </summary>
    public sealed class SFXEffectInstance : IDisposable
    {
        private readonly SFX _effectType;
        private readonly ushort _sourceX;
        private readonly ushort _sourceY;
        private readonly ushort _targetBotId;
        private readonly IReadOnlyList<StringPairPacket> _parameters;

        // Pooled slot providing GameObject, AudioSource, and SpriteRenderer
        private SFXPool.PooledSlot _slot;
        private GameObject _gameObject;
        private SpriteRenderer _spriteRenderer;
        private AudioSource _audioSource;

        // Parsed dynamic parameters from StringPairPacket list
        private Color _primaryColor = Color.white;
        private float _speed = 1f;

        // Source tracking for Effekseer anchoring
        private uint _sourceBotId;
        private bool _hasSourceBot;

        // Attractor target fallback — from dynamic x/y params (used when TargetBotId == 0)
        private ushort _attractorX;
        private ushort _attractorY;
        private bool _hasAttractorPosition;

        // Ordinal Effekseer dynamic input floats from "props" parameter
        private float[] _effekseerDynamicInputs;

        // Sprite animation state
        private Sprite[] _animationFrames;
        private int _currentFrame;
        private float _frameTimer;
        private float _frameDuration = 0.1f;
        private bool _isAnimated;

        // Lifecycle
        private float _lifeTimer;
        private float _maxLifetime = 5f;
        private bool _visualCompleted;
        private bool _audioCompleted;
        private bool _slotReleased;
        private bool _isDisposed;

        // Cached world position computed from server coords — captured before parenting
        // to avoid async drift when the parent (e.g. Player) moves during asset loading.
        private Vector3 _intendedWorldPosition;

        private EffekseerHandle _effekseerHandle;
        private bool _hasEffekseerEffect;

        public SFXEffectInstance(SFXPacket packet, SFXPool.PooledSlot slot)
        {
            _effectType = packet.EffectType;
            _sourceX = packet.X;
            _sourceY = packet.Y;
            _targetBotId = packet.TargetBotId;
            _parameters = packet.Parameters;
            _slot = slot;

            _gameObject = slot.GameObject;
            _spriteRenderer = slot.SpriteRenderer;
            _audioSource = slot.AudioSource;

            ParseParameters();
            SetupSlotPosition();
            PlayAudio();
            LoadVisualAsync().Forget();
        }

        /// <summary>
        /// True when both audio and visual have completed and the pooled slot
        /// has been returned. The manager removes this instance when true.
        /// </summary>
        public bool IsDisposed => _slotReleased;

        private void ParseParameters()
        {
            if (_parameters == null)
            {
                return;
            }

            foreach (var param in _parameters)
            {
                switch (param.Key.ToLowerInvariant())
                {
                    case "sourcebotid":
                        if (uint.TryParse(param.Value, out var srcBotId))
                        {
                            _sourceBotId = srcBotId;
                            _hasSourceBot = true;
                        }

                        break;
                    case "x":
                        if (ushort.TryParse(param.Value, out var attractorX))
                        {
                            _attractorX = attractorX;
                            _hasAttractorPosition = true;
                        }

                        break;
                    case "y":
                        if (ushort.TryParse(param.Value, out var attractorY))
                        {
                            _attractorY = attractorY;
                            _hasAttractorPosition = true;
                        }

                        break;
                    case "props":
                        if (!string.IsNullOrEmpty(param.Value))
                        {
                            var parts = param.Value.Split(',');
                            _effekseerDynamicInputs = new float[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (float.TryParse(
                                        parts[i],
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var propVal))
                                {
                                    _effekseerDynamicInputs[i] = propVal;
                                }
                            }
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// Position the pooled GameObject using the same logic as the
        /// original CreateGameObject — source bot priority, server coords,
        /// target-bot facing rotation.
        /// </summary>
        private void SetupSlotPosition()
        {
            var worldHeight = MapManager.Instance?.WorldHeight ?? 128;

            Vector3 pos;

            if (_hasSourceBot)
            {
                var sourceBot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_sourceBotId);
                if (sourceBot != null)
                {
                    pos = sourceBot.transform.position;
                }
                else
                {
                    pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, worldHeight);
                }
            }
            else
            {
                pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, worldHeight);
            }

            _gameObject.transform.position = pos;
            _intendedWorldPosition = pos;

            // Apply the target bot's logical facing direction to sprite-based VFX (PNG/GIF/WebP)
            // at creation time — once, not updated as the bot rotates.
            // Uses LogicalFacingAngle (raw _targetAngle) rather than transform.rotation
            // to avoid visual smoothing lag and random tremor.
            // Effekseer effects handle source/attractor tracking independently in Update().
            // VFX sprite faces DOWN at 0°, while robot sprite faces UP at 0°
            // (due to VISUAL_ROTATION_OFFSET = -90). The +180 compensates for this 180° offset.
            if (_targetBotId != 0)
            {
                var targetBot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_targetBotId);
                if (targetBot != null)
                {
                    _gameObject.transform.rotation = Quaternion.Euler(0, 0, targetBot.LogicalFacingAngle + 180f);
                }
            }

            _spriteRenderer.sortingOrder = -500;
            _spriteRenderer.color = _primaryColor;
            _spriteRenderer.sprite = null;
        }

        /// <summary>
        /// Start audio playback through the pooled slot's AudioSource.
        /// The pool pre-sets the clip if it was loaded; otherwise audio is skipped.
        /// </summary>
        private void PlayAudio()
        {
            if (_audioSource != null && _audioSource.clip != null)
            {
                _audioSource.volume = AudioManager.Instance?.SfxVolume ?? 1f;
                _audioSource.time = 0f;
                _audioSource.Play();
            }
        }

        private async UniTaskVoid LoadVisualAsync()
        {
            try
            {
                var filename = $"vfx/{_effectType.ToString().ToLowerInvariant()}";
                var loader = ClientAssetLoader.Instance;
                if (loader == null)
                {
                    MarkVisualCompleted();
                    return;
                }

                // Try animated sprites first (cached GIF/WebP decode via AssetCache)
                var animData = await loader.GetAnimatedSpritesAsync(filename, timeoutSeconds: 10);
                if (animData.Frames != null && animData.Frames.Length > 0)
                {
                    _animationFrames = animData.Frames;
                    _currentFrame = 0;
                    _frameDuration = animData.FrameDuration / Mathf.Max(0.01f, _speed);
                    _isAnimated = true;
                    _spriteRenderer.sprite = _animationFrames[0];
                    _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
                    return;
                }

                // No animated sprites — try PNG still frame via texture cache
                var texture = await loader.GetTextureAsync(filename);
                if (texture != null)
                {
                    _spriteRenderer.sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        RenderingConstants.PixelsPerUnit);
                    _maxLifetime = 1f;
                    return;
                }

                // Neither sprites nor texture — try Effekseer
                var bytes = await loader.GetAssetBytesAsync(filename, timeoutSeconds: 10);
                if (bytes != null && bytes.Length > 0)
                {
                    await TryLoadEffekseerAsync(bytes);
                }
                else
                {
                    Debug.LogWarning($"[SFXEffectInstance] No visual data for {filename}");
                    MarkVisualCompleted();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SFXEffectInstance] Failed to load visual for {_effectType}: {ex.Message}");
                MarkVisualCompleted();
            }
        }

        private async UniTask<bool> TryLoadEffekseerAsync(byte[] bytes)
        {
            try
            {
                // Load effect with textures from the server asset pipeline.
                // Texture paths inside the .efk are used as-is for server requests.
                // Override with a texturePathMapper if you need path remapping:
                //   path => "vfx/textures/" + Path.GetFileName(path)
                var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                    bytes,
                    _effectType.ToString(),
                    texturePathMapper: null,
                    textureTimeoutSeconds: 10);

                if (effectAsset == null)
                {
                    Debug.LogWarning("[SFXEffectInstance] RuntimeEffekseerLoader.LoadEffectAsync returned null");
                    MarkVisualCompleted();
                    return false;
                }

                _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, _intendedWorldPosition);
                Debug.Log($"[SFX] Effect handle exists={_effekseerHandle.exists}, intendedPos={_intendedWorldPosition}");

                // Apply ordinal Effekseer dynamic inputs from "props" parameter
                // Format: comma-separated float values, each maps to a dynamic input index
                if (_effekseerDynamicInputs != null)
                {
                    for (int i = 0; i < _effekseerDynamicInputs.Length; i++)
                    {
                        _effekseerHandle.SetDynamicInput(i, _effekseerDynamicInputs[i]);
                    }
                }

                // Set attractor target position — priority: TargetBotId > source_x/source_y (dynamic params) > none
                if (_targetBotId != 0)
                {
                    var targetBot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_targetBotId);
                    if (targetBot != null)
                    {
                        _effekseerHandle.SetTargetLocation(targetBot.transform.position);
                    }
                }
                else if (_hasAttractorPosition)
                {
                    var worldHeight = MapManager.Instance?.WorldHeight ?? 128;
                    var attractorPos = CoordinateUtils.ServerToUnityPos(_attractorX, _attractorY, worldHeight);
                    _effekseerHandle.SetTargetLocation(attractorPos);
                }

                _hasEffekseerEffect = true;

                Debug.Log($"[SFX] worldPos={_gameObject.transform.position}, " +
                  $"localPos={_gameObject.transform.localPosition}, " +
                  $"parent={_gameObject.transform.parent?.name ?? "none"}");

                // Disable SpriteRenderer since Effekseer renders independently;
                // don't destroy it because it belongs to the pooled slot.
                _spriteRenderer.enabled = false;

                // Effekseer effects have their own lifetime; set generous fallback
                _maxLifetime = 10f;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SFXEffectInstance] Failed to load Effekseer effect: {ex.Message}");
                MarkVisualCompleted();
                return false;
            }
        }

        public void Update()
        {
            if (_slotReleased)
            {
                return;
            }

            _lifeTimer += Time.deltaTime;

            // ── Visual update ────────────────────────────────────────────────

            // Advance sprite animation frame if applicable
            if (!_visualCompleted && _isAnimated && _animationFrames != null && _animationFrames.Length > 0)
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
                else
                {
                    // Animation finished
                    _visualCompleted = true;
                }
            }

            // Update Effekseer effect: track source and attractor bot positions every frame
            if (_hasEffekseerEffect)
            {
                // Update source position to follow the source bot
                if (_hasSourceBot)
                {
                    var sourceBot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_sourceBotId);
                    if (sourceBot != null)
                    {
                        _effekseerHandle.SetLocation(sourceBot.transform.position);
                    }
                }

                // Update attractor target position to follow the target bot
                if (_targetBotId != 0)
                {
                    var targetBot = RobotManager.InstanceIfExists?.GetOrCreateRobot(_targetBotId);
                    if (targetBot != null)
                    {
                        _effekseerHandle.SetTargetLocation(targetBot.transform.position);
                    }
                }

                // Check if Effekseer effect has finished
                if (!_effekseerHandle.exists)
                {
                    _visualCompleted = true;
                }
            }

            // Check visual completion via max lifetime (for static sprites, timed effects)
            if (!_hasEffekseerEffect && !_isAnimated && _lifeTimer >= _maxLifetime)
            {
                _visualCompleted = true;
            }

            // ── Audio completion check ───────────────────────────────────────
            if (!_audioCompleted && _audioSource != null && _audioSource.clip != null)
            {
                if (!_audioSource.isPlaying && _lifeTimer > 0.1f)
                {
                    _audioCompleted = true;
                }
                else if (_lifeTimer >= _audioSource.clip.length + 0.1f)
                {
                    _audioCompleted = true;
                }
            }
            else if (!_audioCompleted && (_audioSource == null || _audioSource.clip == null))
            {
                // No audio asset — mark completed immediately
                _audioCompleted = true;
            }

            // ── Release slot when both complete ──────────────────────────────
            if (_visualCompleted && _audioCompleted)
            {
                ReleaseSlot();
                return;
            }

            // Safety net: force-release after generous timeout
            if (_lifeTimer >= Mathf.Max(_maxLifetime + 5f, 30f))
            {
                ReleaseSlot();
            }
        }

        /// <summary>
        /// Mark the visual component as completed (e.g. on load failure).
        /// Does NOT release the slot — waits for audio to finish too.
        /// </summary>
        private void MarkVisualCompleted()
        {
            if (_visualCompleted)
            {
                return;
            }

            _visualCompleted = true;

            if (_hasEffekseerEffect)
            {
                _effekseerHandle.Stop();
            }
        }

        /// <summary>
        /// Return the pooled slot to <see cref="SFXPool"/>.
        /// After this call the instance is fully disposed and the manager
        /// will remove it.
        /// </summary>
        private void ReleaseSlot()
        {
            if (_slotReleased)
            {
                return;
            }

            _slotReleased = true;

            if (_hasEffekseerEffect)
            {
                _effekseerHandle.Stop();
            }

            // Stop audio and return slot to pool
            if (_slot != null)
            {
                SFXPool.Instance?.Release(_slot);
                _slot = null;
            }

            _gameObject = null;
            _spriteRenderer = null;
            _audioSource = null;
        }

        /// <summary>
        /// Force-dispose the effect immediately. Marks visual as complete
        /// and releases the slot regardless of audio state.
        /// Called by <see cref="SFXEffectManager.ClearAllEffects"/>.
        /// </summary>
        public void Dispose()
        {
            if (_slotReleased)
            {
                return;
            }

            MarkVisualCompleted();

            // Don't wait for audio — force release
            _audioCompleted = true;
            ReleaseSlot();
        }
    }
}
