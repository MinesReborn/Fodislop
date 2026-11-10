#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;
using Kern.UI.Controls;

namespace Kern.UI.ServerWindows;
public class ImagePacketBuilder : PacketUIBuilderBase<ImagePacket>
{
    protected override VisualElement BuildTyped(ImagePacket imagePacket, PacketUIBuilder builder)
    {
        VisualElement element = BuildUriImage(imagePacket.URI, builder);
        element.style.width = imagePacket.Width;
        element.style.height = imagePacket.Height;

        // Кликабельность картинки определяет OnClickContext (слоты апгрейда —
        // иконка и есть кнопка). Картинка без действия не должна ловить клики:
        // замок и кольцо выбора лежат поверх иконки слота и иначе съедали бы
        // клик у самой ячейки.
        if (string.IsNullOrEmpty(imagePacket.OnClickContext))
        {
            element.pickingMode = PickingMode.Ignore;
        }

        return element;
    }

    internal static VisualElement BuildUriImage(string uri, PacketUIBuilder builder)
    {
        var element = new VisualElement();

        // Пустой URI — договорённость «картинки нет» (так уходит SliderPacket.Knob
        // без арта ручки): запрос по пустому имени сервер отвечает пустыми байтами,
        // и каждая отрисовка окна сыплет лог ошибками. Загрузку не планируем вовсе.
        if (string.IsNullOrEmpty(uri))
        {
            return element;
        }

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
            texture = await ServerImageSource.LoadAsync(loader, webLoader, uri, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"[PacketUI] Image '{uri}' failed to load.", exception);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (texture == null)
        {
            throw new InvalidOperationException($"[PacketUI] Image '{uri}' returned no texture.");
        }

        if (element != null)
        {
            element.style.backgroundImage = new StyleBackground(texture);
        }
    }

}
