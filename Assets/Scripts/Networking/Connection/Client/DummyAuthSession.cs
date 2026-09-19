#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyAuthSession
{
    private readonly DummyTokenStore _tokenStore;
    private readonly IDummyClock _clock;
    private readonly HashSet<string> _validTokens;

    internal DummyAuthSession(DummyTokenStore tokenStore, IDummyClock clock)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _validTokens = _tokenStore.Load();
    }

    public string PlayerName
    {
        get
        {
            long userID = StableUserID(SystemInfo.deviceUniqueIdentifier);
            return $"ШАХТЁР-{100 + (int)(userID % 900)}";
        }
    }

    public string ResolveToken(string? receivedToken)
    {
        if (!string.IsNullOrEmpty(receivedToken) && _validTokens.Contains(receivedToken))
        {
            return receivedToken;
        }

        var bytes = new byte[16];
        _clock.Random.NextBytes(bytes);
        string newToken = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        _validTokens.Add(newToken);
        _tokenStore.Save(_validTokens);
        return newToken;
    }

    internal static long StableUserID(string? deviceIdentifier)
    {
        string seed = deviceIdentifier ?? string.Empty;
        uint hash = 2166136261u;
        foreach (char character in seed)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return 10_000_000_000L + (hash % 2_000_000_000L);
    }
}
