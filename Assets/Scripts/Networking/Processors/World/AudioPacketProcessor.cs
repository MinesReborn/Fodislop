#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class AudioPacketProcessor(IServerAudioService audio) : IPacketProcessor<AudioPacket>
{
    public void Process(AudioPacket packet) => audio.PlayEffect(packet);
}
