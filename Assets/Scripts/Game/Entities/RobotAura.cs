#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.World;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Кольцо вращающихся стрелок вплотную вокруг робота.
/// </summary>
/// <remarks>
/// ПОЧЕМУ НЕ EFFEKSEER. Путь .efk в клиенте есть, но ни одного файла
/// эффекта в проекте нет, а собрать его можно только в настольном
/// редакторе Effekseer. Здесь геометрия простая и целиком описывается
/// числами, поэтому эффект живёт в коде: он работает сразу, правится
/// константами ниже и не тянет за собой бинарный ассет.
///
/// РАЗМЕРЫ В ПИКСЕЛЯХ, А НЕ В ЮНИТАХ. Клетка мира — 32 пикселя при 16
/// пикселях на юнит, скин робота ровно 32×32, то есть тело занимает два
/// юнита и торчит на юнит от центра. Радиус кольца задан в пикселях той
/// же сетки: так «вплотную к роботу» остаётся вплотную, даже если
/// поменяется масштаб мира.
/// </remarks>
internal sealed class RobotAura
{
    /// <summary>Стрелок по кругу.</summary>
    private const int ArrowCount = 6;

    /// <summary>
    /// Радиус кольца в пикселях сетки. Половина тела — 16 пикселей,
    /// значит 20 оставляет зазор в четыре пикселя: кольцо идёт по самому
    /// краю робота, не задевая его.
    /// </summary>
    private const float RingRadiusPixels = 20f;

    /// <summary>Длина стрелки вдоль касательной, в пикселях сетки.</summary>
    private const int ArrowLengthPixels = 9;

    /// <summary>
    /// Толщина стрелки поперёк кольца, в пикселях сетки. Это и есть
    /// «толщина» ауры: пять пикселей против тридцати двух у тела.
    /// </summary>
    private const int ArrowThicknessPixels = 5;

    /// <summary>Оборотов кольца в секунду.</summary>
    private const float RevolutionsPerSecond = 0.4f;

    /// <summary>
    /// Порядок сортировки. Тело робота — ноль, иконка клана — сто:
    /// кольцо идёт над телом, но под кланом.
    /// </summary>
    private const int AuraSortingOrder = 50;

    private static readonly Color AuraColor = new(0.55f, 0.85f, 1f, 0.85f);

    private static Sprite? _sharedArrowSprite;

    private readonly Transform _robotTransform;
    private readonly WorldEntityBatchRenderer.SpriteHandle?[] _handles = new WorldEntityBatchRenderer.SpriteHandle?[ArrowCount];

    private WorldEntityBatchRenderer? _batchRenderer;
    private Transform? _ringTransform;
    private float _spinDegrees;
    private bool _visible;

    public RobotAura(Transform robotTransform)
    {
        _robotTransform = robotTransform;
    }

    /// <summary>
    /// Общий спрайт стрелки переживает выход из режима игры, а текстура —
    /// нет: следующий заход получил бы ссылку на уничтоженный объект.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForDomainReload()
    {
        _sharedArrowSprite = null;
    }

    public void SetVisible(bool visible, WorldEntityBatchRenderer? batchRenderer, ISceneObjectFactory? sceneObjects)
    {
        if (visible && _ringTransform == null)
        {
            if (batchRenderer == null || sceneObjects == null)
            {
                return;
            }

            Build(batchRenderer, sceneObjects);
        }

        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        for (int i = 0; i < _handles.Length; i++)
        {
            _handles[i]?.SetEnabled(visible);
        }
    }

    /// <summary>Крутит кольцо. Вызывать раз в кадр, пока аура видна.</summary>
    public void Tick(float deltaTime)
    {
        if (!_visible || _ringTransform == null)
        {
            return;
        }

        _spinDegrees = Mathf.Repeat(_spinDegrees + (RevolutionsPerSecond * 360f * deltaTime), 360f);

        // Поворот задаётся в мировых координатах намеренно. Кольцо — дитя
        // робота, чтобы ездить за ним, но робот сам крутится по
        // направлению движения, и локальный поворот сложился бы с его
        // разворотом: аура дёргалась бы при каждом повороте.
        _ringTransform.rotation = Quaternion.Euler(0f, 0f, _spinDegrees);
    }

