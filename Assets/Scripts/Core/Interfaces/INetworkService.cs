using System;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface INetworkService
    {
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
        void SendAction(IActionClientPacket action);
        void Send(IRootClientPacket packet);
    }
}
