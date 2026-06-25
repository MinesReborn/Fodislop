using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
    /// Loads visual assets from the server asset pipeline,
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

        private void CreateGameObject()
        {
            var worldHeight = MapManager.Instance?.WorldHeight ?? 128;

            // Source position: sourceBot > packet X/Y
            Vector3 pos;
            string objLabel;

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

                objLabel = $"SFX_{_effectType}_srcBot{_sourceBotId}";
            }
            else
            {
                pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, worldHeight);
                objLabel = $"SFX_{_effectType}_{_sourceX}_{_sourceY}";
            }

            _gameObject = new GameObject(objLabel);
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

            _spriteRenderer = _gameObject.AddComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = -500;
            _spriteRenderer.color = _primaryColor;
        }

        private async UniTaskVoid LoadVisualAsync()
        {
            try
            {
                var filename = $"vfx/{_effectType.ToString().ToLowerInvariant()}";
                var loader = ClientAssetLoader.Instance;
                if (loader == null)
                {
                    Dispose();
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
                    Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SFXEffectInstance] Failed to load visual for {_effectType}: {ex.Message}");
                Dispose();
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
                    Dispose();
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

                // Remove SpriteRenderer since Effekseer renders independently
                UnityEngine.Object.Destroy(_spriteRenderer);
                _spriteRenderer = null;

                // Effekseer effects have their own lifetime; set generous fallback
                _maxLifetime = 10f;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SFXEffectInstance] Failed to load Effekseer effect: {ex.Message}");
                Dispose();
                return false;
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
                    _isDisposed = true;
                    CleanupGameObject();
                    return;
                }
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
