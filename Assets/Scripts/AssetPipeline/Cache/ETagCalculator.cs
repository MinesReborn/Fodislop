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

        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(data);

        return string.Create(32, hash, static (span, hashBytes) =>
        {
            const string hex = "0123456789abcdef";
            for (int i = 0; i < 16; i++)
            {
                byte b = hashBytes[i];
                span[i * 2] = hex[b >> 4];
                span[i * 2 + 1] = hex[b & 0x0F];
            }
        });
    }

    public static string? Calculate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        return Calculate(data.ToArray());
    }
}