    public void Destroy()
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            _batchRenderer?.UnregisterSprite(_handles[i]);
            _handles[i] = null;
        }

        if (_ringTransform != null)
        {
            Object.Destroy(_ringTransform.gameObject);
            _ringTransform = null;
        }

        _batchRenderer = null;
        _visible = false;
    }

    private void Build(WorldEntityBatchRenderer batchRenderer, ISceneObjectFactory sceneObjects)
    {
        _batchRenderer = batchRenderer;
        GameObject ring = sceneObjects.Create("Aura", RuntimeOwner.Robots);
        ring.transform.SetParent(_robotTransform, worldPositionStays: false);
        _ringTransform = ring.transform;

        Sprite arrow = EnsureArrowSprite();
        float radiusUnits = RingRadiusPixels / RenderingConstants.PIXELS_PER_UNIT;

        for (int i = 0; i < ArrowCount; i++)
        {
            float angleDegrees = i * (360f / ArrowCount);
            float angleRadians = angleDegrees * Mathf.Deg2Rad;

            GameObject arrowObject = sceneObjects.Create($"AuraArrow{i}", RuntimeOwner.Robots);
            Transform arrowTransform = arrowObject.transform;
            arrowTransform.SetParent(_ringTransform, worldPositionStays: false);
            arrowTransform.localPosition = new Vector3(
                Mathf.Cos(angleRadians) * radiusUnits,
                Mathf.Sin(angleRadians) * radiusUnits,
                0f);

            // Спрайт нарисован остриём вдоль +X, а лететь он должен по
            // касательной к окружности — это радиус плюс девяносто.
            arrowTransform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees + 90f);

            WorldEntityBatchRenderer.SpriteHandle handle =
                batchRenderer.RegisterSprite(arrowTransform, AuraSortingOrder);
            batchRenderer.SetSprite(handle, arrow);
            handle.SetColor(AuraColor);
            handle.SetEnabled(false);
            _handles[i] = handle;
        }
    }

    /// <summary>
    /// Рисует стрелку в текстуру.
    /// </summary>
    /// <remarks>
    /// Спрайт процедурный и общий на всех роботов. Отдельный PNG здесь был
    /// бы новым художественным ассетом со всеми вытекающими — исходником,
    /// печатью, местом в библии, — тогда как форма стрелки полностью
    /// описывается тремя числами выше.
    /// </remarks>
    private static Sprite EnsureArrowSprite()
    {
        if (_sharedArrowSprite != null)
        {
            return _sharedArrowSprite;
        }

        // Пиксель-арт: сглаживание размыло бы девятипиксельную стрелку в
        // пятно, а повтор по краям дал бы кайму на прозрачном фоне.
        Texture2D texture = RuntimeTextureFactory.CreateRgba32NoMip(
            ArrowLengthPixels,
            ArrowThicknessPixels,
            "RobotAuraArrow",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        int centerRow = ArrowThicknessPixels / 2;
        int shaftHalfThickness = Mathf.Max(0, (ArrowThicknessPixels / 2) - 1);
        int headStartColumn = ArrowLengthPixels - 4;

        for (int x = 0; x < ArrowLengthPixels; x++)
        {
            // Древко ровной толщины, остриё сходится на нет к последнему
            // столбцу: половина толщины убывает вместе с расстоянием до
            // кончика.
            int halfThickness = x < headStartColumn
                ? shaftHalfThickness
                : Mathf.Min(centerRow, ArrowLengthPixels - 1 - x);

            for (int y = 0; y < ArrowThicknessPixels; y++)
            {
                bool inside = Mathf.Abs(y - centerRow) <= halfThickness;
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        _sharedArrowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, ArrowLengthPixels, ArrowThicknessPixels),
            new Vector2(0.5f, 0.5f),
            RenderingConstants.PIXELS_PER_UNIT);
        _sharedArrowSprite.name = "RobotAuraArrow";
        return _sharedArrowSprite;
    }
}
