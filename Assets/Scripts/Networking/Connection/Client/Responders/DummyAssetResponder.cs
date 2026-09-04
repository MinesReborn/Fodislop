#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyAssetResponder
{
    public static async UniTask HandleRequestAsync(
        RuntimeAssetRequestPacket packet,
        ITextureStorageService textureStorage,
        Action<ServerPacket> sendPacket)
    {
        foreach (var asset in packet.Assets)
        {
            byte[]? data = await textureStorage.GetTextureData(
                asset.Filename.TrimStart('/'));
            RuntimeAssetPacket response = data != null
                ? new RuntimeAssetPacket(asset.Filename, Guid.NewGuid().ToString(), data)
                : new RuntimeAssetPacket(asset.Filename, string.Empty, Array.Empty<byte>());
            sendPacket(new ServerPacket(response));
        }
    }
}
