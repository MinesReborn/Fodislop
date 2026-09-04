#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.UI.Builders;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    public class PacketUIBuilder
    {
        private readonly IAssetLoader _assetLoader;
        private readonly IAsyncOperationSupervisor _operations;
        private readonly PacketUIBuilderFactory _builderFactory = new();

        public PacketUIBuilder(
            IAssetLoader assetLoader,
            IAsyncOperationSupervisor operations)
        {
            _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
            _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        internal IAssetLoader AssetLoader => _assetLoader;
        internal IAsyncOperationSupervisor Operations => _operations;

        /// <summary>
        /// Собирает элемент по пакету. Возвращает элемент всегда: пакет
        /// неизвестного вида превращается в видимую заглушку, а не в null.
        /// </summary>
        public VisualElement Build(IGUIComponentPacket packet)
        {
            PacketUIBuilderBase? builder = _builderFactory.CreateBuilder(packet);
            VisualElement element;

            if (builder != null)
            {
                element = builder.Build(packet, this);
            }
            else
            {
                element = new Label($"[Unimplemented: {packet.GetType().Name}]");
                element.AddToClassList("packet-unimplemented");
            }

            StyleApplicator.ApplyStyles(element, packet);
            ApplyCanvasGeometry(element, packet);
            element.userData = packet;

            return element;
        }

        /// <summary>Собирает детей контейнера и складывает их в указанный узел.</summary>
        public void AddChildren(VisualElement parent, IContainerComponentPacket packet)
        {
            foreach (IGUIComponentPacket childPacket in packet.Children)
            {
                parent.Add(Build(childPacket));
            }
        }

        /// <summary>
        /// Координаты холста, если пакет их прислал. Любая из четырёх делает
        /// элемент абсолютным.
        /// </summary>
        private static void ApplyCanvasGeometry(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.AttachedProperties == null || packet.AttachedProperties.Length == 0)
            {
                return;
            }

            IStyle style = element.style;
            bool absolute = false;

            if (AttachedProperties.TryGetFloat(packet, "Canvas.X", out float left))
            {
                style.left = left;
                absolute = true;
            }

            if (AttachedProperties.TryGetFloat(packet, "Canvas.Y", out float top))
            {
                style.top = top;
                absolute = true;
            }

            if (AttachedProperties.TryGetFloat(packet, "Canvas.Width", out float width))
            {
                style.width = width;
                absolute = true;
            }

            if (AttachedProperties.TryGetFloat(packet, "Canvas.Height", out float height))
            {
                style.height = height;
                absolute = true;
            }

            if (absolute)
            {
                element.AddToClassList("abs");
            }
        }
    }
}
