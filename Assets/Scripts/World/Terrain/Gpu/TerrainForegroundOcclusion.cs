#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>Determines whether opaque foreground geometry fully occludes a cell background.</summary>
    internal static class TerrainForegroundOcclusion
    {
        // Квад переднего плана даёт альфу 1 на каждом пикселе клетки, только если
        // у него есть текстура, она непрозрачна вся, цвет без прозрачности, нет
        // скругления контура и шейдер не выводит квад прозрачным.
        public static bool CoversCell(
            int x,
            int y,
            int foreground,
            TerrainVertex[] vertices,
            TerrainCellSources sources)
        {
            if (foreground < 0 || foreground >= sources.Atlases.Count)
            {
                return false;
            }

            ref TerrainVertex vertex = ref vertices[4];
            const int RoundableFlag = 1;
            bool hasTexture = vertex.UV1z != 0 && Mathf.HalfToFloat(vertex.UV1z) > 0.0001f;
            bool roundable = (Mathf.RoundToInt(vertex.UV6.z) & RoundableFlag) != 0;
            if (!hasTexture || roundable || vertex.Color.a < 255)
            {
                return false;
            }

            // A displaced foreground quad no longer covers the whole cell.  The
            // background must remain drawable behind the exposed edge; otherwise
            // the background is culled as a full rectangle and the quantized
            // silhouette reveals the cleared render target as a black seam.
            //
            // Внутри сплошного массива открытого края нет: все четыре узла клетки
            // общие с соседями, их смещённые квады ложатся встык и закрывают её
            // прямоугольник. С тех пор как узлы внутри массива смещаются, флаг
            // смещения стоит почти у всей породы, и без этой проверки фон
            // рисовался под каждой её клеткой — второй полный проход шейдера
            // террейна по большей части экрана.
            if (vertex.UV5x != 0 && !IsInsideSolidMass(x, y, sources))
            {
                return false;
            }

            CellType foregroundType = sources.CellCache.GetCellData(x + 1, y + 1).Type;
            return sources.Atlases[foreground].IsFullyOpaque(foregroundType);
        }

        // Все восемь соседей — сплошная непрозрачная порода, чьи узлы смещаются
        // вместе с узлами клетки. Незагруженный сосед не считается: фон тогда
        // остаётся, ошибка только в дорогую сторону, не в чёрную щель. Заплатка
        // пересобирает кольцо в клетку вокруг изменения, поэтому выкопанный
        // сосед возвращает фон этой клетке в том же шаге.
        private static bool IsInsideSolidMass(int x, int y, TerrainCellSources sources)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx != 0 || dy != 0) &&
                        !IsSolidMassCell(sources.CellCache.GetCellData(x + 1 + dx, y + 1 + dy), sources.Atlases))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSolidMassCell(CachedCellData cell, IReadOnlyList<IAtlasDescriptor> atlases)
        {
            if (cell.State != TerrainCellState.Loaded ||
                !TerrainVertexDistortionCalculator.IsCause(cell) ||
                MapCellConfigCatalog.GetVisualProperties(cell.Type).IsRoundableLoose)
            {
                return false;
            }

            for (int index = 0; index < atlases.Count; index++)
            {
                if (atlases[index].IsFullyOpaque(cell.Type))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
