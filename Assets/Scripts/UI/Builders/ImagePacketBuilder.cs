using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual; // Corrected using directive for ImagePacket
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class ImagePacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not ImagePacket imagePacket)
            {
                return null;
            }

            var element = new VisualElement();
            element.style.width = imagePacket.Width;
            element.style.height = imagePacket.Height;

            var cts = new CancellationTokenSource();
            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                cts.Cancel();
                cts.Dispose();
            });

            LoadImage(element, imagePacket.URI, cts.Token);

            return element;
        }

        private static void LoadImage(VisualElement element, string uri, CancellationToken token)
        {
            Fodinae.Scripts.ClientAssetLoader.Instance.LoadAndApplyTexture(
                (texture) =>
            {
                if (element != null)
                {
                    element.style.backgroundImage = new StyleBackground(texture); // Set StyleBackground directly
                }
            }, uri, token).Forget();
        }
    }
}
