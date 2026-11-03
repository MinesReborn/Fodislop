#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processing;

public sealed class AudioPacketProcessor : IPacketProcessor<AudioPacket>
{
    private readonly IServerAudioService _audio;
    public AudioPacketProcessor(IServerAudioService audio)
    {
        _audio = audio;
    }

    public void Process(AudioPacket packet) =>
        _audio.Play(packet);
}
