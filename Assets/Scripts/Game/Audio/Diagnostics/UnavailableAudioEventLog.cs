#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.Game.Audio.Diagnostics;

internal static class UnavailableAudioEventLog
{
    private static readonly HashSet<string> s_loggedEventNames = new();
    private static readonly object s_lock = new();

    public static void LogOnce(string eventName, string message, LogType logType)
    {
        lock (s_lock)
        {
            if (!s_loggedEventNames.Add(eventName))
            {
                return;
            }
        }

        Debug.unityLogger.Log(logType, message);
    }
}
