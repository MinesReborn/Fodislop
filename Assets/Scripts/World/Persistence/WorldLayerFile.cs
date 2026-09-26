#nullable enable

using System;
using System.IO;

namespace Kern.Persistence;

/// <summary>
/// Файл слоя мира: заголовок, таблица смещений и RLE-чанки.
/// </summary>
///
/// У потока, читателя и таблицы смещений здесь ровно один хозяин. Замок взят
/// из <see cref="WorldLayerLifetime"/> — критическая секция общая со слоем,
/// иначе слой и загрузчик чанков писали бы в поток одновременно.
internal sealed class WorldLayerFile<T>
    where T : unmanaged
{
    private readonly string _filePath;
    private readonly Func<string, Stream> _openFile;
    private readonly WorldLayerLifetime _lifetime;
    private readonly int _widthChunks;
    private readonly int _heightChunks;
    private readonly int _chunkSize;
    private readonly long[] _chunkOffsets;

    private Stream? _fileStream;

    // One reader for the layer's lifetime, used under the IO lock. A reader per
    // chunk load allocated its buffers and UTF8 decoder on every load.
    private BinaryReader? _reader;

    public WorldLayerFile(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize,
        Func<string, Stream> openFile,
        WorldLayerLifetime lifetime)
    {
        _filePath = filePath;
        _widthChunks = widthChunks;
        _heightChunks = heightChunks;
        _chunkSize = chunkSize;
        _openFile = openFile;
        _lifetime = lifetime;

        // The Look-Up Table (FAT). Stores file offset for each chunk.
        _chunkOffsets = new long[widthChunks * heightChunks];
        Array.Fill(_chunkOffsets, -1);
    }

    public long[] ChunkOffsets => _chunkOffsets;

    public int ChunkCount => _chunkOffsets.Length;

    public long OffsetAt(int index) => _chunkOffsets[index];

    public void Initialize()
    {
        _fileStream = _openFile(_filePath);

        bool valid = WorldLayerFileHeader.TryReadHeader(
            _fileStream,
            _widthChunks,
            _heightChunks,
            _chunkSize,
            _chunkOffsets);

        if (!valid)
        {
            if (_fileStream.Length > 0 &&
                WorldLayerFileHeader.TryReadFormatVersion(_fileStream) is int version &&
                version != WorldLayerFileHeader.CurrentFormatVersion)
            {
                // Легаси формат не поддерживается и не мигрируется. Сохраняем
                // исходный файл рядом с новым вместо безвозвратного удаления:
                // восстановление с сервера может быть недоступно.
                _fileStream.Dispose();
                _fileStream = null;
                string legacyPath = $"{_filePath}.legacy.{DateTime.UtcNow.Ticks}";
                File.Move(_filePath, legacyPath);
                _fileStream = _openFile(_filePath);
            }
            else if (_fileStream.Length > 0)
            {
                // Fail-fast: a damaged map file must never be silently
                // recreated as an empty world. Surface the failure instead.
                _fileStream.Dispose();
                _fileStream = null;
                throw new IOException($"Map file '{_filePath}' is corrupt or its header does not match the expected world dimensions. Refusing to recreate it.");
            }

            WorldLayerFileHeader.WriteHeader(
                _fileStream,
                _widthChunks,
                _heightChunks,
                _chunkSize,
                _chunkOffsets);
        }
    }

    public T[]? TryLoad(int index, int chunkArea)
    {
        if (index < 0 || index >= _chunkOffsets.Length)
        {
            return null;
        }

        lock (_lifetime.IoLock)
        {
            if (_lifetime.Disposed)
            {
                return null;
            }

            long offset = _chunkOffsets[index];
            if (offset < 0 || _fileStream == null)
            {
                return null;
            }

            _fileStream.Seek(offset, SeekOrigin.Begin);
            _reader ??= new BinaryReader(_fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
            return WorldChunkRleCodec.DecodeChunk<T>(_reader, chunkArea);
        }
    }

    public bool VisitChunkRuns(int index, int chunkArea, Action<int, T, int> visitor)
    {
        if (index < 0 || index >= _chunkOffsets.Length)
        {
            return false;
        }

        if (visitor == null)
        {
            throw new ArgumentNullException(nameof(visitor));
        }

        lock (_lifetime.IoLock)
        {
            if (_lifetime.Disposed || _fileStream == null)
            {
                return false;
            }

            long offset = _chunkOffsets[index];
            if (offset < 0)
            {
                return false;
            }

            _fileStream.Seek(offset, SeekOrigin.Begin);
            _reader ??= new BinaryReader(_fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
            WorldChunkRleCodec.VisitChunkRuns(_reader, chunkArea, index, visitor);
            return true;
        }
    }

    public int CopyStoredChunkIndices(int startIndex, int[] destination, out int nextIndex)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (startIndex < 0 || startIndex > _chunkOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        lock (_lifetime.IoLock)
        {
            if (_lifetime.Disposed)
            {
                nextIndex = _chunkOffsets.Length;
                return 0;
            }

            int count = 0;
            int index = startIndex;
            while (index < _chunkOffsets.Length && index - startIndex < destination.Length)
            {
                if (_chunkOffsets[index] >= 0)
                {
                    destination[count++] = index;
                }

                index++;
            }

            nextIndex = index;
            return count;
        }
    }

    public void Save(int index, T[] chunk, int chunkArea)
    {
        if (_fileStream == null)
        {
            throw new ObjectDisposedException(
                nameof(WorldLayer<T>),
                $"World layer '{_filePath}' has no open file stream.");
        }

        lock (_lifetime.IoLock)
        {
            if (_lifetime.Disposed)
            {
                throw new ObjectDisposedException(nameof(WorldLayer<T>));
            }

            _fileStream.Seek(0, SeekOrigin.End);
            long newOffset = _fileStream.Position;

            using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
            WorldChunkRleCodec.EncodeChunk(writer, chunk, chunkArea);

            _chunkOffsets[index] = newOffset;
            WorldLayerFileHeader.WriteChunkOffset(_fileStream, index, newOffset);
        }
    }

    /// <summary>Сбросить буферы файла. false — потока уже нет.</summary>
    public bool Flush(bool toDisk)
    {
        lock (_lifetime.IoLock)
        {
            if (_fileStream == null)
            {
                return false;
            }

            if (toDisk && _fileStream is FileStream file)
            {
                file.Flush(true);
            }
            else
            {
                _fileStream.Flush();
            }

            return true;
        }
    }

    public void CreateDurableBackup(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("Backup path is required.", nameof(backupPath));
        }

        lock (_lifetime.IoLock)
        {
            if (_fileStream == null || _lifetime.Disposed)
            {
                throw new ObjectDisposedException(nameof(WorldLayer<T>));
            }

            string? directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = backupPath + ".tmp";
            long originalPosition = _fileStream.Position;
            try
            {
                _fileStream.Flush();
                _fileStream.Seek(0, SeekOrigin.Begin);
                using (var backup = new FileStream(
                           temporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    _fileStream.CopyTo(backup);
                    backup.Flush(flushToDisk: true);
                }

                if (File.Exists(backupPath))
                {
                    File.Replace(temporaryPath, backupPath, null);
                }
                else
                {
                    File.Move(temporaryPath, backupPath);
                }
            }
            finally
            {
                _fileStream.Seek(originalPosition, SeekOrigin.Begin);
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    /// <summary>
    /// Закрыть поток и читатель. Возвращает отказ закрытия, если он был: слой
    /// решает сам, бросать его или доложить после.
    /// </summary>
    public Exception? DisposeStreams()
    {
        Exception? failure = null;
        lock (_lifetime.IoLock)
        {
            _lifetime.MarkDisposed();
            try
            {
                _reader?.Dispose();
                _reader = null;
                _fileStream?.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                failure = ex;
            }
        }

        return failure;
    }
}
