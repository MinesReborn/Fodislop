#nullable enable

using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

[InitializeOnLoad]
internal static class UnityVersionGate
{
    private const string RequiredVersion = "6000.6.0f1";
    private static readonly Regex s_versionPattern = new(
        "^(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)(?<channel>[abfp])(?<revision>\\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool s_isBlocked;
    private static bool s_hasReported;

    static UnityVersionGate()
    {
        if (IsSupported(Application.unityVersion))
        {
            return;
        }

        s_isBlocked = true;
        EditorApplication.delayCall += Enforce;
    }

    private static void Enforce()
    {
        if (!s_isBlocked)
        {
            return;
        }

        EditorApplication.delayCall -= Enforce;
        EditorApplication.isPlaying = false;

        string message =
            $"Kern requires Unity {RequiredVersion} or newer, " +
            $"but this project is running in Unity {Application.unityVersion}. " +
            "The Editor will close to prevent importing or modifying project data with an older version.";

        Debug.LogError($"[UnityVersionGate] {message}");

        if (!Application.isBatchMode && !s_hasReported)
        {
            s_hasReported = true;
            EditorUtility.DisplayDialog("Unsupported Unity version", message, "Close");
        }

        EditorApplication.Exit(1);
    }

    private static bool IsSupported(string actualVersion)
    {
        if (!TryParse(RequiredVersion, out ParsedVersion required) ||
            !TryParse(actualVersion, out ParsedVersion actual))
        {
            return false;
        }

        return actual.CompareTo(required) >= 0;
    }

    private static bool TryParse(string value, out ParsedVersion version)
    {
        Match match = s_versionPattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out int major) ||
            !int.TryParse(match.Groups["minor"].Value, out int minor) ||
            !int.TryParse(match.Groups["patch"].Value, out int patch) ||
            !int.TryParse(match.Groups["revision"].Value, out int revision))
        {
            version = default;
            return false;
        }

        int channel = match.Groups["channel"].Value[0] switch
        {
            'a' => 0,
            'b' => 1,
            'f' => 2,
            'p' => 3,
            _ => -1,
        };

        version = new ParsedVersion(major, minor, patch, channel, revision);
        return channel >= 0;
    }

    private readonly record struct ParsedVersion(
        int Major,
        int Minor,
        int Patch,
        int Channel,
        int Revision) : IComparable<ParsedVersion>
    {
        public int CompareTo(ParsedVersion other)
        {
            int result = Major.CompareTo(other.Major);
            if (result != 0)
            {
                return result;
            }

            result = Minor.CompareTo(other.Minor);
            if (result != 0)
            {
                return result;
            }

            result = Patch.CompareTo(other.Patch);
            if (result != 0)
            {
                return result;
            }

            result = Channel.CompareTo(other.Channel);
            return result != 0 ? result : Revision.CompareTo(other.Revision);
        }
    }
}
