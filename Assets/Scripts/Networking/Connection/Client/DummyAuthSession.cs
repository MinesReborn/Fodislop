#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyAuthSession
{
    private readonly DummyTokenStore _tokenStore;
    private readonly HashSet<string> _validTokens;

    public DummyAuthSession()
        : this(new DummyTokenStore())
    {
    }

    internal DummyAuthSession(DummyTokenStore tokenStore)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _validTokens = _tokenStore.Load();
    }

    public string PlayerName
    {
        get
        {
            long userId = StableUserId(SystemInfo.deviceUniqueIdentifier);
            return $"ШАХТЁР-{100 + (int)(userId % 900)}";
        }
    }

    public string ResolveToken(string? receivedToken)
    {
        if (!string.IsNullOrEmpty(receivedToken) && _validTokens.Contains(receivedToken))
        {
            return receivedToken;
        }

        string newToken = Guid.NewGuid().ToString("N");
        _validTokens.Add(newToken);
        _tokenStore.Save(_validTokens);
        return newToken;
    }

    internal static long StableUserId(string? deviceIdentifier)
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
