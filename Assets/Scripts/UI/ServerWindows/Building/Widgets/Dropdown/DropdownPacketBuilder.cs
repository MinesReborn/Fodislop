#nullable enable

using System.Collections.Generic;
using System.Globalization;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public abstract class DropdownPacketBuilder<TPacket, TValue> : PacketUIBuilderBase<TPacket>
    where TPacket : DropdownComponentPacket<TValue>
    where TValue : notnull
{
    protected virtual string Caption(TValue value) => value.ToString()!;

    protected override VisualElement BuildTyped(TPacket packet, PacketUIBuilder builder)
    {
        var options = new List<string>(packet.Values.Length);
        foreach (TValue value in packet.Values)
        {
            options.Add(Caption(value));
        }

        string defaultValue = Caption(packet.DefaultValue);

        var dropdown = new DropdownField(options, options.IndexOf(defaultValue))
        {
            name = packet.Name,
            value = defaultValue,
        };
        dropdown.SetEnabled(packet.IsEnabled);
        return dropdown;
    }
}

public class IntDropdownPacketBuilder : DropdownPacketBuilder<IntDropdownPacket, int>
{
    protected override string Caption(int value) => value.ToString(CultureInfo.InvariantCulture);
}

public class StringDropdownPacketBuilder : DropdownPacketBuilder<StringDropdownPacket, string>
{
    protected override string Caption(string value) => value;
}
