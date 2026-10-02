#nullable enable

using System.Collections.Generic;

namespace Kern.Persistence;

/// <summary>
/// Сброс грязных чанков на диск.
/// </summary>
///
/// Снимок берётся на главном потоке, пока кэш не меняется параллельно.
/// Массивы не клонируются: они отсоединяются от dirty-набора, а при следующей
/// записи в такой чанк PrepareForWrite делает ровно одну копию. Поэтому запись
/// видит стабильное состояние, а изменения после снимка остаются отдельной
/// dirty-версией.
///
/// Потоки разделены строго. <see cref="WriteSnapshot"/> трогает только файл
/// (под замком ввода-вывода) и может идти в пуле. Учёт в кэше — завершение
/// или возврат отметок — только на главном потоке, после того как запись
/// дождались: кэш чанков не потокобезопасен, а главный поток меняет его каждый
/// кадр. Отметка снимается лишь с той версии чанка, что реально записана.
internal sealed class WorldLayerDirtyWriter<T>
    where T : unmanaged
{
    private readonly ChunkLRUCache<T> _cache;
    private readonly WorldLayerFile<T> _file;
    private readonly int _chunkArea;

    public WorldLayerDirtyWriter(
        ChunkLRUCache<T> cache,
        WorldLayerFile<T> file,
        int chunkArea)
    {
        _cache = cache;
        _file = file;
        _chunkArea = chunkArea;
    }

    public List<(int Index, T[] Chunk)> TakeSnapshot() => _cache.DetachDirtySnapshot();

    public void RestoreDirty(List<(int Index, T[] Chunk)> snapshot) =>
        _cache.RestoreDirtySnapshot(snapshot);

    public void CompleteSnapshot(List<(int Index, T[] Chunk)> snapshot) =>
        _cache.CompleteDirtySnapshot(snapshot);

    /// <summary>Синхронный сброс на главном потоке: запись и учёт подряд.</summary>
    public void Flush(bool flushToDisk)
    {
        List<(int Index, T[] Chunk)> snapshot = TakeSnapshot();
        try
        {
            WriteSnapshot(snapshot, flushToDisk);
        }
        catch
        {
            RestoreDirty(snapshot);
            throw;
        }

        CompleteSnapshot(snapshot);
    }

    /// <summary>Только файл. Бросает, если снимок не сохранён целиком.</summary>
    public void WriteSnapshot(List<(int Index, T[] Chunk)> snapshot, bool flushToDisk)
    {
        foreach ((int index, T[] chunk) in snapshot)
        {
            _file.Save(index, chunk, _chunkArea);
        }

        if (!_file.Flush(flushToDisk) && snapshot.Count > 0)
        {
            throw new System.ObjectDisposedException(
                nameof(WorldLayerDirtyWriter<T>),
                "World layer file closed before the dirty snapshot was flushed.");
        }
    }
}
