#nullable enable

using System;
using System.Buffers;

namespace Kern;

/// <summary>
/// Fixed-capacity byte buffer with an explicit length, ported from the
/// MinesServer.M3G reference implementation. Out-of-data reads are rejected
/// the same way as the reference so decode behavior stays identical.
/// </summary>
internal sealed class SmartBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly bool _fromPool;
    private int _length;
    private bool _disposed;

    public SmartBuffer(int maxLength, bool usePool = false)
    {
        _fromPool = usePool;
        _buffer = usePool ? ArrayPool<byte>.Shared.Rent(maxLength) : new byte[maxLength];
        _length = 0;
    }

    public int Length => _length;

    public byte this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_length)
            {
                throw new IndexOutOfRangeException("SmartBuffer index out of range.");
            }

            return _buffer[index];
        }
    }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    public int CopyFromArray(byte[] copyFrom, int offset = 0)
    {
        int count = copyFrom.Length - offset;
        if (count > _buffer.Length)
        {
            throw new InvalidOperationException("SmartBuffer cannot copy: source is longer than the buffer.");
        }

        Array.Copy(copyFrom, offset, _buffer, 0, count);
        _length = count;
        return _length;
    }

    public void Clear()
    {
        _length = 0;
    }

    public void Push(byte value)
    {
        if (_length >= _buffer.Length)
        {
            throw new InvalidOperationException("SmartBuffer cannot push: buffer is full.");
        }

        _buffer[_length] = value;
        _length++;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_fromPool)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }
    }
}
