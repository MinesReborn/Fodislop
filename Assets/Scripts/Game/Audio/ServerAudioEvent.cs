#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Fodinae.Audio.Backend;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Effekseer;
using Fodinae.Game.Managers;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace Fodinae.Game
{
    /// <summary>
    /// Единый контроллер эффекта мира (SFX/VFX).
    /// Запускает FMOD Studio 3D пространственный звук и визуальное представление (Effekseer / Спрайты)
    /// с поддержкой безопасного отмена асинхронных загрузок через CancellationToken.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully handle any dynamic asset load/play errors.")]
    public sealed class ServerAudioEvent : IDisposable
    {
        private readonly SFX _effectType;
        private readonly ushort _sourceX;
        private readonly ushort _sourceY;
        private readonly ushort _targetBotId;
        private readonly IReadOnlyList<StringPairPacket> _parameters;
        private readonly IRobotService _robotService;
        private readonly IAudioSystem _audioSystem;
        private readonly IAssetLoader _assetLoader;
        private readonly MapManager _mapManager;
        private readonly IVFXService _vfxPool;

        private IVFXSlot? _slot;
        private GameObject? _gameObject;

        private Color _primaryColor = Color.white;
        private float _speed = 1f;

        private uint _sourceBotId;
        private bool _hasSourceBot;

        private ushort _attractorX;
        private ushort _attractorY;
        private bool _hasAttractorPosition;

        private Dictionary<string, string>? _textureOverrideMap;

        private float[]? _effekseerDynamicInputs;

        private Sprite[]? _animationFrames;
        private Sprite? _ownedStaticSprite;
        private int _currentFrame;
        private float _frameTimer;
        private float _frameDuration = 0.1f;
        private bool _isAnimated;

        private float _lifeTimer;
        private float _maxLifetime = 5f;
        private bool _visualCompleted;
        private bool _slotReleased;
        private bool _isDisposed;

        private Vector3 _intendedWorldPosition;

        private EffekseerHandle _effekseerHandle;
        private EffekseerEffectAsset? _effekseerAsset;
        private bool _hasEffekseerEffect;

        private CancellationTokenSource? _cts;

        public ServerAudioEvent(
            AudioPacket packet,
            IVFXSlot? slot,
            IRobotService robotService,
            IAudioSystem audioSystem,
            IAssetLoader assetLoader,
            MapManager mapManager,
            IVFXService vfxPool,
            IAsyncOperationSupervisor operations)
        {
            _effectType = packet.EffectType;
            _sourceX = packet.X;
            _sourceY = packet.Y;
            _targetBotId = packet.TargetBotId;
            _parameters = packet.Parameters;
            _slot = slot;
            _robotService = robotService;
            _audioSystem = audioSystem;
            _assetLoader = assetLoader;
            _mapManager = mapManager;
            _vfxPool = vfxPool;

            if (slot != null)
            {
                _gameObject = slot.GameObject;
            }

            ParseParameters();
            SetupSlotPosition();
            PlayAudio();

            if (slot != null)
            {
                _cts = new CancellationTokenSource();
                CancellationToken eventToken = _cts.Token;
                operations.Run(
                    "load_server_audio_visual",
                    supervisorToken => LoadVisualWithCancellationAsync(
                        eventToken,
                        supervisorToken));
            }
            else
            {
                _visualCompleted = true;
            }
        }

        public bool IsDisposed => _slotReleased;

        public void Update()
        {
            if (_slotReleased)
            {
                return;
            }

            _lifeTimer += Time.deltaTime;

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
                    _slot?.SetSprite(_animationFrames[_currentFrame]);
                    _slot?.SetEnabled(true);
                }
                else
                {
                    _visualCompleted = true;
                }
            }

            if (_hasEffekseerEffect)
            {
                if (_hasSourceBot)
                {
                    var sourceBot = _robotService.GetOrCreateRobot(_sourceBotId);
                    if (sourceBot != null)
                    {
                        _effekseerHandle.SetLocation(sourceBot.transform.position);
                    }
                }

                if (_targetBotId != 0)
                {
                    var targetBot = _robotService.GetOrCreateRobot(_targetBotId);
                    if (targetBot != null)
                    {
                        _effekseerHandle.SetTargetLocation(targetBot.transform.position);
                    }
                }

                if (!_effekseerHandle.exists)
                {
                    _visualCompleted = true;
                }
            }

            if (!_hasEffekseerEffect && !_isAnimated && _lifeTimer >= _maxLifetime)
            {
                _visualCompleted = true;
            }

            if (_visualCompleted)
            {
                ReleaseSlot();
                return;
            }

            if (_lifeTimer >= Mathf.Max(_maxLifetime + 5f, 30f))
            {
                ReleaseSlot();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _cts?.Cancel();
            _cts?.Dispose();

            MarkVisualCompleted();
            ReleaseSlot();
        }

        private static readonly Dictionary<SFX, string> SfxEventNameCache = new();

        private static string GetSfxEventName(SFX sfx)
        {
            if (SfxEventNameCache.TryGetValue(sfx, out var cachedName))
            {
                return cachedName;
            }

            var name = sfx.ToString();
            var sb = new System.Text.StringBuilder("sfx/");
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        sb.Append('_');
                    }

                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            var result = sb.ToString();
            SfxEventNameCache[sfx] = result;
            return result;
        }

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
                    case "map":
                        if (!string.IsNullOrEmpty(param.Value))
                        {
                            _textureOverrideMap = new Dictionary<string, string>();
                            var entries = param.Value.Split(';');
                            foreach (var entry in entries)
                            {
                                if (string.IsNullOrEmpty(entry))
                                {
                                    continue;
                                }

                                var eqIdx = entry.IndexOf('=');
                                if (eqIdx > 0 && eqIdx < entry.Length - 1)
                                {
                                    var key = entry.Substring(0, eqIdx).Trim();
                                    var val = entry.Substring(eqIdx + 1).Trim();
                                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                                    {
                                        _textureOverrideMap[key] = val;
                                    }
                                }
                            }
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

        private void SetupSlotPosition()
        {
            Vector3 pos;

            if (_hasSourceBot)
            {
                var sourceBot = _robotService.GetOrCreateRobot(_sourceBotId);
                pos = sourceBot != null
                    ? sourceBot.transform.position
                    : CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
            }
            else
            {
                pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
            }

            if (_gameObject != null)
            {
                _gameObject.transform.position = pos;
            }

            _intendedWorldPosition = pos;

            if (_targetBotId != 0 && _gameObject != null)
            {
                var targetBot = _robotService.GetOrCreateRobot(_targetBotId);
                if (targetBot != null)
                {
                    // The dig effect must point the way the bot faces, toward
                    // the cell being dug. The previous +180 offset rendered it
                    // pointing back at the bot's tail.
                    _gameObject.transform.rotation = Quaternion.Euler(0, 0, targetBot.LogicalFacingAngle);
                }
            }

            _slot?.SetColor(_primaryColor);
            _slot?.SetSprite(null);
        }

        private void PlayAudio()
        {
            string eventName = GetSfxEventName(_effectType);
            _audioSystem.PlayAt(eventName, _intendedWorldPosition);
        }

        private async UniTask LoadVisualWithCancellationAsync(
            CancellationToken eventToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                eventToken,
                supervisorToken);
            await LoadVisualAsync(linkedCancellation.Token);
        }

        private async UniTask LoadVisualAsync(CancellationToken token)
        {
            try
            {
                var filename = $"VFX/{_effectType.ToString().ToLowerInvariant()}";
                var animData = await _assetLoader.GetAnimatedSpritesAsync(filename, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (animData.Frames != null && animData.Frames.Length > 0)
                {
                    _animationFrames = animData.Frames;
                    _currentFrame = 0;
                    _frameDuration = animData.FrameDuration / Mathf.Max(0.01f, _speed);
                    _isAnimated = true;
                    _slot?.SetSprite(_animationFrames[0]);
                    _slot?.SetEnabled(true);

                    _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
                    return;
                }

                var texture = await _assetLoader.GetTextureAsync(filename, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (texture != null)
                {
                    _ownedStaticSprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        RenderingConstants.PIXELS_PER_UNIT);
                    _slot?.SetSprite(_ownedStaticSprite);
                    _slot?.SetEnabled(true);

                    _maxLifetime = 1f;
                    return;
                }

                var bytes = await _assetLoader.GetAssetBytesAsync(filename, token, timeoutSeconds: 10);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (bytes != null && bytes.Length > 0)
                {
                    await TryLoadEffekseerAsync(bytes, token);
                }
                else
                {
                    MarkVisualCompleted();
                }
            }
            catch (OperationCanceledException)
            {
                // Task canceled cleanly
            }
            catch (Exception)
            {
                // Server audio may reference an optional visual asset. A
                // missing visual must not turn a valid audio event into a
                // blocking error or a noisy gameplay log.
                MarkVisualCompleted();
            }
        }

        private async UniTask<bool> TryLoadEffekseerAsync(byte[] bytes, CancellationToken token)
        {
            try
            {
                var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                    bytes,
                    _effectType.ToString(),
                    _assetLoader,
                    texturePathMapper: path =>
                    {
                        if (_textureOverrideMap != null && _textureOverrideMap.TryGetValue(path, out var mapped))
                        {
                            return mapped;
                        }

                        return path;
                    },
                    textureTimeoutSeconds: 10);

                if (token.IsCancellationRequested)
                {
                    RuntimeEffekseerLoader.DestroyEffect(effectAsset);
                    return false;
                }

                if (effectAsset == null)
                {
                    MarkVisualCompleted();
                    return false;
                }

                _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, _intendedWorldPosition);
                _effekseerAsset = effectAsset;

                if (_effekseerDynamicInputs != null)
                {
                    for (int i = 0; i < _effekseerDynamicInputs.Length; i++)
                    {
                        _effekseerHandle.SetDynamicInput(i, _effekseerDynamicInputs[i]);
                    }
                }

                if (_targetBotId != 0)
                {
                    var targetBot = _robotService.GetOrCreateRobot(_targetBotId);
                    if (targetBot != null)
                    {
                        _effekseerHandle.SetTargetLocation(targetBot.transform.position);
                    }
                }
                else if (_hasAttractorPosition)
                {
                    var attractorPos = CoordinateUtils.ServerToUnityPos(_attractorX, _attractorY, GetWorldHeight());
                    _effekseerHandle.SetTargetLocation(attractorPos);
                }

                _hasEffekseerEffect = true;

                _slot?.SetEnabled(false);

                _maxLifetime = 10f;
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerAudioEvent] Failed to load Effekseer effect: {ex.Message}");
                MarkVisualCompleted();
                return false;
            }
        }

        private int GetWorldHeight()
        {
            return _mapManager.WorldHeight;
        }

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
                RuntimeEffekseerLoader.DestroyEffect(_effekseerAsset);
                _effekseerAsset = null;
                _hasEffekseerEffect = false;
            }
        }

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
                RuntimeEffekseerLoader.DestroyEffect(_effekseerAsset);
                _effekseerAsset = null;
                _hasEffekseerEffect = false;
            }

            if (_slot != null)
            {
                _vfxPool.Release(_slot);
                _slot = null;
            }

            if (_ownedStaticSprite != null)
            {
                UnityEngine.Object.Destroy(_ownedStaticSprite);
                _ownedStaticSprite = null;
            }

            _gameObject = null;
        }
    }
}
