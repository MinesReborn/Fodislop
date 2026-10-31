#nullable enable

using System;
using System.Globalization;
using System.Collections.Generic;
using MinesServer.Networking.Contracts.Packets;

namespace Kern.Game.Visual.Effects.Events.Parameters;

internal sealed class VisualEffectParameters
{
    public uint SourceBotId { get; private set; }
    public bool HasSourceBot { get; private set; }

    /// <summary>
    /// Буквенное имя цвета кристалла для VisualEffect.Crystal ("b", "c", "g", "r", "v", "w") —
    /// совпадает с именем ассета Crys/{цвет}.png. Сервер кладёт его в параметр "color".
    /// </summary>
    public string CrystalColorLetter { get; private set; } = "g";

    /// <summary>Количество кристаллов из параметра "count" (для VisualEffect.Crystal).</summary>
    public int CrystalCount { get; private set; } = 1;

    public static VisualEffectParameters Parse(IReadOnlyList<StringPairPacket>? parameters)
    {
        var result = new VisualEffectParameters();
        if (parameters == null)
        {
            return result;
        }

        foreach (var param in parameters)
        {
            switch (param.Key.ToLowerInvariant())
            {
                case "sourcebotid":
                    if (uint.TryParse(param.Value, out var srcBotId))
                    {
                        result.SourceBotId = srcBotId;
                        result.HasSourceBot = true;
                    }

                    break;

                case "color":
                {
                    // Пока используется только VisualEffect.Crystal: буквенное имя цвета
                    // кристалла; неизвестные значения молча игнорируем (остаётся "g").
                    var color = param.Value.Trim().ToLowerInvariant();
                    if (color is "b" or "c" or "g" or "r" or "v" or "w")
                    {
                        result.CrystalColorLetter = color;
                    }

                    break;
                }

                case "count":
                {
                    if (int.TryParse(
                            param.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var count))
                    {
                        result.CrystalCount = count;
                    }

                    break;
                }

            }
        }

        return result;
    }
}
