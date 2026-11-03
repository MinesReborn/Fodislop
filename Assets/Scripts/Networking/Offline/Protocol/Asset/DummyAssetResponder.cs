#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Core.Interfaces;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;

namespace Kern.Networking.Offline;

internal static class DummyAssetResponder
{
    public static async UniTask HandleRequestAsync(
        RuntimeAssetRequestPacket packet,
        ITextureStorageService textureStorage,
        IRuntimeAssetPaths assetPaths,
        Action<ServerPacket> sendPacket)
    {
        foreach (var asset in packet.Assets)
        {
            byte[]? data = AssetBatchDispatcher.IsAudioBank(asset.Filename)
                ? await assetPaths.GetAudioBankBytesAsync(asset.Filename)
                : await textureStorage.GetTextureData(asset.Filename);
            RuntimeAssetPacket response = data != null
                ? new RuntimeAssetPacket(
                    asset.Filename,
                    ETagCalculator.Calculate(data) ?? string.Empty,
                    data)
                : new RuntimeAssetPacket(asset.Filename, string.Empty, Array.Empty<byte>());
            sendPacket(new ServerPacket(response));
        }
    }
}
