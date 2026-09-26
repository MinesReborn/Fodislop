#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

public enum TerrainDecalFamily : byte
{
    None = 0,
    Stone = 1,
    Sand = 2,
    Road = 3,
    Ground = 4,
}

public static class TerrainDecalCatalog
{
    public const int VariantCount = 16;

    // Доля клеток, получающих декаль. Порог сравнивается с хэшем клетки,
    // поэтому величина обладает полезным свойством: её подъём только
    // добавляет декали, не трогая уже стоящие. Мир не перерисовывается
    // заново, к нему добавляется гуще.
    // Доля для камня, песка и дороги. Сегодня недостижима: единственный вызов
    // снаружи — GetGroundPlacement, а он жёстко передаёт CellType.Empty, то
    // есть семейство всегда Ground. Ветки Stone/Sand/Road вместе с их раскладкой
    // вариантов — заготовка под декали на непроходимых клетках, которой ещё
    // нет потребителя. Цифра оставлена прежней намеренно: менять её значило бы
    // делать вид, что это на что-то влияет.
    private const uint PlacementPercent = 30;

    public static int GetPackedPlacement(CellType cellType, int worldX, int serverY)
    {
        TerrainDecalFamily family = GetFamily(cellType);
        if (family == TerrainDecalFamily.None)
        {
            return 0;
        }

        uint hash = Hash(worldX, serverY, (uint)cellType);
        uint placementPercent = family == TerrainDecalFamily.Ground
            ? TerrainConfigHolder.GroundDecalPlacementPercent
            : PlacementPercent;
        if ((hash % 100u) >= placementPercent)
        {
            return 0;
        }

        int variant = family switch
        {
            TerrainDecalFamily.Stone => (int)(hash % 4u) * 2,
            TerrainDecalFamily.Sand => (int)(hash % 4u) * 2 + 1,
            TerrainDecalFamily.Road => (int)(hash % 8u),
            TerrainDecalFamily.Ground => (int)(hash % VariantCount),
            _ => 0,
        };
        int rotation = (int)((hash >> 8) & 3u);
        int mirror = (int)((hash >> 10) & 1u);
        int offsetX = (int)((hash >> 12) & 3u);
        int offsetY = (int)((hash >> 14) & 3u);
        return 1 + variant + (rotation << 4) + (mirror << 6) +
            (offsetX << 7) + (offsetY << 9);
    }

    public static int GetGroundPlacement(int worldX, int serverY) =>
        GetPackedPlacement(CellType.Empty, worldX, serverY);

    // Бит 12 (= 4096) в packedPlacement сигнализирует шейдеру использовать
    // _TerrainDecalStoneAtlas вместо основного _TerrainDecalAtlas.
    // Именно 12, а не 11: ground-раскладка упирается ровно в 2048
    // (1 + 15 + (3 << 4) + 64 + (3 << 7) + (3 << 9)), поэтому бит 11
    // выставлялся бы у самой старшей ground-декали — она уходила бы в чужой
    // атлас, а её же код в stone-ветке вырождался бы в 0 - 1.
    // Биты 0..10 содержат variant/rotation/mirror/offset — те же поля что
    // и у ground-декалей, поэтому TerrainTransformDecalUV работает без правок.
    private const int StoneAtlasBit = 1 << 12;

    public static int GetStonePlacement(int worldX, int serverY)
    {
        uint hash = Hash(worldX, serverY, cellType: 7u);
        if ((hash % 100u) >= PlacementPercent)
        {
            return 0;
        }

        int variant = (int)(hash % (uint)VariantCount);
        int rotation = (int)((hash >> 8) & 3u);
        int mirror = (int)((hash >> 10) & 1u);
        int offsetX = (int)((hash >> 12) & 3u);
        int offsetY = (int)((hash >> 14) & 3u);
        int packed = 1 + variant + (rotation << 4) + (mirror << 6) +
            (offsetX << 7) + (offsetY << 9);
        return packed | StoneAtlasBit;
    }

    // Атлас нарисован красно-чёрными тонами под красноскал и черноскал.
    // Остальной камень той же семьи — золото, металл, глубинная порода —
    // им не красим: гамма расходится с палитрой самой клетки.
    public static bool IsStoneDecalSurface(CellType cellType, bool isBackground) =>
        !isBackground && cellType is CellType.RedRock or CellType.BlackRock;

    public static bool IsGroundDecalSurface(CellType cellType, bool isBackground) =>
        cellType == CellType.Empty ||
        (isBackground && cellType != CellType.Unloaded);

    public static bool IsGroundSurface(CellType cellType) =>
        cellType == CellType.Empty;

    public static TerrainDecalFamily GetFamily(CellType cellType)
    {
        if (IsGroundSurface(cellType))
        {
            return TerrainDecalFamily.Ground;
        }

        if (cellType is
            CellType.BlackBoulder1 or
            CellType.BlackBoulder2 or
            CellType.BlackBoulder3 or
            CellType.MetalBoulder1 or
            CellType.MetalBoulder2 or
            CellType.MetalBoulder3 or
            CellType.DeepObsidianRock or
            CellType.DeepStripedRock or
            CellType.Boulder1 or
            CellType.Boulder2 or
            CellType.Boulder3 or
            CellType.Rock or
            CellType.HeavyRock or
            CellType.BlackRock or
            CellType.RedRock or
            CellType.GoldenRock or
            CellType.DeepRock or
            CellType.GRock)
        {
            return TerrainDecalFamily.Stone;
        }

        if (cellType is
            CellType.WhiteSand or
            CellType.DarkWhiteSand or
            CellType.RustySand or
            CellType.DarkRustySand or
            CellType.BlackSand or
            CellType.DarkBlackSand or
            CellType.BlueSand or
            CellType.DarkBlueSand or
            CellType.YellowSand or
            CellType.DarkYellowSand)
        {
            return TerrainDecalFamily.Sand;
        }

        return cellType == CellType.Road
            ? TerrainDecalFamily.Road
            : TerrainDecalFamily.None;
    }

    private static uint Hash(int worldX, int serverY, uint cellType)
    {
        uint hash = unchecked((uint)worldX) * 374761393u;
        hash += unchecked((uint)serverY) * 668265263u;
        hash ^= cellType * 2246822519u;
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return hash ^ (hash >> 16);
    }
}
