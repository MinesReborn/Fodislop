#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Effekseer;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Kern.Game;

/// <summary>
/// Чисто визуальный серверный эффект из VFXPacket (в отличие от ServerAudioEvent —
/// без звука). Визуал ищется как кадры анимации, статичная текстура или
/// Effekseer-ассет "VFX/{имя эффекта}" (тот же конвейер, что у аудио-событий).
///
/// Вылетающий при добыче кристалл (VFX.Crystal) — особый режим: ОДИН кристаллик
/// летит от вскопанной клетки к роботу (TargetBotId) с лёгкой дугой, а над ним
/// висит цифра количества в цвете кристалла (параметры "color"/"count"). Спрайт
/// кристаллика: ассет "VFX/crystal" → иконка "Crys/{цвет}.png" → процедурный
/// ромбик (гарантированно виден даже без ассетов).
/// </summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully handle any dynamic asset load/play errors.")]
public sealed class ServerVfxEvent : IServerWorldEffect
{
    private const float FlightDurationSeconds = 0.4f;
    private const float FlightArcHeightUnits = 0.75f;
    private const float LabelLingerSeconds = 0.7f;
    private const int ProceduralCrystalSize = 8;

    // Размер кристаллика в полёте: масштаб transform'а относительно «сырой»
    // иконки (25×24 px при 16 px/юнит ≈ 1.56 юнита). 1 = как есть,
    // 0.8 ≈ текущий мини-кристалл, 0.4 = совсем маленький. Меняется плавно.
    private const float CrystalScale = 0.5f;

    // Лист со спрайтами кристаллов и цифр из старого клиента
    // (192×12: ячейки 0-5 - кристаллы, 6-14 - цифры 1-9, 15 - ноль).
    private const string CrystalDigitsSheet = "Crys/cryfont-sharedassets0.assets-67.png";
    private const int CrystalSheetCells = 16;
    private const int CrystalSheetIconCells = 6;

    // Цифры масштабируются под высоту кристаллика: текстура кристалла (24px)
    // вдвое выше ячейки листа (12px), поэтому множитель 2.
    private const float CrystalDigitScaleMultiplier = 2f;

    // VFX.PlaceDeny — маркер «клетку занять нельзя» при неудачной установке
    // пака. В клиентском пакете MinesServer.Data член не объявлен, сервер
    // присылает сырой байт 19.
    private const byte PlaceDenyVfxValue = 19;

    // Процедурный запрещающий маркер: 16px при PPU 16 = ровно одна клетка.
    private const int DenyMarkerSize = 16;

    // Плотность строки: шаг между центрами цифр = ширина цифры × этот множитель
    // (меньше 1 - цифры сидят плотнее).
    private const float CrystalDigitSpacingFactor = 0.75f;

    // Зазор между кристалликом и первой цифрой количества.
    private const float CrystalDigitGap = 0.08f;

    private readonly VFX _effectType;
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

    private float _speed = 1f;

    private Sprite[]? _animationFrames;
    private Sprite? _ownedStaticSprite;
    private Sprite? _flightSprite;
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

    private bool _flightMode;
    private bool _flightActive;
    private float _flightElapsed;
    private Vector3 _flightOrigin;

    // Спрайтовые цифры количества: отдельные объекты в мировом бач-рендерере,
    // летят справа от кристаллика и гаснут после прилёта.
    private WorldEntityBatchRenderer _batchRenderer = null!;
    private ISceneObjectFactory _sceneObjects = null!;
    private readonly List<GameObject> _digitObjects = [];
    private readonly List<WorldEntityBatchRenderer.SpriteHandle> _digitHandles = [];
    private float _digitRowOffset;
    private float _digitStep;
    private bool _digitsLingering;
    private float _lingerElapsed;

    private CancellationTokenSource? _cts;

