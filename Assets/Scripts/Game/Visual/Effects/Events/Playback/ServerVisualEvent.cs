#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using Canvas = UnityEngine.Canvas;
using CanvasScaler = UnityEngine.UI.CanvasScaler;
using GraphicRaycaster = UnityEngine.UI.GraphicRaycaster;
using TMPro;
using UnityEngine;

using Kern.AssetPipeline.Loading;
using Kern.Game.Entities.Robots;
using Kern.Game.Visual.Effects.Events.Parameters;
using Kern.Game.Rendering;
using Kern.Game.Visual.Effects.Crystals;
using Kern.Game.Visual.Effects.Loading;
using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Sprites;

using Kern.Game.Visual.Effects.Lighting;
using Kern.World.Lighting;

namespace Kern.Game.Visual.Effects.Events.Playback;

/// <summary>
/// Серверное событие визуального эффекта из VisualPacket (в отличие от ServerAudioEvent —
/// без звука). Визуал ищется как кадры анимации или статичная текстура.
///
/// VisualEffect.Crystal: исходный спрайт и счётчик едут одним корнем в двух фазах
/// CrystallPlus.Update: подъём moveFrom на 0.25 по Y, затем полёт в moveTo.
/// </summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully handle any dynamic asset load/play errors.")]
public sealed class ServerVisualEvent : IServerWorldEffect
{
    private const int ProceduralCrystalSize = 8;

    // Масштаб старого варианта на отдельных ресурсах Crystals/{color}.png.
    private const float CrystalScale = 0.5f;

    // VisualEffect.PlaceDeny — маркер «клетку занять нельзя» при неудачной установке
    // пака. В клиентском пакете MinesServer.Data член не объявлен, сервер
    // присылает сырой байт 19.
    private const byte PlaceDenyVisualEffectValue = 19;

    // Процедурный запрещающий маркер: 16px при PPU 16 = ровно одна клетка.
    private const int DenyMarkerSize = 16;

    // Плотность строки: шаг между центрами цифр = ширина цифры × этот множитель
    // (меньше 1 - цифры сидят плотнее).

    // Зазор между кристалликом и первой цифрой количества.

    private readonly VisualEffect _effectType;
    private readonly int _sourceX;
    private readonly int _sourceY;
    private readonly float? _localFacingAngle;
    private readonly ushort _targetBotId;
    private readonly IRobotService _robotService;
    private readonly IAssetLoader _assetLoader;
    private readonly SpriteAnimationLibrary _spriteAnimations;
    private readonly CrystalCollectionVisualVariant _crystalCollectionVariant;
    private readonly MapManager _mapManager;
    private readonly IVisualEffectService _visualEffectPool;

    private readonly VisualEffectParameters _parsedParams;

    private IVisualEffectSlot? _slot;
    private GameObject? _gameObject;

    private float _speed = 1f;

    private Sprite[]? _animationFrames;
    private Sprite? _ownedStaticSprite;
    private IWorldLabel? _collectionLabel;
    private readonly IWorldLabels? _worldLabels;
    private Sprite? _flightSprite;
    private GameObject? _sourceCollectionSpriteObject;
    private GameObject? _sourceCollectionCanvasObject;
    private bool _ownedStaticSpriteShared;
    private bool _flightSpriteShared;
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

    private bool _flightMode;
    private bool _flightActive;
    private bool _flightReturning;
    private Vector3 _flightDestination;
    private Vector3 _flightOrigin;

    // --- Луч GunShot: порт легаси GunShotScript старого клиента ---
    // Лента из 7 сегментов от стреляющего бота к точке попадания, случайное
    // блуждание узлов (0.9/0.1 к прямой), полураскрытие sin(7t), UV-окно
    // 0.1 родной текстуры, цвет cyan. Рисуется ТОЛЬКО лучом: статичная
    // картинка-фолбэк для GunShot запрещена (ложилась на стрелка).
    private const int BeamSectorCount = 7;

