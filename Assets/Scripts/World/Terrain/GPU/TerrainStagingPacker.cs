#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Кусок изменённой области и его место в промежуточной текстуре.</summary>
public readonly struct TerrainStagedPiece
{
    public TerrainStagedPiece(RectInt target, int stageX, int stageY, int batch)
    {
        Target = target;
        StageX = stageX;
        StageY = stageY;
        Batch = batch;
    }

    /// <summary>Куда кусок ложится в целевой текстуре.</summary>
    public RectInt Target { get; }

    public int StageX { get; }

    public int StageY { get; }

    /// <summary>Номер промежуточной текстуры в кадре.</summary>
    public int Batch { get; }
}

/// <summary>
/// Раскладка изменённых прямоугольников по промежуточным текстурам.
/// </summary>
///
/// Промежуточную текстуру нельзя набивать дважды за кадр: чтение её
/// пиксельного буфера после Apply и CopyTexture того же кадра ждёт render
/// thread. Раньше каждая полоска каждого канала набивала одну и ту же
/// текстуру заново, и тринадцать крошечных прямоугольников стоили
/// 117 синхронизаций — 146 мс на 68 текселей. Поэтому все куски кадра
/// раскладываются полками в одну текстуру, а если места не хватает —
/// в следующую. Одна текстура — одна набивка и одна загрузка на канал.
///
/// Кусок выше промежуточной текстуры режется по высоте (TerrainUploadStrips);
/// шире он быть не может: промежуточная текстура шириной во всю целевую.
public static class TerrainStagingPacker
{
    /// <returns>Сколько промежуточных текстур понадобилось.</returns>
    public static int Pack(
        List<RectInt> rects,
        int stagingWidth,
        int stagingHeight,
        List<TerrainStagedPiece> pieces)
    {
        pieces.Clear();
        if (rects.Count == 0 || stagingWidth <= 0 || stagingHeight <= 0)
        {
            return 0;
        }

        int batch = 0;
        int cursorX = 0;
        int shelfY = 0;
        int shelfHeight = 0;
        for (int index = 0; index < rects.Count; index++)
        {
            RectInt rect = rects[index];
            int strips = TerrainUploadStrips.Count(rect.height, stagingHeight);
            for (int strip = 0; strip < strips; strip++)
            {
                RectInt piece = TerrainUploadStrips.At(rect, stagingHeight, strip);
                if (cursorX + piece.width > stagingWidth)
                {
                    shelfY += shelfHeight;
                    cursorX = 0;
                    shelfHeight = 0;
                }

                if (shelfY + piece.height > stagingHeight)
                {
                    batch++;
                    cursorX = 0;
                    shelfY = 0;
                    shelfHeight = 0;
                }

                pieces.Add(new TerrainStagedPiece(piece, cursorX, shelfY, batch));
                cursorX += piece.width;
                shelfHeight = Mathf.Max(shelfHeight, piece.height);
            }
        }

        return batch + 1;
    }
}
