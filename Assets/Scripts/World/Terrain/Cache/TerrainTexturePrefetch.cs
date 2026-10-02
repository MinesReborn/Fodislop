#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Заказ текстур типов, которые приехали с чанком, до того как клетки этих
/// типов войдут в окно сборки.
/// </summary>
///
/// Окно само заказывает текстуру, когда разрешает тип вошедшей клетки. Но
/// тогда загрузка начинается в тот же момент, когда клетка уже нужна, и
/// первые кадры она рисуется без текстуры, а приезд текстуры потом гонит
/// отдельный шаг перечитывания. Чанки заказываются с запасом вокруг окна,
/// поэтому их приезд — самый ранний момент, когда известны будущие типы.
public sealed class TerrainTexturePrefetch
{
    private readonly bool[] _seen = new bool[256];

    public void PrefetchRegion(
        IWorldDataStorage storage,
        ITextureService textureService,
        int serverX,
        int serverY,
        int width,
        int height)
    {
        if (storage.CellLayer is not { } layer || width <= 0 || height <= 0)
        {
            return;
        }

        System.Array.Clear(_seen, 0, _seen.Length);
        int chunkSize = layer.ChunkSize;
        int firstX = System.Math.Max(0, serverX);
        int firstY = System.Math.Max(0, serverY);
        int lastX = serverX + width - 1;
        int lastY = serverY + height - 1;
        for (int chunkX = firstX / chunkSize; chunkX <= lastX / chunkSize; chunkX++)
        {
            for (int chunkY = firstY / chunkSize; chunkY <= lastY / chunkSize; chunkY++)
            {
                if (!layer.GetChunkIndexAndLocal(chunkX * chunkSize, chunkY * chunkSize, out int chunkIndex, out _))
                {
                    continue;
                }

                // Без касания LRU: предзаказ не должен продлевать жизнь чанку,
                // который окну пока не нужен.
                ChunkReadResult<CellType> chunk = layer.ReadChunk(chunkIndex, touchLRU: false);
                if (chunk.Status != ChunkReadStatus.Available || chunk.Data == null)
                {
                    continue;
                }

                CellType[] cells = chunk.Data;
                for (int index = 0; index < cells.Length; index++)
                {
                    _seen[(byte)cells[index]] = true;
                }
            }
        }

        for (int type = 0; type < _seen.Length; type++)
        {
            if (_seen[type] && (CellType)type != CellType.Unloaded)
            {
                textureService.RequestTexture((CellType)type);
            }
        }
    }
}
