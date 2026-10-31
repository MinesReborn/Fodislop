#nullable enable

using MinesServer.Networking.Server.Packets.Connection;
using Kern.Core.Interfaces;


namespace Kern.Networking.Offline;

// Только для симуляции офлайн-мира: сплошная клетка допускает выемку.
// Сетевые действия отправляются серверу без этой локальной проверки.
public static class CellInteractionRules
{
    public static bool CanMine(in CellConfigurationPacket config) =>
        !config.Properties.HasFlag(CellConfigProperties.Passable);
}
