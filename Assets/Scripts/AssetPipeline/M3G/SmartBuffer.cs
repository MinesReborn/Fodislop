#nullable enable

using System;

namespace Kern;

/// <summary>
/// Fixed-capacity byte buffer with an explicit length, ported from the
/// MinesServer.M3G reference implementation. Out-of-data reads are rejected
/// the same way as the reference so decode behavior stays identical.
/// </summary>
internal sealed class SmartBuffer
{
    private readonly byte[] _buffer;
    private int _length;

    public SmartBuffer(int maxLength)
    {
        _buffer = new byte[maxLength];
        _length = 0;
    }

    public int Length => _length;

    public byte this[int index]
    {
        get
        {
            if (index > _length)
            {
                throw new IndexOutOfRangeException("SmartBuffer index out of range.");
            }

            return _buffer[index];
        }
    }

    public int CopyFromArray(byte[] copyFrom, int offset = 0)
    {
        if (copyFrom.Length - offset > _buffer.Length)
        {
            throw new InvalidOperationException("SmartBuffer cannot copy: source is longer than the buffer.");
        }

        Array.Copy(copyFrom, offset, _buffer, 0, copyFrom.Length - offset);
        _length = copyFrom.Length - offset;
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
}
