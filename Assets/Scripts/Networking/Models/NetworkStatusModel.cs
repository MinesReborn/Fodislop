#nullable enable

namespace Kern.Networking;

public sealed class NetworkStatusModel
{
    public int PingMs { get; private set; }

    public int OnlinePlayers { get; private set; }

    public int OnlineProgrammator { get; private set; }

    public void SetPing(int pingMs)
    {
        PingMs = pingMs;
    }

    public void SetOnline(int players, int programmator)
    {
        OnlinePlayers = players;
        OnlineProgrammator = programmator;
    }
}
