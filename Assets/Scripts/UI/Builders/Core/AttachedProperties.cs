#nullable enable

using System.Globalization;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Shared.Packets;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// Чтение присоединённых свойств пакета (Canvas.X, Grid.Row, DockPanel.Dock).
    /// </summary>
    /// <remarks>
    /// Числа протокола всегда записаны в инвариантной культуре — точка как
    /// десятичный разделитель. Разбор по текущей культуре на Windows с
    /// региональными RU/DE/TR молча теряет геометрию окон: TryParse возвращает
    /// false, координата остаётся нулём, и окно уезжает в угол без единой
    /// ошибки в логе. Правило одно на весь протокол, поэтому и место у него
    /// одно: разбор координат уже был исправлен в одном строителе и остался
    /// сломанным в другом — ровно потому, что жил в двух местах.
    /// </remarks>
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
}
