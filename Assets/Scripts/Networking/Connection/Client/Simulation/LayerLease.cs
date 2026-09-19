#nullable enable

using System.Threading;
using Kern.Persistence;
using MinesServer.Data;

namespace MinesServer.Networking.Connection.Client;

internal sealed class LayerLease(WorldLayer<CellType> layer)
{
    private int _refCount = 1;

    public WorldLayer<CellType> Layer => layer;

    public bool TryAddRef()
    {
        while (true)
        {
            int current = Volatile.Read(ref _refCount);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref _refCount) == 0)
        {
            layer.Dispose();
        }
    }
}