    // Жизнь луча: по фидбеку 0.7 c «слишком долгий по ощущениям» — укоротили вдвое.
    private const float BeamLifetimeSeconds = 0.35f;

    // Отступ «от дула»: начало ленты сдвигается вперёд по ходу луча, чтобы
    // лента не ложилась поверх спрайта стреляющего робота (клетка = 1 юнит).
    private const float BeamMuzzleInset = 0.55f;

    // Полуширина ленты: легаси-0.5 было «слишком жирно» — 0.15 по фидбеку.
    private const float BeamHalfWidth = 0.15f;

    private const float BeamUvWindow = 0.1f;
    private const int BeamSortingOrder = -600;
    private const string BeamShaderName = "Kern/UnlitGunShot";

    private bool _beamMode;
    private GameObject? _beamObject;
    private Mesh? _beamMesh;
    private Material? _beamMaterial;
    private readonly Vector2[] _beamTail = new Vector2[BeamSectorCount + 1];
    private Vector3[] _beamVertices = new Vector3[4 * BeamSectorCount];
    private Vector2[] _beamUvs = new Vector2[4 * BeamSectorCount];
    private Color[] _beamColors = new Color[4 * BeamSectorCount];
    private int[] _beamTriangles = new int[6 * BeamSectorCount];

    // Спрайтовые цифры количества: отдельные объекты в мировом бач-рендерере,
    // летят справа от кристаллика и гаснут после прилёта.
    private WorldEntityBatchRenderer _batchRenderer = null!;
    private ISceneObjectFactory _sceneObjects = null!;
    private readonly List<GameObject> _digitObjects = new();
    private readonly List<WorldEntityBatchRenderer.SpriteHandle> _digitHandles = new();
    private readonly List<Sprite> _digitSprites = new();
    private float _digitRowOffset;
    private float _digitRowVerticalOffset;
    private float _digitStep = 0f;

    private CancellationTokenSource? _cts;
    private readonly TransientEffectLight? _light;
    private SpriteEmissionFrame[] _frameEmission = Array.Empty<SpriteEmissionFrame>();

    public ServerVisualEvent(
        VisualPacket packet,
        IVisualEffectSlot? slot,
        IRobotService robotService,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVisualEffectService visualEffectPool,
        IAsyncOperationSupervisor operations,
        WorldEntityBatchRenderer batchRenderer,
        SpriteAnimationLibrary spriteAnimations,
        CrystalCollectionVisualVariant crystalCollectionVariant,
        ISceneObjectFactory sceneObjects,
        IWorldLabels? worldLabels = null,
        Vector2Int? localSourceCell = null,
        float? localFacingAngle = null,
        LightingEngine? lighting = null)
    {
        _effectType = packet.EffectType;
        _sourceX = localSourceCell?.x ?? packet.X;
        _sourceY = localSourceCell?.y ?? packet.Y;
        _localFacingAngle = localFacingAngle;
        _targetBotId = packet.TargetBotId;
        _slot = slot;
        _robotService = robotService;
        _assetLoader = assetLoader;
        _spriteAnimations = spriteAnimations;
        _crystalCollectionVariant = crystalCollectionVariant;
        _mapManager = mapManager;
        _visualEffectPool = visualEffectPool;
        _batchRenderer = batchRenderer;
        _sceneObjects = sceneObjects;
        _worldLabels = worldLabels;

        if (slot != null)
        {
            _gameObject = slot.GameObject;
        }

        if (_effectType == VisualEffect.Bz && slot != null)
        {
            _light = new TransientEffectLight(lighting ?? throw new ArgumentNullException(nameof(lighting)));
        }
        _parsedParams = VisualEffectParameters.Parse(packet.Parameters);
        SetupSlotPosition();

        if (slot != null)
        {
            _cts = new CancellationTokenSource();
            CancellationToken eventToken = _cts.Token;
            operations.Run(
                "load_server_visual_effect",
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
                SubmitEmission();
            }
            else
            {
                _visualCompleted = true;
            }
        }

        if (_beamMode)
        {
            UpdateBeam();
        }

        if (_flightMode)
        {
            UpdateFlight();
        }
        else if (!_isAnimated && _lifeTimer >= _maxLifetime)
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
        _collectionLabel?.Dispose();
        _collectionLabel = null;
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
            // Кристалл всегда летит к роботу спрайтом своего цвета (Visual/Crystals/<цвет>.png,
            // либо кадр атласа минералов), а не статичной картинкой на клетке. Если спрайта
            // нет - рисуем процедурный кристалл, чтобы эффект был виден всегда.
            if (_effectType == VisualEffect.Crystal)
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

            // Маркер запрета установки пака (VisualEffect.PlaceDeny): значение приходит
            // проводом сырым байтом — в клиентском пакете MinesServer.Data
            // этот член enum не объявлен. Опциональный ассет "Visual/placedeny.png",
            // иначе процедурный красный круг, как в легаси-клиенте.
            if ((byte)_effectType == PlaceDenyVisualEffectValue)
            {
                await LoadPlaceDenyVisualAsync(token);
                return;
            }

            var filename = VisualEffectAssetLoader.GetPublishedAssetPath(_effectType.ToString());
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
        catch (Exception exception)
        {
            Debug.LogError($"[ServerVisualEvent] Failed to create {_effectType} at " +
                $"({_sourceX}, {_sourceY}), target {_targetBotId}: {exception}");
            MarkVisualCompleted();
        }
    }

