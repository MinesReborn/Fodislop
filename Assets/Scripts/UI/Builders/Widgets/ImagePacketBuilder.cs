#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class ImagePacketBuilder : PacketUIBuilderBase<ImagePacket>
{
    protected override VisualElement BuildTyped(ImagePacket imagePacket, PacketUIBuilder builder)
    {
        VisualElement element = BuildUriImage(imagePacket.URI, builder);
        element.style.width = imagePacket.Width;
        element.style.height = imagePacket.Height;
        return element;
    }

    internal static VisualElement BuildUriImage(string uri, PacketUIBuilder builder)
    {
        var element = new VisualElement();

        var cts = new CancellationTokenSource();
        element.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            cts.Cancel();
            cts.Dispose();
        });

        builder.Operations.Run(
            $"load_packet_image_{uri}",
            supervisorToken => LoadImage(
                element,
                uri,
                builder.AssetLoader,
                builder.WebAssetLoader,
                cts.Token,
                supervisorToken));

        return element;
    }

    private static async UniTask LoadImage(
        VisualElement element,
        string uri,
        IAssetLoader loader,
        IWebAssetLoader webLoader,
        CancellationToken elementToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            elementToken,
            supervisorToken);
        CancellationToken token = linkedCancellation.Token;
        Texture2D? texture;
        try
        {
            texture = await GetTexture(uri, loader, webLoader, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PacketUI] Image '{uri}' failed to load: {exception}");
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (texture == null)
        {
            Debug.LogWarning(
                $"[ImagePacketBuilder] Optional image '{uri}' returned no texture; skipped.");
            return;
        }

        if (element != null)
        {
            element.style.backgroundImage = new StyleBackground(texture);
        }
    }

    private static async UniTask<Texture2D?> GetTexture(
        string uri,
        IAssetLoader loader,
        IWebAssetLoader webLoader,
        CancellationToken token)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? absolute) ||
            (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return await loader.GetTextureAsync(uri, token);
        }

        return await webLoader.GetTextureAsync(uri, token);
    }
}
