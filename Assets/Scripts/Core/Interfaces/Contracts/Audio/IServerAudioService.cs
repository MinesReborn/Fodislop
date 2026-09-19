#nullable enable

using MinesServer.Networking.Server.Packets.World;

namespace Kern.Core.Interfaces;
public interface IServerAudioService
{
    void PlayEffect(AudioPacket packet);
    void ClearAllEffects();
}
