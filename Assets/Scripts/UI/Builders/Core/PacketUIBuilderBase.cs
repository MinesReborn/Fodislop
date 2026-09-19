#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public abstract class PacketUIBuilderBase
{
    public abstract VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder);
}

public abstract class PacketUIBuilderBase<TPacket> : PacketUIBuilderBase
    where TPacket : IGUIComponentPacket
{
    public sealed override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
    {
        return BuildTyped((TPacket)packet, builder);
    }

    protected abstract VisualElement BuildTyped(TPacket packet, PacketUIBuilder builder);
}
