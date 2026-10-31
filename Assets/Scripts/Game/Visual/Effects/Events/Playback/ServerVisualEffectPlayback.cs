#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

using Kern.Diagnostics.Frames;
using Kern.Game.Visual.Effects.Events.Parameters;
using Kern.Game.Visual.Effects.Loading;

namespace Kern.Game.Visual.Effects.Events.Playback;

/// <summary>Owns loading and playback of the visual effect paired with a server event.</summary>
internal sealed class ServerVisualEffectPlayback : IDisposable
{
    private readonly string _visualEffectName;
    private readonly ushort _sourceX;
    private readonly ushort _sourceY;
    private readonly ushort _targetBotId;
    private readonly IRobotService _robotService;
    private readonly ILocalPlayerState _localPlayer;
    private readonly IAssetLoader _assetLoader;
    private readonly MapManager _mapManager;
    private readonly IVisualEffectService _visualEffectPool;
    private readonly VisualEffectParameters _parsedParams;

    private IVisualEffectSlot? _slot;
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
    private IRobotView? _sourceBot;
    private IRobotView? _targetBot;
    private CancellationTokenSource? _cts;

    public ServerVisualEffectPlayback(
        string visualEffectName,
        ushort targetBotId,
        ushort sourceX,
        ushort sourceY,
        VisualEffectParameters parsedParams,
        IVisualEffectSlot? slot,
        IRobotService robotService,
        ILocalPlayerState localPlayer,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVisualEffectService visualEffectPool)
    {
        _visualEffectName = visualEffectName;
        _sourceX = sourceX;
        _sourceY = sourceY;
        _targetBotId = targetBotId;
        _parsedParams = parsedParams;
        _slot = slot;
        _robotService = robotService;
        _localPlayer = localPlayer;
        _assetLoader = assetLoader;
        _mapManager = mapManager;
        _visualEffectPool = visualEffectPool;
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

        if (!_isAnimated && _lifeTimer >= _maxLifetime)
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

    internal static void RecordVisualOperationIfSlow(string what, long startTimestamp)
    {
        double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds >= 2.0)
        {
            Kern.Diagnostics.Frames.FrameEventLog.Record(
                $"визуальный эффект: {what} {milliseconds:F1} мс");
        }
    }

    // Эффект только ищет робота, но не создаёт его: botId приходит от сервера,
    // и робот, созданный ради эффекта, не попадал бы под удаление устаревших —
    // каждый незнакомый id оставлял бы объект на сцене до конца сессии.
    private IRobotView? FindExistingRobot(uint botId) =>
        _robotService.TryGetRobot(botId, out IRobotView? robot) ? robot : null;

    private void SetupSlotPosition()
    {
        Vector3 pos;

        if (_parsedParams.HasSourceBot)
        {
            long robotStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _sourceBot = FindExistingRobot(_parsedParams.SourceBotId);
            RecordVisualOperationIfSlow("робот-источник", robotStart);
            pos = _sourceBot != null
                ? _sourceBot.transform.position
                : WorldCoordinateConverter.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
        }
        else
        {
            _sourceBot = null;
            pos = WorldCoordinateConverter.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
        }

        if (_gameObject != null)
        {
            _gameObject.transform.position = pos;
        }

        _intendedWorldPosition = pos;

        // Слот приходит из пула с поворотом прошлого эффекта: поворот
        // ставится всегда, без бота — нулевой.
        float facing = 0f;
        // An audio-only event has no visual to rotate. Resolving or creating
        // a target robot here would load scene/visual resources inside AudioPacket.
        if (_targetBotId != 0 && _gameObject != null)
        {
            long robotStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _targetBot = FindExistingRobot(_targetBotId);
            RecordVisualOperationIfSlow("робот-цель", robotStart);
            if (_targetBot != null)
            {
                facing = FacingTowardEffectCell(_targetBot);
            }
        }
        else
        {
            _targetBot = null;
        }

        if (_gameObject != null)
        {
            _gameObject.transform.rotation = Quaternion.Euler(0, 0, facing);
        }

        _slot?.SetColor(_primaryColor);
        _slot?.SetSprite(null);
    }

    // Эффект копания смотрит от бота на копаемую клетку. Поворота в пакете
    // нет, поэтому направление восстанавливается, от надёжного к запасному:
    //
    // 1. Своё копание: направление, в котором клиент сам копал эту клетку.
    //    Не зависит ни от лага, ни от шагов после копания.
    // 2. Чужой бот: от последней позиции, присланной сервером, к клетке.
    //    Сервер копает от своей позиции бота и сообщает её раньше копания.
    // 3. Иначе — текущий угол бота. У бота, о котором сервер ещё ничего не
    //    сообщал, геометрия не считается: его позиция — начало мира.
    //
    // Сервер присылает поворот бота раньше эффекта копания, так что в штатном
    // порядке угол бота уже верный. Шаги 1–2 держат направление и тогда,
    // когда порядок нарушен: заглушка раньше отвечала на поворот с задержкой,
    // а пакет эффекта может прийти о боте, про которого клиент не знает.
    private float FacingTowardEffectCell(IRobotView bot)
    {
        ILocalPlayer? local = _localPlayer.Current;
        if (local != null &&
            local.BotId == bot.BotId &&
            local.TryGetDigDirection(_sourceX, _sourceY, out Direction digDirection))
        {
            return FacingAngle(digDirection);
        }

        if (bot.TryGetServerPosition(out Vector3 botPosition))
        {
            Vector3 cell = WorldCoordinateConverter.ServerToUnityPos(_sourceX, _sourceY, GetWorldHeight());
            Vector3 delta = cell - botPosition;
            float absoluteX = Mathf.Abs(delta.x);
            float absoluteY = Mathf.Abs(delta.y);
            if (Mathf.Max(absoluteX, absoluteY) >= 0.5f && Mathf.Abs(absoluteX - absoluteY) >= 0.5f)
            {
                // Ось Y Unity смотрит вверх, серверная — вниз.
                return FacingAngle(absoluteX > absoluteY
                    ? (delta.x > 0f ? Direction.Right : Direction.Left)
                    : (delta.y > 0f ? Direction.Up : Direction.Down));
            }
        }

        return bot.LogicalFacingAngle;
    }

    // Логический угол бота (Robot.LogicalFacingAngle) для направления.
    private static float FacingAngle(Direction direction) => direction switch
    {
        Direction.Up => 0f,
        Direction.Left => 90f,
        Direction.Down => 180f,
        Direction.Right => -90f,
        _ => 0f,
    };

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
            LoadedVisualEffect visual =
                await new VisualEffectAssetLoader(_assetLoader).LoadAsync(_visualEffectName, token);
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

    private int GetWorldHeight() => _mapManager.WorldHeight;

    private void MarkVisualCompleted()
    {
        if (_visualCompleted)
        {
            return;
        }

        _visualCompleted = true;
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
            _visualEffectPool.Release(_slot);
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
