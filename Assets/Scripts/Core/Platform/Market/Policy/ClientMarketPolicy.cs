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

    // Язык по умолчанию — русский для России и Беларуси, иначе английский
    public static string DefaultLanguageCode =>
        string.Equals(SystemCountryCode, "RU", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SystemCountryCode, "BY", StringComparison.OrdinalIgnoreCase)
            ? "ru"
            : "en";
}
