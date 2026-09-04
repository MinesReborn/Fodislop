#nullable enable

using System.Collections.Generic;
using System.Globalization;
using MinesServer.Networking.Shared.Packets;

namespace Fodinae.Game;

/// <summary>
/// Parsed parameters from AudioPacket (attractor coordinates, source bot, texture overrides, Effekseer dynamic inputs).
/// </summary>
internal sealed class ServerAudioParameters
{
    public uint SourceBotId { get; private set; }
    public bool HasSourceBot { get; private set; }
    public ushort AttractorX { get; private set; }
    public ushort AttractorY { get; private set; }
    public bool HasAttractorPosition { get; private set; }
    public Dictionary<string, string>? TextureOverrideMap { get; private set; }
    public float[]? EffekseerDynamicInputs { get; private set; }

    public static ServerAudioParameters Parse(IReadOnlyList<StringPairPacket>? parameters)
    {
        var result = new ServerAudioParameters();
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

                case "x":
                    if (ushort.TryParse(param.Value, out var attractorX))
                    {
                        result.AttractorX = attractorX;
                        result.HasAttractorPosition = true;
                    }

                    break;

                case "y":
                    if (ushort.TryParse(param.Value, out var attractorY))
                    {
                        result.AttractorY = attractorY;
                        result.HasAttractorPosition = true;
                    }

                    break;

                case "map":
                    if (!string.IsNullOrEmpty(param.Value))
                    {
                        result.TextureOverrideMap = new Dictionary<string, string>();
                        var entries = param.Value.Split(';');
                        foreach (var entry in entries)
                        {
                            if (string.IsNullOrEmpty(entry))
                            {
                                continue;
                            }

                            var eqIdx = entry.IndexOf('=');
                            if (eqIdx > 0 && eqIdx < entry.Length - 1)
                            {
                                var key = entry.Substring(0, eqIdx).Trim();
                                var val = entry.Substring(eqIdx + 1).Trim();
                                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                                {
                                    result.TextureOverrideMap[key] = val;
                                }
                            }
                        }
                    }

                    break;

                case "props":
                    if (!string.IsNullOrEmpty(param.Value))
                    {
                        var parts = param.Value.Split(',');
                        result.EffekseerDynamicInputs = new float[parts.Length];
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (float.TryParse(
                                    parts[i],
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out var propVal))
                            {
                                result.EffekseerDynamicInputs[i] = propVal;
                            }
                        }
                    }

                    break;
            }
        }

        return result;
    }
}
