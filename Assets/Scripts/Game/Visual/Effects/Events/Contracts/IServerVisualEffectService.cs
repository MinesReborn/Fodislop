#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;

public interface IServerVisualEffectService
{
    void PlayEffect(VisualEffect effectType, ushort x, ushort y, ushort targetBotId = 0, int param = 0);
    void PlayEffect(VisualPacket packet);
    void PlayBoundaryDig(int serverX, int serverY, Direction direction);
}
