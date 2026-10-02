#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Разрез изменённого прямоугольника на полоски постоянной высоты.
/// </summary>
///
/// Выгрузка на GPU идёт через промежуточную текстуру постоянного размера, и
/// это единственное место, где форма прямоугольника вообще на что-то влияет.
/// Вынесено отдельно ровно поэтому: прямоугольники приходят с сервера, форма
/// у них произвольная, и «ничего не потеряно и ничего не послано дважды»
/// должно проверяться тестом, а не разглядыванием цикла среди работы с
/// текстурами.
public static class TerrainUploadStrips
{
    /// <summary>Сколько полосок потребует прямоугольник.</summary>
    public static int Count(int rectHeight, int stagingRows)
    {
        if (rectHeight <= 0 || stagingRows <= 0)
        {
            return 0;
        }

        return ((rectHeight - 1) / stagingRows) + 1;
    }

    /// <summary>
    /// Полоска с номером <paramref name="index"/>. Ширина не режется никогда:
    /// промежуточная текстура шириной во всю целевую, а прямоугольник шире
    /// целевой текстуры быть не может.
    /// </summary>
    public static RectInt At(RectInt rect, int stagingRows, int index)
    {
        int row = index * stagingRows;
        int height = Mathf.Min(stagingRows, rect.height - row);
        return new RectInt(rect.x, rect.y + row, rect.width, height);
    }
}
