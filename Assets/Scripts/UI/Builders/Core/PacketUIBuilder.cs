#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.UI.Builders;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;
public class PacketUIBuilder
{
    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly PacketUIBuilderFactory _builderFactory = new();

    public PacketUIBuilder(
        IAssetLoader assetLoader,
        IAsyncOperationSupervisor operations,
        IWebAssetLoader webAssetLoader)
    {
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _webAssetLoader = webAssetLoader ?? throw new ArgumentNullException(nameof(webAssetLoader));
    }

    internal IAssetLoader AssetLoader => _assetLoader;
    internal IAsyncOperationSupervisor Operations => _operations;
    internal IWebAssetLoader WebAssetLoader => _webAssetLoader;

    public VisualElement Build(IGUIComponentPacket packet)
    {
        PacketUIBuilderBase? builder = _builderFactory.CreateBuilder(packet);
        if (builder == null)
        {
            string message = $"[PacketUI] Unsupported GUI component: {packet.GetType().FullName} " +
                $"(code {packet.PacketCode}).";
            Debug.LogError(message);
            throw new NotSupportedException(message);
        }

        VisualElement element = builder.Build(packet, this);
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

        if (TryCanvasValue(packet, "Canvas.X", out float left))
        {
            style.left = left;
            absolute = true;
        }

        if (TryCanvasValue(packet, "Canvas.Y", out float top))
        {
            style.top = top;
            absolute = true;
        }

        if (TryCanvasValue(packet, "Canvas.Width", out float width))
        {
            if (width <= 0f)
            {
                throw new InvalidOperationException("Canvas.Width must be positive.");
            }

            style.width = width;
            absolute = true;
        }

        if (TryCanvasValue(packet, "Canvas.Height", out float height))
        {
            if (height <= 0f)
            {
                throw new InvalidOperationException("Canvas.Height must be positive.");
            }

            style.height = height;
            absolute = true;
        }

        if (absolute)
        {
            element.AddToClassList("abs");
        }
    }

    private static bool TryCanvasValue(IGUIComponentPacket packet, string key, out float value)
    {
        if (!AttachedProperties.Has(packet, key))
        {
            value = 0f;
            return false;
        }

        if (!AttachedProperties.TryGetFloat(packet, key, out value) ||
            float.IsNaN(value) || float.IsInfinity(value))
        {
            string raw = AttachedProperties.Find(packet, key) ?? string.Empty;
            throw new InvalidOperationException(
                $"Invalid {key}='{raw}' on {packet.GetType().Name}.");
        }

        return true;
    }
}