    /// <summary>
    /// Пробует кадры анимации, затем статичную текстуру.
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
            if (_light != null)
            {
                if (animData.FrameEmission.Length != frames.Length)
                {
                    throw new InvalidOperationException("Mining animation has no frame emission metadata.");
                }
                _frameEmission = animData.FrameEmission;
            }
            frameDuration = animData.FrameDuration / Mathf.Max(0.01f, _speed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            if (_effectType == VisualEffect.Bz)
            {
                throw;
            }

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
            SubmitEmission();

            _maxLifetime = (_animationFrames.Length * _frameDuration) + 0.5f;
            return true;
        }

        if (_effectType == VisualEffect.Bz)
        {
            throw new InvalidOperationException("Spark animation did not provide its frames.");
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
            // VisualEffect.GunShot — легаси-луч старого клиента: процедурная лента из
            // родной текстуры 'Visual/gunshot.png', а не статичная картинка на клетке.
            if (_effectType == VisualEffect.GunShot)
            {
                // Луч рисуется только когда робот-стрелок резолвится. Иначе
                // НЕ рисуем ничего: фолбэк «полная текстура статичным
                // спрайтом» ложился прямо на игрока-стрелка.
                if (_targetBot != null)
                {
                    StartBeam(texture);
                }
                else
                {
                    MarkVisualCompleted();
                }

                return true;
            }

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

        Debug.Log($"[ServerVisualEvent] '{filename}': анимация и текстура отсутствуют.");
        return false;
    }

    private async UniTask<bool> TryStartCrystalFlightAsync(CancellationToken token)
    {
        string mineral = _parsedParams.CrystalColorLetter switch
        {
            "b" => "blue", "r" => "red", "v" => "violet",
            "w" => "white", "c" => "cyan", _ => "green",
        };
        Sprite? sprite;
        bool isSharedSprite = false;
        if (_crystalCollectionVariant == CrystalCollectionVisualVariant.SourceMineralAtlas)
        {
            sprite = _spriteAnimations.GetCrystalCollectionSprite(_parsedParams.CrystalColorLetter);
            isSharedSprite = true;
        }
        else
        {
            sprite = await TryLoadCrystalSpriteAsync($"Visual/Crystals/{mineral}.png", token);
        }

        if (token.IsCancellationRequested)
        {
            if (sprite != null && !isSharedSprite)
            {
                UnityEngine.Object.Destroy(sprite);
            }

            return false;
        }

        if (sprite == null)
        {
            return false;
        }

        _flightSpriteShared = isSharedSprite;
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
        Sprite sprite = _batchRenderer.GetOrCreateOwnedSprite(
            $"ProceduralCrystal/{CrystalColorKey(_parsedParams.CrystalColorLetter)}",
            CreateProceduralCrystalTexture,
            ProceduralCrystalSize,
            ProceduralCrystalSize);
        _flightSpriteShared = true;
        await ConfigureFlightAsync(sprite, token);
    }

