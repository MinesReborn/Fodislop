#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using MinesServer.Networking.Shared.Packets;

namespace Kern.Game;

internal sealed class ServerAudioParameters
{
    public uint SourceBotID { get; private set; }
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
                    if (uint.TryParse(param.Value, out var srcBotID))
                    {
                        result.SourceBotID = srcBotID;
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
                        result.TextureOverrideMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string entry in param.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            string trimmed = entry.Trim();
                            if (trimmed.Length == 0)
                            {
                                continue;
                            }

                            int eqIdx = trimmed.IndexOf('=', StringComparison.Ordinal);
                            if (eqIdx > 0 && eqIdx < trimmed.Length - 1)
                            {
                                string key = trimmed[..eqIdx].Trim();
                                string val = trimmed[(eqIdx + 1)..].Trim();
                                if (key.Length > 0 && val.Length > 0)
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
                        var parsed = new List<float>();
                        foreach (string part in param.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            string trimmed = part.Trim();
                            if (trimmed.Length == 0)
                            {
                                continue;
                            }

                            if (float.TryParse(
                                    trimmed,
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out var propVal))
                            {
                                parsed.Add(propVal);
                            }
                        }

                        if (parsed.Count > 0)
                        {
                            result.EffekseerDynamicInputs = parsed.ToArray();
                        }
                    }

                    break;
            }
        }

        return result;
    }
}
