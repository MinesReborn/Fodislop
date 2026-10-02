#nullable enable

using System;
using System.Security.Cryptography;

namespace Kern;

public static class ETagCalculator
{
    public static string? Calculate(byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        return Calculate((ReadOnlySpan<byte>)data);
    }

    public static string? Calculate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        Span<byte> hashBytes = stackalloc byte[16];
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        incrementalHash.AppendData(data);
        if (!incrementalHash.TryGetHashAndReset(hashBytes, out int written) || written != 16)
        {
            return null;
        }

        const string hex = "0123456789abcdef";
        Span<char> hexChars = stackalloc char[32];
        for (int i = 0; i < 16; i++)
        {
            byte b = hashBytes[i];
            hexChars[i * 2] = hex[b >> 4];
            hexChars[i * 2 + 1] = hex[b & 0x0F];
        }

        return new string(hexChars);
    }
}
