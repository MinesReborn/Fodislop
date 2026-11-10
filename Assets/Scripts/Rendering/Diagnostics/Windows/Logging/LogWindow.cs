#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

using Kern.Rendering.Diagnostics.Tools;

using Kern.Diagnostics.Logging;

namespace Kern.Rendering.Diagnostics.Windows;

// Плашка логов: то, что Unity пишет в консоль, видно прямо в игре. На телефоне
// logcat недоступен, а исключение, оборвавшее стартовый пайплайн, выглядит как
// вечный экран загрузки — здесь оно читается текстом.
public sealed class LogWindow : ToolWindow
{
    private const float RefreshIntervalSeconds = 0.25f;

    private const int StackLinesShown = 5;

    private readonly List<RuntimeLogCapture.Entry> _source = new();
    private readonly List<string> _rows = new();
    private readonly List<RuntimeLogCapture.LogKind> _rowKinds = new();

    private Vector2 _scroll;
    private bool _showLogs = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private bool _followEnd = true;
    private bool _frozen;
    private string _summary = "ожидание первой выборки логов";
    private float _nextRefresh;

    public LogWindow()
        : base("Логи", new Rect(292f, 300f, 520f, 320f))
    {
    }

    public override Vector2 MinimumSize => new(320f, 160f);

    // Плашку нельзя закрыть: на телефоне нет F1, и закрытая плашка до конца
    // сессии осталась бы недоступной. Свернуть её можно — от неё остаётся полоса
    // заголовка.
    protected override bool CanClose => false;

    public override void Tick()
    {
        if (!Visible || _frozen)
        {
            return;
        }

        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }

