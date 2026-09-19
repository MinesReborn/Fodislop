#nullable enable

using MinesServer.Networking.Server.Packets.Connection;

namespace MinesServer.Networking.Connection.Client;

internal readonly record struct DummyWorldDescriptor(
    int Width,
    int Height,
    CellConfigurationPacket[] CellConfigurations);
