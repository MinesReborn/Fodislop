#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.World;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Магическая аура вокруг робота: летающие светящиеся нити.
/// </summary>
/// <remarks>
/// НИТЬ СОБИРАЕТСЯ ИЗ ЗВЕНЬЕВ. Одна нить — это цепочка коротких отрезков,
/// расставленных по дуге, а не один длинный спрайт. Длинный спрайт — это
/// прямая хорда: на радиусе в два десятка пикселей она заметно срезает
/// окружность, и вместо облетающей робота линии получается многоугольник.
/// Звенья же поворачиваются каждое по своей касательной, и нить реально
/// огибает тело.
///
/// Прозрачность вдоль нити убывает от головы к хвосту — так линия читается
/// как летящая, а не как неподвижная дужка.
///
/// ЗАЖИГАЕТСЯ И ГАСНЕТ НЕ МГНОВЕННО. По нажатию нити вспыхивают и
/// расходятся наружу, по отпусканию медленнее гаснут и стягиваются к телу.
/// Атака заметно короче релиза: заклинание вспыхивает резко, а
/// рассеивается неохотно. Равные времена дают ощущение тумблера.
///
/// РАЗМЕРЫ В ПИКСЕЛЯХ СЕТКИ, А НЕ В ЮНИТАХ. Клетка мира — 32 пикселя при
/// 16 пикселях на юнит, скин робота ровно 32x32, то есть тело торчит на 16
/// пикселей от центра. Радиусы заданы в пикселях той же сетки: так
/// «вплотную к роботу» остаётся вплотную при любом масштабе мира.
/// </remarks>
internal sealed class RobotAura
{
    /// <summary>Нитей в ауре.</summary>
    private const int WispCount = 10;

    /// <summary>
    /// Звеньев в нити. Пять звеньев дают дугу примерно в четверть радиана —
    /// уже линия, ещё не кольцо.
    /// </summary>
    private const int SegmentsPerWisp = 5;

    /// <summary>Угол между соседними звеньями нити, градусы.</summary>
    private const float SegmentSpacingDegrees = 5.5f;

    /// <summary>
    /// Радиус ауры в покое, в пикселях сетки. Половина тела — 16, так что
    /// в стянутом виде нити прижаты к самому корпусу.
    /// </summary>
    private const float InnerRadiusPixels = 13f;

    /// <summary>Радиус раскрытой ауры, в пикселях сетки.</summary>
    private const float OuterRadiusPixels = 20f;

    /// <summary>
    /// Разброс радиуса между нитями, в долях. Держится небольшим: широкий
    /// разброс разрежает ауру в облако, а она должна быть плотной.
    /// </summary>
    private const float RadiusJitter = 0.16f;

    /// <summary>Оборотов вокруг робота в секунду, средняя.</summary>
    private const float RevolutionsPerSecond = 0.34f;

    /// <summary>
    /// Разброс скоростей между нитями. Без него вся аура вращается как
    /// одно жёсткое колесо: рисунок внутри неё остаётся неподвижным.
    /// </summary>
    private const float SpeedJitter = 0.45f;

    /// <summary>Время выхода на полную яркость, секунды.</summary>
    private const float AttackSeconds = 0.18f;

    /// <summary>Время затухания после отпускания, секунды.</summary>
    private const float ReleaseSeconds = 0.5f;

    /// <summary>Длина звена вдоль касательной, пикселей.</summary>
    private const int SegmentLengthPixels = 5;

    /// <summary>Толщина звена поперёк, пикселей.</summary>
    private const int SegmentThicknessPixels = 3;

    /// <summary>
    /// Порядок сортировки. Тело робота — ноль, иконка клана — сто: нити
    /// вьются над телом, но под кланом.
    /// </summary>
    private const int AuraSortingOrder = 50;

    /// <summary>
    /// Цвета нитей. Магия читается по холодной части спектра, а разнобой
    /// оттенков не даёт ауре выглядеть перекрашенной копией одной дуги.
    /// </summary>
    private static readonly Color[] WispTints =
    [
        new(0.55f, 0.80f, 1.00f, 1f),
        new(0.76f, 0.62f, 1.00f, 1f),
        new(0.90f, 0.95f, 1.00f, 1f),
    ];

    private static Sprite? _sharedSegmentSprite;

    private readonly Transform _robotTransform;
    private readonly Wisp[] _wisps = new Wisp[WispCount];

