#nullable enable

namespace Kern.Core;

public static class WorldRenderLayers
{
    public const string WorldUI = "UI";
    public const string WorldUISortingLayer = "World UI";
    public const int TerrainSortingOrder = -1000;

    // Строго ниже террейна, а не вровень с ним. Оба рендерера рисуются с
    // альфа-блендингом в слое Default, и пока номер совпадал, порядок между
    // ними задавала не эта константа, а очередь материала и расстояние до
    // камеры. То есть он мог меняться от кадра к кадру и от положения
    // камеры: подложка мира то ложилась под террейн, то накрывала его
    // собственный фоновый слой.
    public const int WorldBackgroundSortingOrder = TerrainSortingOrder - 100;

    // Сущности мира (роботы, хвосты, эффекты в общем батче) — над фоновыми
    // клетками террейна, но под его блоками: стенки и передний план блоков
    // вынесены в отдельный рендерер TerrainForegroundSortingOrder.
    public const int WorldEntitySortingOrder = TerrainSortingOrder + 998;

    // Блоки террейна (боковые стенки и верхняя грань) — над роботами и
    // хвостами, чтобы они уходили под породу, как в легаси-клиенте.
    public const int TerrainForegroundSortingOrder = WorldEntitySortingOrder + 1;
}
