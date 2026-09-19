#nullable enable

using System.Globalization;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Shared.Packets;

namespace Kern.UI.Builders;
public static class AttachedProperties
{
    public static bool TryGetFloat(IGUIComponentPacket packet, string key, out float value)
    {
        value = 0f;
        string? raw = Find(packet, key);
        return raw != null && float.TryParse(
            raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryGetInt(IGUIComponentPacket packet, string key, out int value)
    {
        value = 0;
        string? raw = Find(packet, key);
        return raw != null && int.TryParse(
            raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool Has(IGUIComponentPacket packet, string key)
    {
        return Find(packet, key) != null;
    }

    public static string? Find(IGUIComponentPacket packet, string key)
    {
        StringPairPacket[]? properties = packet.AttachedProperties;
        if (properties == null)
        {
            return null;
        }

        foreach (StringPairPacket property in properties)
        {
            if (property.Key == key)
            {
                return property.Value;
            }
        }

        return null;
    }
}