    public ServerVfxEvent(
        VFXPacket packet,
        IVfxSlot? slot,
        IRobotService robotService,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVfxService vfxPool,
        IAsyncOperationSupervisor operations,
        WorldEntityBatchRenderer batchRenderer,
        ISceneObjectFactory sceneObjects)
    {
        _effectType = packet.EffectType;
        _sourceX = packet.X;
        _sourceY = packet.Y;
        _targetBotID = packet.TargetBotId;
        _slot = slot;
        _robotService = robotService;
        _assetLoader = assetLoader;
        _mapManager = mapManager;
        _vfxPool = vfxPool;
        _batchRenderer = batchRenderer;
        _sceneObjects = sceneObjects;

        if (slot != null)
        {
            _gameObject = slot.GameObject;
        }

        _parsedParams = ServerAudioParameters.Parse(packet.Parameters);
        SetupSlotPosition();

        if (slot != null)
        {
            _cts = new CancellationTokenSource();
            CancellationToken eventToken = _cts.Token;
            operations.Run(
                "load_server_vfx_visual",
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

        if (_flightMode)
        {
            UpdateFlight();
        }
        else if (_digitsLingering)
        {
            UpdateDigitsLinger();
        }
        else if (!_hasEffekseerEffect && !_isAnimated && _lifeTimer >= _maxLifetime)
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

    private UniTask LoadVisualWithCancellationAsync(
        CancellationToken eventToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            eventToken,
            supervisorToken);
        return LoadVisualAsync(linkedCancellation.Token);
    }

    private async UniTask LoadVisualAsync(CancellationToken token)
    {
        try
        {
            // Кристалл всегда летит к роботу: ассет "VFX/crystal" (если появится на
            // сервере) используется как спрайт полёта, а не как статичная картинка
            // на клетке. Если ни ассета, ни иконки Crys нет - рисуем процедурный
            // кристалл, чтобы эффект был виден всегда.
            if (_effectType == VFX.Crystal)
            {
                if (await TryStartCrystalFlightAsync(token))
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await StartFlightWithProceduralCrystalAsync(token);
                return;
            }

            // Маркер запрета установки пака (VFX.PlaceDeny): значение приходит
            // проводом сырым байтом — в клиентском пакете MinesServer.Data
            // этот член enum не объявлен. Опциональный ассет "VFX/placedeny",
            // иначе процедурный красный круг, как в легаси-клиенте.
            if ((byte)_effectType == PlaceDenyVfxValue)
            {
                await LoadPlaceDenyVisualAsync(token);
                return;
            }

            var filename = $"VFX/{_effectType.ToString().ToLowerInvariant()}";
            if (await TryLoadNamedVisualAsync(filename, token))
            {
                return;
            }

            MarkVisualCompleted();
        }
        catch (OperationCanceledException)
        {
            // Task canceled cleanly
        }
        catch (Exception)
        {
            // A missing optional visual must not turn a valid server event into
            // a blocking error or a noisy gameplay log.
            MarkVisualCompleted();
        }
    }

    /// <summary>
    /// Пробует кадры анимации, затем статичную текстуру, затем Effekseer-ассет.
    /// Каждый шаг изолирован: исключение «ассета нет» (загрузчик бросает на пропуске)
    /// не должно обрывать цепочку фолбэков.
    /// </summary>
    private async UniTask<bool> TryLoadNamedVisualAsync(string filename, CancellationToken token)
    {
        Sprite[]? frames = null;
        float frameDuration = _frameDuration;
        try
        {
            var animData = await _assetLoader.GetAnimatedSpritesAsync(filename, token);
            frames = animData.Frames;
            frameDuration = animData.FrameDuration / Mathf.Max(0.01f, _speed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            frames = null;
        }

        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (frames != null && frames.Length > 0)
        {
            _animationFrames = frames;
            _currentFrame = 0;
            _frameDuration = frameDuration;
            _isAnimated = true;
            _slot?.SetSprite(_animationFrames[0]);
            _slot?.SetEnabled(true);

            _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
            return true;
        }

        Texture2D? texture = null;
        try
        {
            texture = await _assetLoader.GetTextureAsync(filename, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            texture = null;
        }

        if (token.IsCancellationRequested)
        {
            return false;
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
            return true;
        }

        byte[]? bytes = null;
        try
        {
            bytes = await _assetLoader.GetAssetBytesAsync(filename, token, timeoutSeconds: 10);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            bytes = null;
        }

        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (bytes != null && bytes.Length > 0)
        {
            return await TryLoadEffekseerAsync(filename, bytes, token);
        }

        return false;
    }

    private async UniTask<bool> TryStartCrystalFlightAsync(CancellationToken token)
    {
        // Приоритет спрайта полёта: ассет "VFX/crystal" (задел на будущее), затем
        // цветная иконка кристалла с сервера "Crys/{цвет}.png".
        Sprite? sprite = await TryLoadCrystalSpriteAsync("vfx/crystal", token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        sprite ??= await TryLoadCrystalSpriteAsync($"Crys/{_parsedParams.CrystalColorLetter}.png", token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (sprite == null)
        {
            return false;
        }

        await ConfigureFlightAsync(sprite, token);
        return true;
    }

    private async UniTask<Sprite?> TryLoadCrystalSpriteAsync(string filename, CancellationToken token)
    {
        try
        {
            var texture = await _assetLoader.GetTextureAsync(filename, token);
            return texture == null
                ? null
                : Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    RenderingConstants.PIXELS_PER_UNIT);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async UniTask StartFlightWithProceduralCrystalAsync(CancellationToken token)
    {
        var sprite = Sprite.Create(
            CreateProceduralCrystalTexture(),
            new Rect(0, 0, ProceduralCrystalSize, ProceduralCrystalSize),
            new Vector2(0.5f, 0.5f),
            RenderingConstants.PIXELS_PER_UNIT);
        await ConfigureFlightAsync(sprite, token);
    }

    private async UniTask ConfigureFlightAsync(Sprite sprite, CancellationToken token)
    {
        _flightSprite = sprite;
        _flightMode = true;
        _maxLifetime = FlightDurationSeconds + LabelLingerSeconds + 1f;

        Debug.Log($"[ServerVfxEvent] Crystal flight: cell {_sourceX}:{_sourceY}, bot {_targetBotID}, " +
                  $"sprite '{(sprite.texture != null ? sprite.texture.name : "?")}', amount {_parsedParams.CrystalCount}, " +
                  $"crystalScale {CrystalScale}.");

        StartFlight();
        await CreateCrystalDigitsAsync(token);
    }

    // Один кристаллик летит к роботу, а количество летит следом цифрами из
    // спрайт-листа старого клиента (cryfont): белые цифры с тёмной обводкой,
    // тонированные в цвет кристалла. Цифры стоят строго справа от кристаллика,
    // на той же высоте, масштаб - под высоту кристаллика.
    private async UniTask CreateCrystalDigitsAsync(CancellationToken token)
    {
        Texture2D? sheet = await _assetLoader.GetTextureAsync(CrystalDigitsSheet, token);
        if (token.IsCancellationRequested || sheet == null)
        {
            return;
        }

        float cell = (float)sheet.width / CrystalSheetCells;
        float digitScale = CrystalScale * CrystalDigitScaleMultiplier;
        float digitWidth = cell / RenderingConstants.PIXELS_PER_UNIT * digitScale;
        _digitStep = digitWidth * CrystalDigitSpacingFactor;

        float crystalHalf = _flightSprite != null
            ? _flightSprite.rect.width / _flightSprite.pixelsPerUnit * CrystalScale * 0.5f
            : 0f;
        float digitHalf = digitWidth * 0.5f;
        _digitRowOffset = crystalHalf + digitHalf + CrystalDigitGap;

        string digits = Mathf.Clamp(_parsedParams.CrystalCount, 0, 999)
            .ToString(CultureInfo.InvariantCulture);

        Color tint = CrystalNumberColor(_parsedParams.CrystalColorLetter);
        Vector3 basePosition = _intendedWorldPosition;

        for (int i = 0; i < digits.Length; i++)
        {
            int digit = digits[i] - '0';
            int cellIndex = digit == 0
                ? CrystalSheetCells - 1
                : CrystalSheetIconCells + digit - 1;

            var digitSprite = Sprite.Create(
                sheet,
                new Rect(cellIndex * cell, 0f, cell, sheet.height),
                new Vector2(0.5f, 0.5f),
                RenderingConstants.PIXELS_PER_UNIT);

            GameObject go = _sceneObjects.Create($"CrystalDigit_{i}", RuntimeOwner.VFX);
            go.transform.position = basePosition + (Vector3.right * (_digitStep * i));
            go.transform.localScale = Vector3.one * digitScale;

            WorldEntityBatchRenderer.SpriteHandle handle =
                _batchRenderer.RegisterSprite(go.transform, -500);

            // ВАЖНО: спрайт ставится через батч-рендерер, а не через хэндл -
            // только этот путь регистрирует текстуру листа в мировом атласе.
            _batchRenderer.SetSprite(handle, digitSprite);
            handle.SetColor(tint);
            handle.SetEnabled(true);

            _digitObjects.Add(go);
            _digitHandles.Add(handle);
        }
    }

    private static Color CrystalNumberColor(string letter) => letter switch
    {
        // Приблизительно те же цвета, что у иконок Crys/*.png.
        "b" => new Color(0.30f, 0.55f, 1.00f),
        "r" => new Color(1.00f, 0.35f, 0.35f),
        "v" => new Color(0.75f, 0.45f, 1.00f),
        "w" => new Color(0.95f, 0.95f, 0.95f),
        "c" => new Color(0.35f, 0.90f, 1.00f),
        _ => new Color(0.35f, 0.90f, 0.45f),
    };

    // Маркер запрета установки пака: опциональный ассет "VFX/placedeny"
    // (тот же конвейер: анимация → статичная текстура → Effekseer), иначе
    // процедурный красный круг с чертой — как в легаси-клиенте.
    private async UniTask LoadPlaceDenyVisualAsync(CancellationToken token)
    {
        if (await TryLoadNamedVisualAsync("VFX/placedeny", token))
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        StartProceduralDenyMarker();
    }

    private void StartProceduralDenyMarker()
    {
        // Формат — RGBA32 sRGB (linear: false): спрайт попадает в мировой
        // атлас батч-рендерера, который требует совпадения graphicsFormat.
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            DenyMarkerSize,
            DenyMarkerSize,
            "ProceduralDenyMarker",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        var pixels = new Color32[DenyMarkerSize * DenyMarkerSize];
        float center = (DenyMarkerSize - 1) * 0.5f;
        float outer = (DenyMarkerSize * 0.5f) - 0.5f;
        var red = new Color32(214, 36, 36, 224);
        for (int py = 0; py < DenyMarkerSize; py++)
        {
            for (int px = 0; px < DenyMarkerSize; px++)
            {
                float dx = px - center;
                float dy = py - center;
                float dist = Mathf.Sqrt((dx * dx) + (dy * dy));
                bool ring = dist <= outer && dist > (outer - 2.5f);
                bool slash = Mathf.Abs(dx - dy) < 1.3f && dist <= (outer - 1.5f);
                pixels[(py * DenyMarkerSize) + px] = ring || slash ? red : default;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false);

        _ownedStaticSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, DenyMarkerSize, DenyMarkerSize),
            new Vector2(0.5f, 0.5f),
            RenderingConstants.PIXELS_PER_UNIT);
        _slot?.SetColor(Color.white);
        _slot?.SetSprite(_ownedStaticSprite);
        _slot?.SetEnabled(true);
        _isAnimated = false;
        _maxLifetime = 0.7f;
    }

    private Texture2D CreateProceduralCrystalTexture()
    {
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            ProceduralCrystalSize,
            ProceduralCrystalSize,
            "ProceduralCrystal",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        Color color = _parsedParams.CrystalColorLetter switch
        {
            "b" => new Color(0.20f, 0.50f, 1.00f),
            "r" => new Color(1.00f, 0.25f, 0.25f),
            "v" => new Color(0.70f, 0.30f, 1.00f),
            "w" => new Color(0.95f, 0.95f, 1.00f),
            "c" => new Color(0.30f, 0.95f, 1.00f),
            _ => new Color(0.25f, 0.90f, 0.35f),
        };

        var pixels = new Color[ProceduralCrystalSize * ProceduralCrystalSize];
        var center = (ProceduralCrystalSize - 1) * 0.5f;
        for (int y = 0; y < ProceduralCrystalSize; y++)
        {
            for (int x = 0; x < ProceduralCrystalSize; x++)
            {
                float distance = MathF.Abs(x - center) + MathF.Abs(y - center);
                pixels[(y * ProceduralCrystalSize) + x] = distance <= center + 0.5f ? color : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private void StartFlight()
    {
        _flightActive = true;
        _flightElapsed = 0f;
        _flightOrigin = _intendedWorldPosition;
        if (_gameObject != null)
        {
            // Иконка кристалла летит «как есть»: сбрасываем поворот, применённый
            // в SetupSlotPosition по направлению взгляда робота, и задаём масштаб.
            _gameObject.transform.rotation = Quaternion.identity;
            _gameObject.transform.localScale = Vector3.one * CrystalScale;
        }

        _slot?.SetSprite(_flightSprite);
        _slot?.SetEnabled(true);
    }

    private void UpdateFlight()
    {
        if (!_flightActive)
        {
            return;
        }

        _flightElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_flightElapsed / FlightDurationSeconds);
        Vector3 destination = _targetBot != null
            ? _targetBot.transform.position
            : _intendedWorldPosition;
        Vector3 position = Vector3.Lerp(_flightOrigin, destination, progress);
        position.y += FlightArcHeightUnits * Mathf.Sin(Mathf.PI * progress);

        if (_gameObject != null)
        {
            _gameObject.transform.position = position;
        }

        Vector3 digitBase = position + new Vector3(_digitRowOffset, 0f, 0f);
        for (int i = 0; i < _digitObjects.Count; i++)
        {
            if (_digitObjects[i] != null)
            {
                _digitObjects[i].transform.position =
                    digitBase + (Vector3.right * (_digitStep * i));
            }
        }

        if (progress < 1f)
        {
            return;
        }

        // Кристаллик долетел и исчез, цифры ещё немного висят и гаснут.
        _flightActive = false;
        _flightMode = false;
        _digitsLingering = true;
        _lingerElapsed = 0f;
        _slot?.SetEnabled(false);
        SetDigitsOpacity(1f);
    }

    private void UpdateDigitsLinger()
    {
        _lingerElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_lingerElapsed / LabelLingerSeconds);
        Vector3 anchor = _targetBot != null
            ? _targetBot.transform.position
            : _intendedWorldPosition;
        Vector3 digitBase = anchor
            + new Vector3(_digitRowOffset, 0f, 0f)
            + (Vector3.up * (0.3f * progress));

        for (int i = 0; i < _digitObjects.Count; i++)
        {
            if (_digitObjects[i] != null)
            {
                _digitObjects[i].transform.position =
                    digitBase + (Vector3.right * (_digitStep * i));
            }
        }

        SetDigitsOpacity(1f - progress);

        if (progress >= 1f)
        {
            _visualCompleted = true;
        }
    }

    private void SetDigitsOpacity(float opacity)
    {
        Color tint = CrystalNumberColor(_parsedParams.CrystalColorLetter);
        tint.a = Mathf.Clamp01(opacity);
        for (int i = 0; i < _digitHandles.Count; i++)
        {
            _digitHandles[i]?.SetColor(tint);
        }
    }

    private async UniTask<bool> TryLoadEffekseerAsync(string filename, byte[] bytes, CancellationToken token)
    {
        try
        {
            var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                bytes,
                _effectType.ToString(),
                _assetLoader,
                texturePathMapper: path =>
                {
                    if (_parsedParams.TextureOverrideMap != null && _parsedParams.TextureOverrideMap.TryGetValue(path, out var mapped))
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
                var attractorPos = CoordinateUtils.ServerToUnityPos(_parsedParams.AttractorX, _parsedParams.AttractorY, GetWorldHeight());
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
            Debug.LogWarning($"[ServerVfxEvent] Failed to load Effekseer effect '{filename}': {ex.Message}");
            MarkVisualCompleted();
            return false;
        }
    }

    private void SetupSlotPosition()
    {
        Vector3 pos;

        if (_parsedParams.HasSourceBot)
        {
            _sourceBot = _robotService.GetOrCreateRobot(_parsedParams.SourceBotID);
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
            _targetBot = _robotService.GetOrCreateRobot(_targetBotID);
            if (_targetBot != null && _gameObject != null)
            {
                // Направленные эффекты разворачиваются по направлению взгляда
                // бота из пакета (тот же контракт, что у аудио-эффектов).
                _gameObject.transform.rotation = Quaternion.Euler(0, 0, _targetBot.LogicalFacingAngle);
            }
        }
        else
        {
            _targetBot = null;
        }

        _slot?.SetColor(Color.white);
        _slot?.SetSprite(null);
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
        MarkVisualCompleted();

        if (_slot != null)
        {
            _vfxPool.Release(_slot);
            _slot = null;
        }

        for (int i = 0; i < _digitObjects.Count; i++)
        {
            if (_digitObjects[i] != null)
            {
                UnityEngine.Object.Destroy(_digitObjects[i]);
            }
        }

        _digitObjects.Clear();
        _digitHandles.Clear();

        if (_ownedStaticSprite != null)
        {
            UnityEngine.Object.Destroy(_ownedStaticSprite);
            _ownedStaticSprite = null;
        }

        if (_flightSprite != null)
        {
            UnityEngine.Object.Destroy(_flightSprite);
            _flightSprite = null;
        }

        _gameObject = null;
        _sourceBot = null;
        _targetBot = null;
    }
}
