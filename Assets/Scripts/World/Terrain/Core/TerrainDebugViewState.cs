#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

// Отладочные виды террейна, отдельно от световых.
//
// Световой дебаг принадлежит lighting. Этот список разделяет геометрию
// клетки, контактное затенение и этапы поверхностного цвета: текстуру,
// анимацию и декаль.
//
// Раскладка обязана совпадать с TerrainDebugView.hlsl: номера едут в шейдер
// как есть.
public enum TerrainDebugView
{
    Off = 0,
    ReliefRim = 1,
    ForeignSides = 2,
    Coverage = 3,
    Layer = 4,
    Anchored = 5,
    CellLocal = 6,
    ReliefGroup = 7,
    ContinuousSheet = 8,
    AmbientOcclusion = 9,
    BackgroundTileIdentity = 10,
    FacetedGlint = 11,
    SourceAlbedo = 12,
    AnimatedColor = 13,
    PrismaticTint = 14,
    DecalContribution = 15,
    ShimmerFlow = 16,
    PrismaticFlow = 17,
    ForegroundTileIdentity = 18,
}

public static class TerrainDebugViewState
{
    private static readonly int _terrainDebugViewID = Shader.PropertyToID("_TerrainDebugView");
    private static readonly int _terrainDebugBackgroundTileIdentityID =
        Shader.PropertyToID("_TerrainDebugBackgroundTileIdentity");

