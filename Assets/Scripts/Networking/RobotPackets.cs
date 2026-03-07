using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using System.Collections.Generic;

namespace Fodinae.Assets.Scripts.Networking
{
    public class RobotPositionPacket : IHBPacket
    {
        public ushort BotId { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public byte Rotation { get; set; } // 0: Right, 1: Up, 2: Left, 3: Down

        public RobotPositionPacket(ushort botId, ushort x, ushort y, byte rotation)
        {
            BotId = botId;
            X = x;
            Y = y;
            Rotation = rotation;
        }
    }

    public class RobotInfoPacket : IServerPacketPayload
    {
        public ushort BotId { get; set; }
        public int PlayerId { get; set; }
        public string Skin { get; set; }
        public string Tail { get; set; }
        public string Name { get; set; }

        public RobotInfoPacket(ushort botId, int playerId, string skin, string tail, string name)
        {
            BotId = botId;
            PlayerId = playerId;
            Skin = skin;
            Tail = tail;
            Name = name;
        }
    }
}