        _nextRefresh = Time.unscaledTime + RefreshIntervalSeconds;
        Rebuild();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _frozen = false;
        _followEnd = true;
        _showLogs = true;
        _showWarnings = true;
        _showErrors = true;
        _nextRefresh = 0f;
        _summary = "ожидание первой выборки логов";
        _source.Clear();
        _rows.Clear();
        _rowKinds.Clear();
    }

    protected override void DrawContent()
    {
        ToolChrome.SectionHeader("СОСТОЯНИЕ");
        GUILayout.Label(_summary, MutedLabelStyle);
        if (RuntimeLogCapture.PersistentLogPath is { } persistentLogPath)
        {
            GUILayout.Label($"Файл на устройстве: {persistentLogPath}", WrappedLabelStyle);
        }

        int errors = RuntimeLogCapture.ErrorCount;
        int warnings = RuntimeLogCapture.WarningCount;
        if (errors > 0)
        {
            ToolChrome.Banner($"ОШИБОК: {errors}", ToolTheme.Error);
        }
        else if (warnings > 0)
        {
            ToolChrome.Banner($"ПРЕДУПРЕЖДЕНИЙ: {warnings}", ToolTheme.Warning);
        }

        GUILayout.Space(4f);
        using (ToolLayout.Horizontal())
        {
            _showLogs = GUILayout.Toggle(_showLogs, "Логи", SegmentedButtonStyle);
            _showWarnings = GUILayout.Toggle(_showWarnings, "Warn", SegmentedButtonStyle);
            _showErrors = GUILayout.Toggle(_showErrors, "Errors", SegmentedButtonStyle);
        }

        using (ToolLayout.Horizontal())
        {
            _frozen = GUILayout.Toggle(_frozen, "Стоп", SegmentedButtonStyle);
            _followEnd = GUILayout.Toggle(_followEnd, "В конец", SegmentedButtonStyle);
            if (GUILayout.Button("Очистить", SecondaryButtonStyle))
            {
                RuntimeLogCapture.Clear();
                Rebuild();
            }

            if (GUILayout.Button("Копировать", SecondaryButtonStyle))
            {
                CopyVisibleToClipboard();
            }

            if (GUILayout.Button("Экспорт", SecondaryButtonStyle))
            {
                CopyFullBufferToClipboard();
            }
        }

        if (_followEnd && !_frozen)
        {
            // Значение ограничит сам GUILayout по высоте содержимого: так лента
            // остаётся прижатой к низу и на Layout, и на Repaint.
            _scroll.y = float.MaxValue;
        }

        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (_rows.Count == 0)
            {
                GUILayout.Label("Строк нет.", MutedLabelStyle);
                return;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                using (ToolLayout.Horizontal())
                {
                    ToolChrome.StatusPip(ColorFor(_rowKinds[i]), 5f);
                    GUILayout.Label(_rows[i], WrappedLabelStyle);
                }
            }
        }
    }

    // Строки собираются в Tick, а не в отрисовке: живой счётчик мог измениться
    // между Layout и Repaint, и число контролов событий разошлось бы.
    private void Rebuild()
    {
        RuntimeLogCapture.CopyTo(_source);
        _rows.Clear();
        _rowKinds.Clear();

        foreach (RuntimeLogCapture.Entry entry in _source)
        {
            if (!IsShown(entry.Kind))
            {
                continue;
            }

            _rows.Add(Format(entry));
            _rowKinds.Add(entry.Kind);
        }

        _summary =
            $"строк {_source.Count}, показано {_rows.Count}, " +
            $"ошибок {RuntimeLogCapture.ErrorCount}, предупреждений {RuntimeLogCapture.WarningCount}";
    }

    private bool IsShown(RuntimeLogCapture.LogKind kind) => kind switch
    {
        RuntimeLogCapture.LogKind.Warning => _showWarnings,
        RuntimeLogCapture.LogKind.Error or RuntimeLogCapture.LogKind.Exception => _showErrors,
        _ => _showLogs,
    };

    private static string Format(RuntimeLogCapture.Entry entry) =>
        entry.StackTrace.Length == 0
            ? $"{entry.Time}  {entry.Message}"
            : $"{entry.Time}  {entry.Message}\n{ShortenStack(entry.StackTrace)}";

    private static string ShortenStack(string stackTrace)
    {
        int lines = 0;
        for (int i = 0; i < stackTrace.Length; i++)
        {
            if (stackTrace[i] != '\n')
            {
                continue;
            }

            lines++;
            if (lines >= StackLinesShown)
            {
                return stackTrace.Substring(0, i) + "\n…";
            }
        }

        return stackTrace;
    }

    private static Color ColorFor(RuntimeLogCapture.LogKind kind) => kind switch
    {
        RuntimeLogCapture.LogKind.Error or RuntimeLogCapture.LogKind.Exception => ToolTheme.Error,
        RuntimeLogCapture.LogKind.Warning => ToolTheme.Warning,
        _ => ToolTheme.Success,
    };

    private void CopyVisibleToClipboard()
    {
        StringBuilder builder = new();
        builder.AppendLine(_summary);
        for (int i = 0; i < _rows.Count; i++)
        {
            builder.Append('[').Append(_rowKinds[i]).Append("] ").AppendLine(_rows[i]);
        }

        try
        {
            GUIUtility.systemCopyBuffer = builder.ToString();
        }
        catch (Exception exception)
        {
            // Системный буфер обмена есть не на каждой платформе, а плашка
            // обязана остаться рабочей и без него.
            Debug.LogWarning($"[LogWindow] Буфер обмена недоступен: {exception.Message}");
        }
    }

    private static void CopyFullBufferToClipboard()
    {
        var entries = new List<RuntimeLogCapture.Entry>();
        RuntimeLogCapture.CopyTo(entries);
        var builder = new StringBuilder();
        builder.AppendLine(
            $"строк {entries.Count}, ошибок {RuntimeLogCapture.ErrorCount}, " +
            $"предупреждений {RuntimeLogCapture.WarningCount}");
        if (RuntimeLogCapture.PersistentLogPath is { } persistentLogPath)
        {
            builder.Append("Файл на устройстве: ").AppendLine(persistentLogPath);
        }

        foreach (RuntimeLogCapture.Entry entry in entries)
        {
            builder.Append('[').Append(entry.Time).Append("] [").Append(entry.Kind).Append("] ")
                .AppendLine(entry.Message);
            if (entry.StackTrace.Length > 0)
            {
                builder.AppendLine(entry.StackTrace);
            }
        }

        try
        {
            GUIUtility.systemCopyBuffer = builder.ToString();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[LogWindow] Не удалось экспортировать полный журнал: {exception.Message}");
        }
    }
}
