#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Core
{
    /// <summary>
    /// Legacy diagnostic hook retained for compatibility. Startup fail-fast is
    /// explicit at the transition boundary; ordinary log errors must never
    /// pause the editor or stop the game globally.
    ///
    /// Важно: НЕ fail-fast'им по внутренним ошибкам самого редактора.
    /// Unity 6000 с com.unity.ide.visualstudio 2.0.28 кидает шум при каждом
    /// domain reload (Cannot exit scope 'TScope', TypeInitializationException
    /// VisualStudioEditor и т.п.) — это не наш код, и из-за него приложение
    /// падать не должно. Фильтруем по содержимому и стеку.
    /// </summary>
    public static class FailFastLogHandler
    {
        private static bool _registered;
        private static bool _failing;
        private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);

        // Маркеры внутренностей редактора: lifecycle-скоупы, domain reload,
        // IDE-пакеты, test-runner. Ошибки из этих мест — не нашего кода.
        private static readonly string[] UnityEditorNoiseMarkers =
        {
            "Unity.Scripting.LifecycleManagement",
            "UnityEngine.DomainReloadLifecycleController",
            "UnityEngine.UnityLifecycleInternal",
            "Microsoft.Unity.VisualStudio",
            "UnityEditor.EditorAssemblies",
            "UnityEditor.TestTools",
            "UnityEngine.TestRunner",
            "UnityEngine.TestTools",
        };

        // Типовые тексты ошибок lifecycle-менеджмента при code reload.
        private static readonly string[] UnityEditorNoiseMessages =
        {
            "Cannot exit scope of type 'TScope'",
            "could not enter scope",
            "Lifecycle ERROR",
            "are restricted during",
        };

        private static bool IsUnityEditorNoise(string message, string stackTrace)
        {
            foreach (string marker in UnityEditorNoiseMessages)
            {
                if (message.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // В стеке есть код проекта (Assets/ или Fodinae) — это наша
            // ошибка, fail-fast по ней положен.
            if (!string.IsNullOrEmpty(stackTrace) &&
                (stackTrace.Contains("Assets/", StringComparison.Ordinal) ||
                 stackTrace.Contains("Fodinae", StringComparison.Ordinal)))
            {
                return false;
            }

            foreach (string marker in UnityEditorNoiseMarkers)
            {
                if (!string.IsNullOrEmpty(stackTrace) &&
                    stackTrace.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemReload()
        {
            _registered = false;
            _failing = false;
            ReportedFailures.Clear();
        }

        /// <summary>Подписывает обработчик, если это редактор и подписки ещё нет.</summary>
        public static void EnsureRegistered()
        {
#if UNITY_EDITOR
            if (_registered)
            {
                return;
            }

            Application.logMessageReceived += OnLogMessage;
            _registered = true;
#endif
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (_failing)
            {
                return;
            }

            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            if (IsUnityEditorNoise(message, stackTrace))
            {
                return;
            }

            // Unity can invoke logMessageReceived once per frame when a scene
            // MonoBehaviour retries initialization from Update. Fail-fast is a
            // diagnostic breakpoint, not a second error pipeline: report each
            // distinct message/stack pair once per play session.
            string failureKey = string.Concat(type, "\n", message, "\n", stackTrace);
            if (!ReportedFailures.Add(failureKey))
            {
                return;
            }

            // Do not intercept or re-emit the error. Startup code reports
            // failures through SceneTransitionTicket and TransitionChanged.
        }
    }
}