    private WorldEntityBatchRenderer? _batchRenderer;
    private Transform? _auraTransform;
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
        _sharedSegmentSprite = null;
    }

    /// <summary>
    /// Задаёт, держат ли клавишу. Гашение не мгновенное: см. релиз.
    /// </summary>
    public void SetWanted(bool wanted, WorldEntityBatchRenderer? batchRenderer, ISceneObjectFactory? sceneObjects)
    {
        if (wanted && _auraTransform == null)
        {
            if (batchRenderer == null || sceneObjects == null)
            {
                return;
            }

            Build(batchRenderer, sceneObjects);
        }

        _wanted = wanted;
    }

    /// <summary>Двигает нити и огибающую. Вызывать раз в кадр.</summary>
    public void Tick(float deltaTime)
    {
        if (_auraTransform == null)
        {
            return;
        }

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

        // Сглаживание концов: у линейной огибающей скорость обрывается
        // скачком, и это заметно щёлкает на старте и в самом конце.
        float eased = _energy * _energy * (3f - (2f * _energy));
        float radiusPixels = Mathf.Lerp(InnerRadiusPixels, OuterRadiusPixels, eased);

        // Разворот робота не должен участвовать: аура дочерняя к нему,
        // чтобы ездить следом, но крутится она сама по себе.
        _auraTransform.rotation = Quaternion.identity;

        for (int i = 0; i < _wisps.Length; i++)
        {
            UpdateWisp(_wisps[i], radiusPixels, eased);
        }
    }

    public void Destroy()
    {
        for (int i = 0; i < _wisps.Length; i++)
        {
            Wisp wisp = _wisps[i];
            if (wisp.Segments != null)
            {
                for (int s = 0; s < wisp.Segments.Length; s++)
                {
                    _batchRenderer?.UnregisterSprite(wisp.Segments[s].Handle);
                }
            }

            _wisps[i] = default;
        }

        if (_auraTransform != null)
        {
            Object.Destroy(_auraTransform.gameObject);
            _auraTransform = null;
        }

        _batchRenderer = null;
        _energy = 0f;
        _wanted = false;
    }

    private void UpdateWisp(Wisp wisp, float radiusPixels, float eased)
    {
        if (wisp.Segments == null)
        {
            return;
        }

        float headAngle = wisp.StartAngle + (_time * wisp.AngularSpeed);
        float radiusUnits = radiusPixels * wisp.RadiusScale / RenderingConstants.PIXELS_PER_UNIT;

        // Пульсация яркости всей нити: аура дышит, а не горит ровным светом.
        float pulse = 0.72f + (0.28f * Mathf.Sin((_time * wisp.PulseSpeed) + wisp.PulsePhase));

        for (int s = 0; s < wisp.Segments.Length; s++)
        {
            Segment segment = wisp.Segments[s];
            if (segment.Transform == null || segment.Handle == null)
            {
                continue;
            }

            // Хвост отстаёт от головы против хода нити — иначе линия
            // выглядела бы летящей задом наперёд.
            float angle = headAngle -
                (Mathf.Sign(wisp.AngularSpeed) * s * SegmentSpacingDegrees * Mathf.Deg2Rad);

            segment.Transform.localPosition = new Vector3(
                Mathf.Cos(angle) * radiusUnits,
                Mathf.Sin(angle) * radiusUnits,
                0f);

            // Спрайт звена вытянут вдоль +X, а лежать оно должно по
            // касательной к окружности — это радиус плюс девяносто.
            segment.Transform.localRotation =
                Quaternion.Euler(0f, 0f, (angle * Mathf.Rad2Deg) + 90f);

            // Убывание к хвосту: голова яркая, конец растворяется.
            float taper = 1f - (s / (float)wisp.Segments.Length);
            Color tint = wisp.Tint;
            tint.a = eased * pulse * taper * taper;
            segment.Handle.SetColor(tint);
        }
    }

    private void SetHandlesEnabled(bool enabled)
    {
        for (int i = 0; i < _wisps.Length; i++)
        {
            Segment[]? segments = _wisps[i].Segments;
            if (segments == null)
            {
                continue;
            }

            for (int s = 0; s < segments.Length; s++)
            {
                segments[s].Handle?.SetEnabled(enabled);
            }
        }
    }

    private void Build(WorldEntityBatchRenderer batchRenderer, ISceneObjectFactory sceneObjects)
    {
        _batchRenderer = batchRenderer;
        GameObject aura = sceneObjects.Create("Aura", RuntimeOwner.Robots);
        aura.transform.SetParent(_robotTransform, worldPositionStays: false);
        _auraTransform = aura.transform;

        Sprite segmentSprite = EnsureSegmentSprite();

        // Зерно от идентификатора объекта: у двух роботов рядом ауры
        // должны отличаться, но у одного робота рисунок обязан быть одним
        // и тем же от показа к показу. Идентификатор берётся хешем, а не
        // приведением к int: в EntityId оно объявлено устаревшим, потому
        // что в int он скоро перестанет помещаться.
        var random = new System.Random(_robotTransform.GetEntityId().GetHashCode());

        for (int i = 0; i < WispCount; i++)
        {
            float direction = random.Next(2) == 0 ? 1f : -1f;
            var segments = new Segment[SegmentsPerWisp];
            for (int s = 0; s < SegmentsPerWisp; s++)
            {
                GameObject segmentObject = sceneObjects.Create($"AuraWisp{i}Segment{s}", RuntimeOwner.Robots);
                Transform segmentTransform = segmentObject.transform;
                segmentTransform.SetParent(_auraTransform, worldPositionStays: false);

                WorldEntityBatchRenderer.SpriteHandle handle =
                    batchRenderer.RegisterSprite(segmentTransform, AuraSortingOrder);
                batchRenderer.SetSprite(handle, segmentSprite);
                handle.SetEnabled(false);
                segments[s] = new Segment { Transform = segmentTransform, Handle = handle };
            }

            _wisps[i] = new Wisp
            {
                Segments = segments,

                // Углы раскидываются равномерно со сдвигом, а не случайно:
                // десяток случайных углов регулярно оставляет проплешину в
                // полкруга, а аура должна быть плотной по всей окружности.
                StartAngle = ((i / (float)WispCount) + (NextUnit(random) * 0.6f / WispCount)) * Mathf.PI * 2f,
                AngularSpeed = direction * RevolutionsPerSecond * Mathf.PI * 2f *
                    (1f + ((NextUnit(random) - 0.5f) * 2f * SpeedJitter)),
                RadiusScale = 1f + ((NextUnit(random) - 0.5f) * 2f * RadiusJitter),
                PulseSpeed = 1.6f + (NextUnit(random) * 2.4f),
                PulsePhase = NextUnit(random) * Mathf.PI * 2f,
                Tint = WispTints[i % WispTints.Length],
            };
        }
    }

    private static float NextUnit(System.Random random) => (float)random.NextDouble();

    /// <summary>
    /// Рисует звено нити: короткий отрезок, размытый поперёк.
    /// </summary>
    /// <remarks>
    /// Спрайт процедурный и общий на все ауры. Поперёк альфа спадает от
    /// середины к краю, вдоль — держится: так звенья, встав в цепочку,
    /// сливаются в сплошную светящуюся линию без перемычек и утолщений на
    /// стыках, а сама линия остаётся мягкой по краям.
    /// </remarks>
    private static Sprite EnsureSegmentSprite()
    {
        if (_sharedSegmentSprite != null)
        {
            return _sharedSegmentSprite;
        }

        // Сглаживание размыло бы отрезок в пять пикселей, повтор по краям
        // дал бы кайму на прозрачном фоне.
        Texture2D texture = RuntimeTextureFactory.CreateRgba32NoMip(
            SegmentLengthPixels,
            SegmentThicknessPixels,
            "RobotAuraWisp",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        float centerRow = (SegmentThicknessPixels - 1) / 2f;
        float maxDistance = centerRow + 0.5f;

        for (int x = 0; x < SegmentLengthPixels; x++)
        {
            for (int y = 0; y < SegmentThicknessPixels; y++)
            {
                float distance = Mathf.Abs(y - centerRow);
                float falloff = Mathf.Clamp01(1f - (distance / maxDistance));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff));
            }
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        _sharedSegmentSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, SegmentLengthPixels, SegmentThicknessPixels),
            new Vector2(0.5f, 0.5f),
            RenderingConstants.PIXELS_PER_UNIT);
        _sharedSegmentSprite.name = "RobotAuraWisp";
        return _sharedSegmentSprite;
    }

    private struct Segment
    {
        public Transform? Transform;
        public WorldEntityBatchRenderer.SpriteHandle? Handle;
    }

    private struct Wisp
    {
        public Segment[]? Segments;
        public float StartAngle;
        public float AngularSpeed;
        public float RadiusScale;
        public float PulseSpeed;
        public float PulsePhase;
        public Color Tint;
    }
}
