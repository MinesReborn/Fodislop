#nullable enable

using System.Threading;

namespace Kern.Networking.Dummy;

internal sealed class DummyTransportByteCounter
{
    private long _receivedBytes;

    public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);

    public void Add(int byteCount) => Interlocked.Add(ref _receivedBytes, byteCount);
}
