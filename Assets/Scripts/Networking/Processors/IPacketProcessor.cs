#nullable enable

using MinesServer.Networking.Server.Packets;

namespace Kern.Networking.Processors;
/// <typeparam name="T">Type of ServerPacket payload to process.</typeparam>
public interface IPacketProcessor<in T>
{
    void Process(T packet);
}
