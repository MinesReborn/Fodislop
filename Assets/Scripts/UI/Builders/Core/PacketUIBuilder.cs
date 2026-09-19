#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.UI.Builders;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Kern.UI;
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

    public void AddChildren(VisualElement parent, IContainerComponentPacket packet)
    {
        foreach (IGUIComponentPacket childPacket in packet.Children)
        {
            parent.Add(Build(childPacket));
        }
    }

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
