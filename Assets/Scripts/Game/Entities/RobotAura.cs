#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.World;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Магическая аура вокруг робота: облако пылинок, кружащих вплотную к телу.
/// </summary>
/// <remarks>
/// ЗАЖИГАЕТСЯ И ГАСНЕТ НЕ МГНОВЕННО. У ауры есть атака и релиз: по нажатию
/// пыль вспыхивает и раздаётся наружу за доли секунды, по отпусканию
/// медленнее гаснет и втягивается обратно. Мгновенное появление и
/// исчезновение читается как ошибка отрисовки, а не как заклинание,
/// поэтому огибающая здесь не украшение, а само существо эффекта.
///
/// ПОЧЕМУ НЕ EFFEKSEER. Путь .efk в клиенте есть, но ни одного файла
/// эффекта в проекте нет, а собрать его можно только в настольном
/// редакторе. Здесь всё описывается числами, поэтому эффект живёт в коде.
///
/// РАЗМЕРЫ В ПИКСЕЛЯХ СЕТКИ, А НЕ В ЮНИТАХ. Клетка мира — 32 пикселя при
/// 16 пикселях на юнит, скин робота ровно 32x32, то есть тело торчит на 16
/// пикселей от центра. Радиусы заданы в пикселях той же сетки: так
/// «вплотную к роботу» остаётся вплотную при любом масштабе мира.
/// </remarks>
internal sealed class RobotAura
{
    /// <summary>Пылинок в облаке.</summary>
    private const int MoteCount = 20;

    /// <summary>
    /// Радиус облака в покое, в пикселях сетки. Половина тела — 16, так
    /// что в свёрнутом виде пыль прячется у самого корпуса.
    /// </summary>
    private const float InnerRadiusPixels = 13f;

    /// <summary>Радиус раскрытого облака, в пикселях сетки.</summary>
    private const float OuterRadiusPixels = 21f;

    /// <summary>
    /// Разброс радиуса между пылинками, в долях. Без него облако
    /// вырождается в кольцо: у пыли не должно быть чёткого края.
    /// </summary>
    private const float RadiusJitter = 0.28f;

    /// <summary>Оборотов вокруг робота в секунду, средняя.</summary>
    private const float RevolutionsPerSecond = 0.22f;

    /// <summary>
    /// Разброс скоростей между пылинками. Одинаковая скорость превращает
    /// облако в жёсткую вертушку — рисунок остаётся неподвижным, вращается
    /// только он целиком.
    /// </summary>
    private const float SpeedJitter = 0.55f;

    /// <summary>Размах вертикального покачивания, в пикселях сетки.</summary>
    private const float BobPixels = 2.5f;

    /// <summary>Время выхода на полную яркость, секунды.</summary>
    private const float AttackSeconds = 0.18f;

    /// <summary>Время затухания после отпускания, секунды.</summary>
    private const float ReleaseSeconds = 0.5f;

    /// <summary>Сторона текстуры пылинки, пикселей.</summary>
    private const int MoteSizePixels = 7;

    /// <summary>
    /// Порядок сортировки. Тело робота — ноль, иконка клана — сто: пыль
    /// вьётся над телом, но под кланом.
    /// </summary>
    private const int AuraSortingOrder = 50;

    /// <summary>
    /// Цвета пылинок. Магия читается по холодной части спектра, а разнобой
    /// оттенков не даёт облаку выглядеть перекрашенной копией одного пятна.
    /// </summary>
    private static readonly Color[] MoteTints =
    [
        new(0.60f, 0.80f, 1.00f, 1f),
        new(0.78f, 0.66f, 1.00f, 1f),
        new(0.92f, 0.94f, 1.00f, 1f),
    ];

    private static Sprite? _sharedMoteSprite;

    private readonly Transform _robotTransform;
    private readonly Mote[] _motes = new Mote[MoteCount];

    private WorldEntityBatchRenderer? _batchRenderer;
    private Transform? _cloudTransform;
    private float _energy;
    private bool _wanted;
    private float _time;

    public RobotAura(Transform robotTransform)
    {
        _robotTransform = robotTransform;
    }

    /// <summary>Аура ещё видна: горит или доигрывает затухание.</summary>
    public bool IsAlive => _energy > 0.001f;