    // Глобаль шейдера живёт в нативной части и переживает доменную
    // перезагрузку, а статическое поле — нет. Без публикации на старте они
    // расходятся: C# считает, что вид выключен, а кадр рисуется прошлым
    // выбранным видом, которого может уже и не быть в перечислении.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForRuntime()
    {
        Active = TerrainDebugView.Off;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void PublishOnLoad() => Publish();

    public static TerrainDebugView Active { get; private set; } = TerrainDebugView.Off;

    public static string Describe(TerrainDebugView view) => view switch
    {
        TerrainDebugView.Off => "Обычный вид",
        TerrainDebugView.ReliefRim => "Кайма рельефа",
        TerrainDebugView.ForeignSides => "Чужие стороны",
        TerrainDebugView.Coverage => "Силуэт клетки",
        TerrainDebugView.Layer => "Слой",
        TerrainDebugView.Anchored => "Смещённые клетки",
        TerrainDebugView.CellLocal => "Координата в клетке",
        TerrainDebugView.ReliefGroup => "Рельефная группа",
        TerrainDebugView.ContinuousSheet => "Сплошной лист",
        TerrainDebugView.AmbientOcclusion => "AO: вклад в террейн",
        TerrainDebugView.BackgroundTileIdentity => "Уникальные тайлы фона",
        TerrainDebugView.FacetedGlint => "Глинт фасеток",
        TerrainDebugView.SourceAlbedo => "Исходная текстура",
        TerrainDebugView.AnimatedColor => "Цвет после анимации",
        TerrainDebugView.PrismaticTint => "Призматический тинт",
        TerrainDebugView.DecalContribution => "Вклад декали",
        TerrainDebugView.ShimmerFlow => "Поток shimmer",
        TerrainDebugView.PrismaticFlow => "Поток призматик",
        TerrainDebugView.ForegroundTileIdentity => "Уникальные тайлы передних блоков",
        _ => view.ToString(),
    };

    public static string Legend(TerrainDebugView view) => view switch
    {
        TerrainDebugView.Off => "Термы террейна не подменяются.",
        TerrainDebugView.ReliefRim =>
            "Зелёное — фаска не затемняет пиксель, красное — максимальное " +
            "затемнение. Это геометрический множитель после анимации и декалей. " +
            "Фиолетовое — фаска выключена настройкой.",
        TerrainDebugView.ForeignSides =>
            "Красный — чужой сосед сверху, зелёный — снизу, синий — слева, " +
            "жёлтый — справа. Цветной передний план обрезан по силуэту; " +
            "серое — подложка или клетка без рельефной группы.",
        TerrainDebugView.Coverage =>
            "Бирюзовое — передний план внутри контура, малиновое — вырезанная " +
            "часть его несущего прямоугольника, серое — подложка.",
        TerrainDebugView.Layer =>
            "Зелёное — видимый контур переднего плана, синее — подложка. " +
            "Синее там, где ждёшь блок, значит блок не нарисован.",
        TerrainDebugView.Anchored =>
            "Жёлтое — у клетки смещён хотя бы один угол. Цвет ограничен " +
            "видимым контуром клетки.",
        TerrainDebugView.CellLocal =>
            "Красный — X внутри клетки, зелёный — Y. Цвет ограничен " +
            "видимым контуром клетки.",
        TerrainDebugView.ReliefGroup =>
            "Оттенок — код рельефа. Чёрно-синее — клетка без группы.",
        TerrainDebugView.ContinuousSheet =>
            "Бирюзовое — клетка адресует лист целиком по мировой координате; " +
            "тёмное — тайл на клетку.",
        TerrainDebugView.AmbientOcclusion =>
            "Показывает эффективное затемнение, которое AO применяет к террейну " +
            "с учётом флага получателя, силы и нижнего предела. Зелёное — " +
            "вклада нет, красное — максимальное затемнение.",
        TerrainDebugView.BackgroundTileIdentity =>
            "Показывает только подложку. Цвет без хеша кодирует слот атласа " +
            "и координату тайла 32×32: один atlas tile всегда получает один " +
            "цвет. Пурпурный — нет корректного адреса либо он за пределом " +
            "поддерживаемого размера.",
        TerrainDebugView.ForegroundTileIdentity =>
            "Показывает только передний слой. Цвет без хеша кодирует слот атласа " +
            "и фактически выбранный тайл 32×32 после автотайлинга; силуэт " +
            "учитывает геометрию клетки. Пурпурный — некорректный адрес.",
        TerrainDebugView.FacetedGlint =>
            "Фактическая интенсивность формулы глинта, нормализованная на его силу: " +
            "почти чёрное — вклада нет, оранжевое — активный глинт.",
        TerrainDebugView.SourceAlbedo => "Цвет реального текселя после выборки атласа.",
        TerrainDebugView.AnimatedColor =>
            "Результат выбранного типа цветовой анимации до наложения декали.",
        TerrainDebugView.PrismaticTint =>
            "Фактическое изменение цвета от Prismatic Crystal: нейтральный серый — " +
            "вклада нет; контраст усилен только в диагностическом отображении.",
        TerrainDebugView.DecalContribution =>
            "Разница до и после декали: нейтральный серый — нет вклада, " +
            "каналы показывают знак и силу изменения с усиленным диагностическим контрастом.",
        TerrainDebugView.ShimmerFlow =>
            "Статическая выборка _FlowMap для shimmer. Карта задаёт пространственное " +
            "поле; движение результата смотри в «Цвет после анимации». Чёрное — " +
            "профиль клетки не использует эту карту.",
        TerrainDebugView.PrismaticFlow =>
            "Статическая выборка _PrismaticFlowMap для призматических кристаллов. " +
            "Карта задаёт оттенок, а время меняет фазу эффекта. Для результата " +
            "смотри «Цвет после анимации». Чёрное — клетка не призматическая.",
        _ => string.Empty,
    };

    public static bool IsSurfacePipelineView(TerrainDebugView view) => view switch
    {
        TerrainDebugView.BackgroundTileIdentity or
        TerrainDebugView.ForegroundTileIdentity or
        TerrainDebugView.AmbientOcclusion or
        TerrainDebugView.FacetedGlint or
        TerrainDebugView.SourceAlbedo or
        TerrainDebugView.AnimatedColor or
        TerrainDebugView.PrismaticTint or
        TerrainDebugView.DecalContribution or
        TerrainDebugView.ShimmerFlow or
        TerrainDebugView.PrismaticFlow => true,
        _ => false,
    };

    public static void Set(TerrainDebugView view)
    {
        Active = view;
        Publish();
    }

    // Глобаль переживает смену сцены и доменную перезагрузку, поэтому её
    // публикуют заново, а не полагаются на прошлое значение.
    public static void Publish()
    {
        Shader.SetGlobalInteger(_terrainDebugViewID, (int)Active);
        Shader.SetGlobalInteger(
            _terrainDebugBackgroundTileIdentityID,
            Active == TerrainDebugView.BackgroundTileIdentity ? 1 : 0);
    }

    public static void Reset() => Set(TerrainDebugView.Off);
}
