#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using Kern.World.Streaming;

namespace Kern.Networking.Offline;

internal readonly struct DummyStreamingGateLease : IDisposable
{
    private readonly SemaphoreSlim _gate;

    internal DummyStreamingGateLease(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        try
        {
            _gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Семафор мог освободиться одновременно с уничтожением мира.
        }
    }

}
