#nullable enable

using System;

namespace Kern;

/// <summary>
/// Decoded M3G pixels as an RGBA32 buffer in Unity texture row order
/// (row 0 is the bottom row, matching the stream layout).
/// </summary>
public readonly record struct M3DecodedImage(byte[] Rgba, int Width, int Height);

/// <summary>
/// M3G decompressor ported from the MinesServer.M3G reference implementation
/// (MinesServer.M3G/M3Decompressor.cs). System.Drawing is not available in
/// Unity, so pixels are produced as an RGBA32 buffer instead of a Bitmap.
/// The decompression steps themselves are kept identical to the reference.
/// </summary>
public sealed class M3Decompressor
{
    private const int DefaultBufferSize = 2_500_000;
    private const int WidthOffset = 0;
    private const int HeightOffset = 2;
    private const int OperationsOffset = 4;
    private const int OperationCount = 10;
    private const int HeaderSize = 14;
    private const int MinSourceLength = 15;
    private const int MaximumDecodedBytes = 64 * 1024 * 1024;

    private SmartBuffer _inBuffer = null!;
    private SmartBuffer _outBuffer = null!;
    private readonly M3Operation[] _operations = new M3Operation[OperationCount];
    private readonly int[] _dictionary = new int[256];

    public M3DecodedImage Decompress(byte[] source)
    {
        if (source.Length < MinSourceLength)
        {
            throw new InvalidOperationException("M3G image has an incomplete header.");
        }

        int width = BitConverter.ToUInt16(source, WidthOffset);
        int height = BitConverter.ToUInt16(source, HeightOffset);
        long pixelBytes = (long)width * height * 4;
        if (width == 0 || height == 0 || pixelBytes > MaximumDecodedBytes)
        {
            throw new InvalidOperationException($"Invalid M3G dimensions {width}x{height}.");
        }

        int pixelBufferBytes = (int)pixelBytes;
        int requiredBufferSize = Math.Max(DefaultBufferSize, pixelBufferBytes);
        _inBuffer = new SmartBuffer(requiredBufferSize, usePool: true);
        _outBuffer = new SmartBuffer(requiredBufferSize, usePool: true);

        try
        {
            for (int index = 0; index < OperationCount; index++)
            {
                _operations[index] = (M3Operation)source[OperationsOffset + index];
            }

            _inBuffer.CopyFromArray(source, HeaderSize);

            foreach (M3Operation operation in _operations)
            {
                switch (operation)
                {
                    case M3Operation.Delta:
                        UnDelta();
                        SwapBuffers();
                        break;
                    case M3Operation.Ngramm:
                        UnNgramm();
                        SwapBuffers();
                        break;
                    case M3Operation.RLE:
                        UnRLE();
                        SwapBuffers();
                        break;
                    case M3Operation.PackInterleaved:
                    case M3Operation.PackPlanar:
                        return CreateImage(width, height, operation);
                    case M3Operation.None:
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown M3G operation {(byte)operation}.");
                }
            }

            throw new InvalidOperationException("M3G image has no pixel packing operation.");
        }
        finally
        {
            _inBuffer.Dispose();
            _outBuffer.Dispose();
        }
    }

    private void SwapBuffers()
    {
        (_inBuffer, _outBuffer) = (_outBuffer, _inBuffer);
    }

    private void UnDelta()
    {
        _outBuffer.Clear();
        byte lastByte = 0;
        for (int index = 0; index < _inBuffer.Length; index++)
        {
            lastByte = (byte)(lastByte + _inBuffer[index]);
            _outBuffer.Push(lastByte);
        }
    }

