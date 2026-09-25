#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

// Отладочные виды террейна, отдельно от световых.
//
// Световой вид показывает, сколько света пришло в пиксель. Он не отвечает на
// вопрос, почему пиксель тёмный: гасить может кайма рельефа, силуэт клетки,
// контактное затенение или просто не тот слой. Термы показываются отдельно;
// категориальные виды окрашивают слои и atlas tile identity.
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
        TerrainDebugView.AmbientOcclusion => "Контактное затенение",
        TerrainDebugView.BackgroundTileIdentity => "Уникальные тайлы фона",
        _ => view.ToString(),
    };

    public static string Legend(TerrainDebugView view) => view switch
    {
        TerrainDebugView.Off => "Термы террейна не подменяются.",
        TerrainDebugView.ReliefRim =>
            "Зелёное — кайма не трогает пиксель, красное — гасит. " +
            "Фиолетовое — кайма выключена настройкой, считать нечего.",
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
            "Зелёное — затенения нет, красное — полное. Если полоса в кадре " +
            "видна здесь красным, гасит затенение; если тут ровно зелено — " +
            "гасит что-то другое.",
        TerrainDebugView.BackgroundTileIdentity =>
            "Показывает только подложку. Цвет без хеша кодирует слот атласа " +
            "и координату тайла 32×32: один atlas tile всегда получает один " +
            "цвет. Пурпурный — нет корректного адреса либо он за пределом " +
            "поддерживаемого размера.",
        _ => string.Empty,
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
