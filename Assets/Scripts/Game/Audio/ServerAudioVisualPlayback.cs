#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Effekseer;
using Kern.World;
using UnityEngine;

namespace Kern.Game;

/// <summary>Owns the visual lifecycle of one server audio/VFX event.</summary>
internal sealed class ServerAudioVisualPlayback : IDisposable
{
    private readonly string _visualEffectName;
    private readonly ushort _sourceX;
    private readonly ushort _sourceY;
    private readonly ushort _targetBotID;
    private readonly IRobotService _robotService;
    private readonly IAssetLoader _assetLoader;
    private readonly MapManager _mapManager;
    private readonly IVfxService _vfxPool;
    private readonly ServerAudioParameters _parsedParams;

    private IVfxSlot? _slot;
    private GameObject? _gameObject;
    private Color _primaryColor = Color.white;
    private float _speed = 1f;
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
    private IRobotView? _sourceBot;
    private IRobotView? _targetBot;
    private CancellationTokenSource? _cts;

    public ServerAudioVisualPlayback(
        string visualEffectName,
        ushort targetBotId,
        ushort sourceX,
        ushort sourceY,
        ServerAudioParameters parsedParams,
        IVfxSlot? slot,
        IRobotService robotService,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVfxService vfxPool)
    {
        _visualEffectName = visualEffectName;
        _sourceX = sourceX;
        _sourceY = sourceY;
        _targetBotID = targetBotId;
        _parsedParams = parsedParams;
        _slot = slot;
        _robotService = robotService;
        _assetLoader = assetLoader;
        _mapManager = mapManager;
        _vfxPool = vfxPool;
        _gameObject = slot?.GameObject;

        SetupSlotPosition();
    }

    public Vector3 IntendedWorldPosition => _intendedWorldPosition;

    public bool IsDisposed => _slotReleased;

    public void StartLoading(IAsyncOperationSupervisor operations)
    {
        if (_slot == null)
        {
            _visualCompleted = true;
            return;
        }

        _cts = new CancellationTokenSource();
        CancellationToken eventToken = _cts.Token;
        operations.Run(
            "load_server_audio_visual",
            supervisorToken => LoadVisualWithCancellationAsync(eventToken, supervisorToken));
    }

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
            if (_sourceBot != null)
            {
                _effekseerHandle.SetLocation(_sourceBot.transform.position);
            }

            if (_targetBot != null)
            {
                _effekseerHandle.SetTargetLocation(_targetBot.transform.position);
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

    internal static void RecordIfSlow(string what, long startTimestamp)
    {
        double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds >= 2.0)
        {
            Kern.Core.Interfaces.Diagnostics.FrameEventLog.Record(
                $"звуковое событие: {what} {milliseconds:F1} мс");
        }
    }

    private void SetupSlotPosition()
    {
        Vector3 pos;

        if (_parsedParams.HasSourceBot)
        {
            long robotStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _sourceBot = _robotService.GetOrCreateRobot(_parsedParams.SourceBotID);
            RecordIfSlow("робот-источник", robotStart);
            pos = _sourceBot != null
                ? _sourceBot.transform.position
                : CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
        }
        else
        {
            _sourceBot = null;
            pos = CoordinateUtils.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
        }

        if (_gameObject != null)
        {
            _gameObject.transform.position = pos;
        }

        _intendedWorldPosition = pos;

        if (_targetBotID != 0)
        {
            long robotStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _targetBot = _robotService.GetOrCreateRobot(_targetBotID);
            RecordIfSlow("робот-цель", robotStart);
            if (_targetBot != null && _gameObject != null)
            {
                // The dig effect must point the way the bot faces, toward
                // the cell being dug. The previous +180 offset rendered it
                // pointing back at the bot's tail.
                _gameObject.transform.rotation = Quaternion.Euler(0, 0, _targetBot.LogicalFacingAngle);
            }
        }
        else
        {
            _targetBot = null;
        }

        _slot?.SetColor(_primaryColor);
        _slot?.SetSprite(null);
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
            ServerAudioVisual visual =
                await new ServerAudioVisualLoader(_assetLoader).LoadAsync(_visualEffectName, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (visual.Frames != null)
            {
                _animationFrames = visual.Frames;
                _currentFrame = 0;
                _frameDuration = visual.FrameDuration / Mathf.Max(0.01f, _speed);
                _isAnimated = true;
                _slot?.SetSprite(_animationFrames[0]);
                _slot?.SetEnabled(true);
                _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
                return;
            }

            if (visual.StaticSprite != null)
            {
                _ownedStaticSprite = visual.StaticSprite;
                _slot?.SetSprite(_ownedStaticSprite);
                _slot?.SetEnabled(true);
                _maxLifetime = 1f;
                return;
            }

            if (visual.EffectBytes != null)
            {
                await TryLoadEffekseerAsync(visual.EffectBytes, token);
                return;
            }

            MarkVisualCompleted();
        }
        catch (OperationCanceledException)
        {
            // Task canceled cleanly.
        }
        catch (Exception)
        {
            // Optional visual assets do not invalidate the associated audio event.
            MarkVisualCompleted();
        }
    }

    private async UniTask<bool> TryLoadEffekseerAsync(byte[] bytes, CancellationToken token)
    {
        try
        {
            var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                bytes,
                _visualEffectName,
                _assetLoader,
                texturePathMapper: path =>
                {
                    if (_parsedParams.TextureOverrideMap != null &&
                        _parsedParams.TextureOverrideMap.TryGetValue(path, out var mapped))
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

            if (_parsedParams.EffekseerDynamicInputs != null)
            {
                for (int i = 0; i < _parsedParams.EffekseerDynamicInputs.Length; i++)
                {
                    _effekseerHandle.SetDynamicInput(i, _parsedParams.EffekseerDynamicInputs[i]);
                }
            }

            if (_targetBotID != 0)
            {
                var targetBot = _robotService.GetOrCreateRobot(_targetBotID);
                if (targetBot != null)
                {
                    _effekseerHandle.SetTargetLocation(targetBot.transform.position);
                }
            }
            else if (_parsedParams.HasAttractorPosition)
            {
                var attractorPos = CoordinateUtils.ServerToUnityPos(
                    _parsedParams.AttractorX,
                    _parsedParams.AttractorY,
                    GetWorldHeight());
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

    private int GetWorldHeight() => _mapManager.WorldHeight;

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
        MarkVisualCompleted();

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
        _sourceBot = null;
        _targetBot = null;
    }
}
