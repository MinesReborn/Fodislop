using Fodinae.Scripts.Game.Managers;
using MinesServer.Networking.Server.Packets.World;

namespace Fodinae.Scripts.Networking.Processors
{
    public class AudioPacketProcessor : IPacketProcessor<AudioPacket>
    {
        public void Process(AudioPacket packet)
        {
            if (ServerAudioEventManager.Instance != null)
            {
                ServerAudioEventManager.Instance.PlayEffect(packet);
            }
        }
    }
}