    private async UniTask ConfigureFlightAsync(Sprite sprite, CancellationToken token)
    {
        _flightSprite = sprite;
        _flightMode = true;
        _maxLifetime = float.PositiveInfinity;

        StartFlight();
        await CreateCrystalDigitsAsync(token);
    }

    // SourceMineralAtlas recreates the original Canvas and TMP hierarchy.
    // CurrentAssets keeps the existing project world-label presentation.
    private UniTask CreateCrystalDigitsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_parsedParams.CrystalCount < 2)
        {
            return UniTask.CompletedTask;
        }

        if (_crystalCollectionVariant == CrystalCollectionVisualVariant.SourceMineralAtlas)
        {
            if (_gameObject == null)
            {
                throw new InvalidOperationException("Source crystal collection requires its world root.");
            }

            string count = _parsedParams.CrystalCount.ToString(CultureInfo.InvariantCulture);
            _sourceCollectionCanvasObject = CreateSourceCollectionCounter(
                _sceneObjects,
                _gameObject.transform,
                count);
            return UniTask.CompletedTask;
        }

        if (_worldLabels == null)
        {
            throw new InvalidOperationException("Crystal collection requires world labels.");
        }

        _digitRowOffset = 0.25f;
        _digitRowVerticalOffset = 0f;
        _collectionLabel = _worldLabels.Create(WorldLabelKind.Collection);
        _collectionLabel.SetText(_parsedParams.CrystalCount.ToString(CultureInfo.InvariantCulture));
        _collectionLabel.SetPosition(_intendedWorldPosition +
            new Vector3(_digitRowOffset, _digitRowVerticalOffset, 0f));
        _collectionLabel.SetVisible(true);
        return UniTask.CompletedTask;
    }

    internal static GameObject CreateSourceCollectionCounter(
        ISceneObjectFactory sceneObjects,
        Transform parent,
        string count)
    {
        GameObject canvasObject = sceneObjects.Create("Canvas", RuntimeOwner.VisualEffects);
        GameObject textObject = sceneObjects.Create("Text (TMP)", RuntimeOwner.VisualEffects);
        try
        {
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(parent, worldPositionStays: false);
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            if (canvasObject.transform is not RectTransform canvasRect)
            {
                throw new InvalidOperationException("World-space collection Canvas needs a RectTransform.");
            }

            canvasRect.sizeDelta = Vector2.one;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.planeDistance = 100f;
            canvas.overrideSorting = false;
            canvas.sortingOrder = 100;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            textObject.layer = 5;
            textObject.transform.SetParent(canvasObject.transform, worldPositionStays: false);
            textObject.transform.localPosition = Vector3.zero;
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one;

            TextMeshProUGUI counter = textObject.AddComponent<TextMeshProUGUI>();
            if (textObject.transform is not RectTransform textRect)
            {
                throw new InvalidOperationException("World-space collection text needs a RectTransform.");
            }

            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.anchoredPosition = new Vector2(1f, 0.375f);
            textRect.sizeDelta = Vector2.zero;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            counter.fontSize = 0.5f;
            counter.enableAutoSizing = false;
            counter.overflowMode = TextOverflowModes.Truncate;
            counter.text = count;
            return canvasObject;
        }
        catch
        {
            UnityEngine.Object.Destroy(canvasObject);
            UnityEngine.Object.Destroy(textObject);
            throw;
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

    private static string CrystalColorKey(string letter) => letter switch
    {
        "b" => "b",
        "r" => "r",
        "v" => "v",
        "w" => "w",
        "c" => "c",
        _ => "g",
    };

    // Маркер запрета установки пака: опциональный ассет "Visual/placedeny.png"
    // (тот же конвейер: анимация → статичная текстура), иначе
    // процедурный красный круг с чертой — как в легаси-клиенте.
    private async UniTask LoadPlaceDenyVisualAsync(CancellationToken token)
    {
        if (await TryLoadNamedVisualAsync("Visual/placedeny.png", token))
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
        _ownedStaticSprite = _batchRenderer.GetOrCreateOwnedSprite(
            "ProceduralDenyMarker",
            CreateProceduralDenyTexture,
            DenyMarkerSize,
            DenyMarkerSize);
        _ownedStaticSpriteShared = true;
        _slot?.SetColor(Color.white);
        _slot?.SetSprite(_ownedStaticSprite);
        _slot?.SetEnabled(true);
        _isAnimated = false;
        _maxLifetime = 0.7f;
    }

    private static Texture2D CreateProceduralDenyTexture()
    {
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
        return texture;
    }

    // ------------------------------------------------------------------
    // Луч GunShot (легаси): геометрия пересобирается каждый кадр — начало
    // следует за стреляющим ботом, узлы дрожат случайным блужданием.
    // ------------------------------------------------------------------
    private void StartBeam(Texture2D texture)
    {
        Shader beamShader = Shader.Find(BeamShaderName);
        if (beamShader == null)
        {
            Debug.LogWarning($"[ServerVisualEvent] Шейдер '{BeamShaderName}' не найден — луч GunShot не построен.");
            MarkVisualCompleted();
            return;
        }

        // Направление луча считается геометрией, разворот «по взгляду бота»
        // из SetupSlotPosition мешать не должен.
        _gameObject!.transform.rotation = Quaternion.identity;

        _beamTriangles = new int[6 * BeamSectorCount];
        _beamUvs = new Vector2[4 * BeamSectorCount];
        _beamColors = new Color[4 * BeamSectorCount];
        _beamVertices = new Vector3[4 * BeamSectorCount];
        for (int i = 0; i < BeamSectorCount; i++)
        {
            _beamTriangles[6 * i] = 4 * i;
            _beamTriangles[6 * i + 1] = 4 * i + 1;
            _beamTriangles[6 * i + 2] = 4 * i + 2;
            _beamTriangles[6 * i + 3] = 4 * i + 1;
            _beamTriangles[6 * i + 4] = 4 * i + 3;
            _beamTriangles[6 * i + 5] = 4 * i + 2;
        }

        for (int i = 0; i < _beamColors.Length; i++)
        {
            _beamColors[i] = new Color(0f, 1f, 1f, 1f);
        }

        _beamMesh = new Mesh
        {
            name = "GunShotBeam",
        };
        _beamMesh.MarkDynamic();
        _beamMesh.vertices = _beamVertices;
        _beamMesh.uv = _beamUvs;
        _beamMesh.colors = _beamColors;
        _beamMesh.triangles = _beamTriangles;
        _beamMesh.bounds = new Bounds(Vector3.zero, new Vector3(44f, 44f, 44f));

        _beamMaterial = new Material(beamShader)
        {
            name = "GunShotBeamMaterial",
            hideFlags = HideFlags.HideAndDontSave,
            mainTexture = texture,
        };

        _beamObject = _sceneObjects.Create("GunShotBeam", RuntimeOwner.General);
        _beamObject.transform.SetParent(_gameObject.transform, false);
        _beamObject.AddComponent<MeshFilter>().sharedMesh = _beamMesh;
        MeshRenderer beamRenderer = _beamObject.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = _beamMaterial;
        beamRenderer.sortingOrder = BeamSortingOrder;

        ResetBeamTail();

        _beamMode = true;
        _isAnimated = false;
        _maxLifetime = BeamLifetimeSeconds;
        UpdateBeam();
        Debug.Log("[ServerVisualEvent] GunShot: легаси-луч запущен (текстура 'Visual/gunshot.png').");
    }

    private void ResetBeamTail()
    {
        Vector2 startLocal = BeamStartLocal();
        _beamTail[0] = startLocal;
        _beamTail[BeamSectorCount] = Vector2.zero;
        for (int i = 1; i < BeamSectorCount; i++)
        {
            float d = (float)i / BeamSectorCount;
            _beamTail[i] = d * startLocal;
        }
    }

    // Начало луча в локальных координатах относительно точки попадания
    // (луч привязан к _gameObject, стоящему в точке попадания).
    private Vector2 BeamStartLocal()
    {
        // Старт луча привязан к стреляющему роботу. Для чужих роботов берём
        // серверную позицию: живой transform тянется интерполяцией, а после
        // PruneStaleRobots GetOrCreateRobot может материализовать «призрака»
        // в произвольной точке — из-за этого луч «через раз» стартовал в
        // нескольких клетках от робота. Свой робот всегда рендерится по
        // предсказанию, поэтому для него берём живой transform.
        Vector3 start;
        if (_targetBot is Robot shooter && !shooter.IsLocalPlayer && shooter.HasServerPosition)
        {
            start = shooter.ServerPosition;
        }
        else
        {
            start = _targetBot != null
                ? _targetBot.transform.position
                : _intendedWorldPosition;
        }

        Vector3 delta = start - _intendedWorldPosition;
        float distance = delta.magnitude;
        if (distance > 1e-3f)
        {
            // Отступ «от дула»: сдвигаем начало ленты вперёд по ходу луча,
            // чтобы она не ложилась поверх спрайта стреляющего робота.
            delta -= (delta / distance) * BeamMuzzleInset;
        }

        return new Vector2(delta.x, delta.y);
    }

    private void UpdateBeam()
    {
        if (_beamObject == null || _beamMesh == null)
        {
            return;
        }

        // Случайное блуждание узлов с подтягиванием к прямой (0.9/0.1), как в легаси.
        _beamTail[0] = BeamStartLocal();
        _beamTail[BeamSectorCount] = Vector2.zero;
        for (int i = 1; i < BeamSectorCount; i++)
        {
            float d = (float)i / BeamSectorCount;
            Vector2 wobble = _beamTail[i]
                + new Vector2(UnityEngine.Random.value - 0.5f, UnityEngine.Random.value - 0.5f);
            _beamTail[i] = (0.9f * wobble) + (0.1f * (d * _beamTail[0]));
        }

        // Полураскрытие ленты, как в легаси: sin(7 * t), полуширина ужата вдвое.
        float half = BeamHalfWidth * Mathf.Sin(7f * _lifeTimer);

        for (int j = 0; j < BeamSectorCount; j++)
        {
            Vector2 toNext = (_beamTail[j + 1] - _beamTail[j]).normalized;
            Vector2 prevDir = j > 0
                ? (_beamTail[j + 1] - _beamTail[j - 1]).normalized
                : toNext;
            Vector2 nextDir = j < BeamSectorCount - 1
                ? (_beamTail[j + 2] - _beamTail[j]).normalized
                : toNext;
            Vector2 prevOffset = half * new Vector2(prevDir.y, -prevDir.x);
            Vector2 nextOffset = half * new Vector2(nextDir.y, -nextDir.x);

            _beamVertices[4 * j] = _beamTail[j] + prevOffset;
            _beamVertices[4 * j + 1] = _beamTail[j] - prevOffset;
            _beamVertices[4 * j + 2] = _beamTail[j + 1] + nextOffset;
            _beamVertices[4 * j + 3] = _beamTail[j + 1] - nextOffset;

            // Случайное окно 0.1 по ширине текстуры, во всю её высоту.
            float u = UnityEngine.Random.value * (1f - BeamUvWindow);
            _beamUvs[4 * j] = new Vector2(u, 0f);
            _beamUvs[4 * j + 1] = new Vector2(u, 1f);
            _beamUvs[4 * j + 2] = new Vector2(u + BeamUvWindow, 0f);
            _beamUvs[4 * j + 3] = new Vector2(u + BeamUvWindow, 1f);
        }

        _beamMesh.vertices = _beamVertices;
        _beamMesh.uv = _beamUvs;
        _beamMesh.colors = _beamColors;
        _beamMesh.bounds = new Bounds(Vector3.zero, new Vector3(44f, 44f, 44f));
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
        _flightReturning = false;
        if (_targetBot == null)
        {
            throw new InvalidOperationException("Crystal collection requires its target robot.");
        }

        _flightDestination = _targetBot.transform.position;
        _flightOrigin = _intendedWorldPosition;
        if (_gameObject != null)
        {
            // Иконка кристалла летит «как есть»: сбрасываем поворот, применённый
            // в SetupSlotPosition по направлению взгляда робота, и задаём масштаб.
            _gameObject.transform.rotation = Quaternion.identity;
            _gameObject.transform.localScale = Vector3.one *
                (_crystalCollectionVariant == CrystalCollectionVisualVariant.SourceMineralAtlas ? 1f : CrystalScale);
        }

        if (_crystalCollectionVariant == CrystalCollectionVisualVariant.SourceMineralAtlas)
        {
            CreateSourceCollectionSprite();
            _slot?.SetEnabled(false);
        }
        else
        {
            _slot?.SetSprite(_flightSprite);
            _slot?.SetEnabled(true);
        }
    }

    private void CreateSourceCollectionSprite()
    {
        if (_gameObject == null || _flightSprite == null)
        {
            throw new InvalidOperationException("Source crystal collection requires its world root and sprite.");
        }

        _sourceCollectionSpriteObject = CreateSourceCollectionSpriteObject(
            _sceneObjects,
            _gameObject.transform,
            _flightSprite);
    }

    internal static GameObject CreateSourceCollectionSpriteObject(
        ISceneObjectFactory sceneObjects,
        Transform parent,
        Sprite sprite)
    {
        GameObject spriteObject = sceneObjects.Create("CrystalCollectionMineral", RuntimeOwner.VisualEffects);
        try
        {
            spriteObject.transform.SetParent(parent, worldPositionStays: false);
            spriteObject.transform.localPosition = Vector3.zero;
            spriteObject.transform.localRotation = Quaternion.identity;
            spriteObject.transform.localScale = Vector3.one;

            SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 5;
            renderer.color = Color.white;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.maskInteraction = SpriteMaskInteraction.None;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return spriteObject;
        }
        catch
        {
            UnityEngine.Object.Destroy(spriteObject);
            throw;
        }
    }

    private void UpdateFlight()
    {
        if (!_flightActive)
        {
            return;
        }

        // CrystallPlus.Update, RVA 0x19B5710: test == 0 moves toward
        // moveFrom + (0, 0.25) at dt*8; test == 1 then moves to moveTo
        // at dt*12 and completes within 0.05 units.
        Vector3 current = _gameObject != null
            ? _gameObject.transform.position
            : _flightOrigin;
        Vector3 position = AdvanceCrystalFlightSourceLogic(
            current,
            _flightOrigin,
            _flightDestination,
            ref _flightReturning,
            Time.deltaTime,
            out bool completed);

        if (_gameObject != null)
        {
            _gameObject.transform.position = position;
        }

        Vector3 digitBase = position + new Vector3(_digitRowOffset, _digitRowVerticalOffset, 0f);
        _collectionLabel?.SetPosition(digitBase);
        for (int i = 0; i < _digitObjects.Count; i++)
        {
            if (_digitObjects[i] != null)
            {
                _digitObjects[i].transform.position =
                    digitBase + (Vector3.right * (_digitStep * i));
            }
        }

        if (completed)
        {
            _flightActive = false;
            _flightMode = false;
            _visualCompleted = true;
        }
    }

    internal static Vector3 AdvanceCrystalFlightSourceLogic(
        Vector3 current,
        Vector3 origin,
        Vector3 destination,
        ref bool returning,
        float deltaTime,
        out bool completed)
    {
        Vector3 target = returning ? destination : origin + (Vector3.up * 0.25f);
        float speed = returning ? 12f : 8f;
        Vector3 next = Vector3.Lerp(current, target, deltaTime * speed);
        completed = false;

        if (Vector2.Distance(next, target) < 0.05f)
        {
            if (returning)
            {
                completed = true;
            }
            else
            {
                returning = true;
            }
        }

        return next;
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
            _sourceBot = FindExistingRobot(_parsedParams.SourceBotId);
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

        if (_targetBotId != 0)
        {
            _targetBot = FindExistingRobot(_targetBotId);
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

        if (_localFacingAngle.HasValue && _gameObject != null)
        {
            _gameObject.transform.rotation = Quaternion.Euler(0f, 0f, _localFacingAngle.Value);
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

    }

    private void SubmitEmission()
    {
        if (_light != null && _gameObject != null && _currentFrame < _frameEmission.Length)
        {
            SpriteEmissionFrame frame = _frameEmission[_currentFrame];
            _light.Submit(_gameObject.transform.TransformPoint(new Vector3(frame.Centroid.x, frame.Centroid.y, 0f)),
                _gameObject.transform.lossyScale, frame.Radiance);
        }
    }

    private void ReleaseSlot()
    {
        _light?.Dispose();
        _collectionLabel?.Dispose();
        _collectionLabel = null;
        if (_sourceCollectionSpriteObject != null)
        {
            UnityEngine.Object.Destroy(_sourceCollectionSpriteObject);
            _sourceCollectionSpriteObject = null;
        }
        if (_sourceCollectionCanvasObject != null)
        {
            UnityEngine.Object.Destroy(_sourceCollectionCanvasObject);
            _sourceCollectionCanvasObject = null;
        }
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

        for (int i = 0; i < _digitHandles.Count; i++)
        {
            _batchRenderer.UnregisterSprite(_digitHandles[i]);
        }

        for (int i = 0; i < _digitObjects.Count; i++)
        {
            if (_digitObjects[i] != null)
            {
                UnityEngine.Object.Destroy(_digitObjects[i]);
            }
        }

        for (int i = 0; i < _digitSprites.Count; i++)
        {
            if (_digitSprites[i] != null)
            {
                UnityEngine.Object.Destroy(_digitSprites[i]);
            }
        }

        _digitObjects.Clear();
        _digitHandles.Clear();
        _digitSprites.Clear();

        _beamMode = false;
        if (_beamObject != null)
        {
            UnityEngine.Object.Destroy(_beamObject);
            _beamObject = null;
        }

        if (_beamMaterial != null)
        {
            UnityEngine.Object.Destroy(_beamMaterial);
            _beamMaterial = null;
        }

        if (_beamMesh != null)
        {
            UnityEngine.Object.Destroy(_beamMesh);
            _beamMesh = null;
        }

        if (_ownedStaticSprite != null && !_ownedStaticSpriteShared)
        {
            UnityEngine.Object.Destroy(_ownedStaticSprite);
            _ownedStaticSprite = null;
        }

        if (_flightSprite != null && !_flightSpriteShared)
        {
            UnityEngine.Object.Destroy(_flightSprite);
            _flightSprite = null;
        }

        _gameObject = null;
        _sourceBot = null;
        _targetBot = null;
    }
}
