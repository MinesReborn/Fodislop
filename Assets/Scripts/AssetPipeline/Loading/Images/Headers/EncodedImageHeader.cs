#nullable enable

using System;
using System.Buffers.Binary;

namespace Kern.AssetPipeline.Loading;

/// <summary>
/// Размер закодированной картинки по её заголовку, без распаковки.
/// </summary>
///
/// LoadImage распаковывает сразу во весь размер из заголовка, а заголовок
/// пишет отправитель. Поэтому размер читается и проверяется до вызова.
/// Формат один — PNG, как в AssetBatchDispatcher.IsTextureFile.
public static class EncodedImageHeader
{
    private static readonly byte[] s_pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        return data.StartsWith(s_pngSignature) && TryReadPng(data, out width, out height);
    }

    // Первый чанк PNG обязан быть IHDR: ширина и высота — big-endian сразу за типом.
    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 ||
            data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
        {
            return false;
        }

        uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        if (pngWidth > int.MaxValue || pngHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)pngWidth;
        height = (int)pngHeight;
        return true;
    }
}
