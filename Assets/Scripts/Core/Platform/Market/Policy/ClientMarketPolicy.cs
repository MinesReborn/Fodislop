#nullable enable

using System;
using System.Globalization;

namespace Kern.Core.Platform;

public static class ClientMarketPolicy
{
    public static string SystemCountryCode
    {
        get
        {
            try
            {
                return RegionInfo.CurrentRegion.TwoLetterISORegionName;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    public static bool IsVkAvailable =>
        string.Equals(SystemCountryCode, "RU", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SystemCountryCode, "BY", StringComparison.OrdinalIgnoreCase);

    public static string DefaultLanguageCode => IsVkAvailable ? "ru" : "en";
}
