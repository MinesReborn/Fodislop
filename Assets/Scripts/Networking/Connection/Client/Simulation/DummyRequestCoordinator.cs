#nullable enable

using System;
using System.Threading;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyRequestCoordinator
{
    public static void ReplaceRequest(
        ref CancellationTokenSource? activeRequest,
        CancellationTokenSource newRequest)
    {
        CancellationTokenSource? previous = Interlocked.Exchange(ref activeRequest, newRequest);
        if (previous != null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public static void CancelRequest(ref CancellationTokenSource? activeRequest)
    {
        CancellationTokenSource? current = Interlocked.Exchange(ref activeRequest, null);
        if (current != null)
        {
            try
            {
                current.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public static bool TryCompleteRequest(
        ref CancellationTokenSource? activeRequest,
        CancellationTokenSource expectedRequest)
    {
        return Interlocked.CompareExchange(ref activeRequest, null, expectedRequest) == expectedRequest;
    }
}