    private void UnRLE()
    {
        _outBuffer.Clear();
        if (_inBuffer.Length < 2)
        {
            throw new InvalidOperationException("M3G run dictionary is incomplete.");
        }

        byte runMarker = _inBuffer[0];
        byte longRunMarker = _inBuffer[1];

        int index = 2;
        while (index < _inBuffer.Length)
        {
            byte currentByte = _inBuffer[index++];
            if (currentByte == runMarker)
            {
                if (index + 1 >= _inBuffer.Length)
                {
                    throw new InvalidOperationException("M3G run data is truncated.");
                }

                int count = _inBuffer[index++];
                byte value = _inBuffer[index++];
                for (int repeat = 0; repeat < count; repeat++)
                {
                    _outBuffer.Push(value);
                }
            }
            else if (currentByte == longRunMarker)
            {
                if (index + 2 >= _inBuffer.Length)
                {
                    throw new InvalidOperationException("M3G long run data is truncated.");
                }

                int countLow = _inBuffer[index++];
                int countHigh = _inBuffer[index++];
                byte value = _inBuffer[index++];
                int totalCount = countLow + 256 * countHigh;
                for (int repeat = 0; repeat < totalCount; repeat++)
                {
                    _outBuffer.Push(value);
                }
            }
            else
            {
                _outBuffer.Push(currentByte);
            }
        }
    }

    private void UnNgramm()
    {
        const int NgrammDictionarySize = 1560;
        _outBuffer.Clear();
        Array.Clear(_dictionary, 0, _dictionary.Length);

        int sourceIndex = 0;
        byte separator = _inBuffer[sourceIndex++];

        byte currentByte = _inBuffer[sourceIndex++];
        while (sourceIndex < NgrammDictionarySize && currentByte != separator)
        {
            _dictionary[currentByte] = sourceIndex;
            while (sourceIndex < NgrammDictionarySize && currentByte != separator)
            {
                currentByte = _inBuffer[sourceIndex++];
            }

            if (sourceIndex < NgrammDictionarySize)
            {
                currentByte = _inBuffer[sourceIndex++];
            }
        }

        if (sourceIndex >= NgrammDictionarySize)
        {
            throw new InvalidOperationException(
                "M3G ngram dictionary header is corrupted or has an unexpected size.");
        }

        while (sourceIndex < _inBuffer.Length)
        {
            currentByte = _inBuffer[sourceIndex++];
            int dictionaryStart = _dictionary[currentByte];
            if (dictionaryStart > 0)
            {
                byte valueFromDictionary = _inBuffer[dictionaryStart++];
                while (valueFromDictionary != separator)
                {
                    _outBuffer.Push(valueFromDictionary);
                    valueFromDictionary = _inBuffer[dictionaryStart++];
                }
            }
            else if (currentByte == separator)
            {
                byte literalByte = _inBuffer[sourceIndex++];
                _outBuffer.Push(literalByte);
            }
            else
            {
                _outBuffer.Push(currentByte);
            }
        }
    }

    private M3DecodedImage CreateImage(int width, int height, M3Operation format)
    {
        int pixels = checked(width * height);
        int pixelBytes = checked(pixels * 4);
        if (_inBuffer.Length < pixelBytes)
        {
            throw new InvalidOperationException("M3G image data is truncated.");
        }

        byte[] rgba = new byte[pixelBytes];
        if (format == M3Operation.PackInterleaved)
        {
            for (int row = 0; row < height; row++)
            {
                int sourceRowStart = row * width * 4;
                int destinationRowStart = row * width * 4;
                for (int offset = 0; offset < width * 4; offset++)
                {
                    rgba[destinationRowStart + offset] = _inBuffer[sourceRowStart + offset];
                }
            }
        }
        else
        {
            int rPlaneOffset = 0;
            int gPlaneOffset = pixels;
            int bPlaneOffset = 2 * pixels;
            int aPlaneOffset = 3 * pixels;

            for (int row = 0; row < height; row++)
            {
                int destinationRowStart = row * width * 4;
                for (int column = 0; column < width; column++)
                {
                    int planeIndex = row * width + column;
                    int destination = destinationRowStart + column * 4;
                    rgba[destination] = _inBuffer[rPlaneOffset + planeIndex];
                    rgba[destination + 1] = _inBuffer[gPlaneOffset + planeIndex];
                    rgba[destination + 2] = _inBuffer[bPlaneOffset + planeIndex];
                    rgba[destination + 3] = _inBuffer[aPlaneOffset + planeIndex];
                }
            }
        }

        return new M3DecodedImage(rgba, width, height);
    }
}