    /// <summary>
    /// Общий спрайт переживает выход из режима игры, а его текстура — нет:
    /// следующий заход получил бы ссылку на уничтоженный объект.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        _sharedMoteSprite = null;
    }

    /// <summary>
    /// Задаёт, держат ли клавишу. Гашение не мгновенное: см. релиз.
    /// </summary>
    public void SetWanted(bool wanted, WorldEntityBatchRenderer? batchRenderer, ISceneObjectFactory? sceneObjects)
    {
        if (wanted && _cloudTransform == null)
        {
            if (batchRenderer == null || sceneObjects == null)
            {
                return;
            }

            Build(batchRenderer, sceneObjects);
        }

        _wanted = wanted;
    }

    /// <summary>Двигает облако и огибающую. Вызывать раз в кадр.</summary>
    public void Tick(float deltaTime)
    {
        if (_cloudTransform == null)
        {
            return;
        }

        // Атака короче релиза намеренно: заклинание вспыхивает резко, а
        // рассеивается неохотно. Равные времена дают ощущение тумблера.
        float rate = _wanted
            ? deltaTime / Mathf.Max(0.0001f, AttackSeconds)
            : -deltaTime / Mathf.Max(0.0001f, ReleaseSeconds);
        float previousEnergy = _energy;
        _energy = Mathf.Clamp01(_energy + rate);

        if (_energy <= 0f)
        {
            if (previousEnergy > 0f)
            {
                SetHandlesEnabled(false);
            }

            return;
        }

        if (previousEnergy <= 0f)
        {
            SetHandlesEnabled(true);
        }

        _time += deltaTime;

        // Сглаживание концов: линейная огибающая заметно «щёлкает» на
        // старте и в самом конце, потому что скорость обрывается скачком.
        float eased = _energy * _energy * (3f - (2f * _energy));

        float radiusPixels = Mathf.Lerp(InnerRadiusPixels, OuterRadiusPixels, eased);
        float bobUnits = BobPixels / RenderingConstants.PIXELS_PER_UNIT;

        // Поворот облака берётся в мировых координатах намеренно: оно
        // дочернее к роботу, чтобы ездить за ним, но робот разворачивается
        // по направлению движения, и его разворот сложился бы с кружением.
        _cloudTransform.rotation = Quaternion.identity;

        for (int i = 0; i < _motes.Length; i++)
        {
            Mote mote = _motes[i];
            if (mote.Transform == null || mote.Handle == null)
            {
                continue;
            }

            float angle = mote.StartAngle + (_time * mote.AngularSpeed);
            float radiusUnits = radiusPixels * mote.RadiusScale / RenderingConstants.PIXELS_PER_UNIT;
            float bob = Mathf.Sin((_time * mote.BobSpeed) + mote.BobPhase) * bobUnits * eased;

            mote.Transform.localPosition = new Vector3(
                Mathf.Cos(angle) * radiusUnits,
                (Mathf.Sin(angle) * radiusUnits) + bob,
                0f);

            // Мерцание: без него пылинки читаются как твёрдые точки на
            // орбите, а не как взвесь.
            float twinkle = 0.55f + (0.45f * Mathf.Sin((_time * mote.TwinkleSpeed) + mote.TwinklePhase));
            Color tint = mote.Tint;
            tint.a = eased * twinkle;
            mote.Handle.SetColor(tint);
        }
    }

    public void Destroy()
    {
        for (int i = 0; i < _motes.Length; i++)
        {
            _batchRenderer?.UnregisterSprite(_motes[i].Handle);
            _motes[i] = default;
        }

        if (_cloudTransform != null)
        {
            Object.Destroy(_cloudTransform.gameObject);
            _cloudTransform = null;
        }

        _batchRenderer = null;
        _energy = 0f;
        _wanted = false;
    }

    private void SetHandlesEnabled(bool enabled)
    {
        for (int i = 0; i < _motes.Length; i++)
        {
            _motes[i].Handle?.SetEnabled(enabled);
        }
    }

    private void Build(WorldEntityBatchRenderer batchRenderer, ISceneObjectFactory sceneObjects)
    {
        _batchRenderer = batchRenderer;
        GameObject cloud = sceneObjects.Create("Aura", RuntimeOwner.Robots);
        cloud.transform.SetParent(_robotTransform, worldPositionStays: false);
        _cloudTransform = cloud.transform;

        Sprite moteSprite = EnsureMoteSprite();

        // Зерно от идентификатора объекта: у двух роботов рядом облака
        // должны отличаться, но у одного робота рисунок обязан быть одним
        // и тем же от показа к показу. Идентификатор берётся хешем, а не
        // приведением к int: в EntityId оно объявлено устаревшим, потому
        // что в int он скоро перестанет помещаться.
        var random = new System.Random(_robotTransform.GetEntityId().GetHashCode());

        for (int i = 0; i < MoteCount; i++)
        {
            GameObject moteObject = sceneObjects.Create($"AuraMote{i}", RuntimeOwner.Robots);
            Transform moteTransform = moteObject.transform;
            moteTransform.SetParent(_cloudTransform, worldPositionStays: false);

            WorldEntityBatchRenderer.SpriteHandle handle =
                batchRenderer.RegisterSprite(moteTransform, AuraSortingOrder);
            batchRenderer.SetSprite(handle, moteSprite);
            handle.SetEnabled(false);

            float direction = random.Next(2) == 0 ? 1f : -1f;
            _motes[i] = new Mote
            {
                Transform = moteTransform,
                Handle = handle,

                // Углы раскидываются равномерно со сдвигом, а не случайно:
                // случайные углы на двух десятках точек регулярно дают
                // проплешину в полкруга.
                StartAngle = ((i / (float)MoteCount) + (NextUnit(random) * 0.5f / MoteCount)) * Mathf.PI * 2f,
                AngularSpeed = direction * RevolutionsPerSecond * Mathf.PI * 2f *
                    (1f + ((NextUnit(random) - 0.5f) * 2f * SpeedJitter)),
                RadiusScale = 1f + ((NextUnit(random) - 0.5f) * 2f * RadiusJitter),
                BobSpeed = 1.4f + (NextUnit(random) * 1.8f),
                BobPhase = NextUnit(random) * Mathf.PI * 2f,
                TwinkleSpeed = 2.2f + (NextUnit(random) * 3.5f),
                TwinklePhase = NextUnit(random) * Mathf.PI * 2f,
                Tint = MoteTints[i % MoteTints.Length],
            };
        }
    }

    private static float NextUnit(System.Random random) => (float)random.NextDouble();

    /// <summary>
    /// Рисует пылинку: мягкое пятно, гаснущее к краю.
    /// </summary>
    /// <remarks>
    /// Спрайт процедурный и общий на всех роботов. Резкий круг на семи
    /// пикселях выглядит гайкой, поэтому альфа падает от центра к краю по
    /// квадрату — пятно без обвода, из какого и складывается взвесь.
    /// </remarks>
    private static Sprite EnsureMoteSprite()
    {
        if (_sharedMoteSprite != null)
        {
            return _sharedMoteSprite;
        }

        // Сглаживание размыло бы семипиксельное пятно, повтор по краям дал
        // бы кайму на прозрачном фоне.
        Texture2D texture = RuntimeTextureFactory.CreateRgba32NoMip(
            MoteSizePixels,
            MoteSizePixels,
            "RobotAuraMote",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        float center = (MoteSizePixels - 1) / 2f;
        float maxDistance = center + 0.5f;

        for (int x = 0; x < MoteSizePixels; x++)
        {
            for (int y = 0; y < MoteSizePixels; y++)
            {
                float distance = Mathf.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
                float falloff = Mathf.Clamp01(1f - (distance / maxDistance));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff * falloff));
            }
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        _sharedMoteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, MoteSizePixels, MoteSizePixels),
            new Vector2(0.5f, 0.5f),
            RenderingConstants.PIXELS_PER_UNIT);
        _sharedMoteSprite.name = "RobotAuraMote";
        return _sharedMoteSprite;
    }

    private struct Mote
    {
        public Transform? Transform;
        public WorldEntityBatchRenderer.SpriteHandle? Handle;
        public float StartAngle;
        public float AngularSpeed;
        public float RadiusScale;
        public float BobSpeed;
        public float BobPhase;
        public float TwinkleSpeed;
        public float TwinklePhase;
        public Color Tint;
    }
}
