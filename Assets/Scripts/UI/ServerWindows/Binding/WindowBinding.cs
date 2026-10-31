#nullable enable

using SmartFormat.Extensions;

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.UI.Controls;
using SmartFormat;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class WindowBinding : IDisposable
{
    private static int s_warmUpStarted;

    /// <summary>
    /// Прогрев движка шаблонов (SmartFormat + NCalc) в фоне. Первое реальное
    /// окно иначе платило за разовую инициализацию парсера ~3 секунды (репорт:
    /// первое открытие маркета — 3.2 с в биндинге, последующие — 1 мс).
    /// Форматирование — чистый управляемый код без Unity API, прогреваться
    /// можно с потока пула; супервизор держит операцию до конца.
    /// </summary>
    public static void WarmUpInBackground(IAsyncOperationSupervisor operations)
    {
        if (Interlocked.Exchange(ref s_warmUpStarted, 1) != 0)
        {
            return;
        }

        operations.Run("window_binding_warmup", cancellationToken => UniTask.RunOnThreadPool(() =>
        {
            var formatter = new SmartFormatter();
            formatter.AddExtensions(new DefaultSource(), new DictionarySource());
            formatter.AddExtensions(new DefaultFormatter(), new LogiCalcFormatter());
            var values = new Dictionary<string, object> { ["crys0"] = 12 };
            formatter.Format("{:calc(0):((359) - ({crys0}))}", values);
            formatter.Format("{crys0}", values);
        }));
    }

    private readonly SmartFormatter _formatter;
    private readonly Dictionary<string, VisualElement> _inputs = new();
    private readonly Dictionary<Label, string> _labelTemplates = new();
    private bool _disposed;

    public WindowBinding()
    {
        _formatter = new SmartFormatter();
        _formatter.AddExtensions(
            new DefaultSource(),
            new DictionarySource());
        _formatter.AddExtensions(
            new DefaultFormatter(),
            new LogiCalcFormatter());
    }

    public void Bind(VisualElement root)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowBinding));
        }

        WalkTree(root);
        RefreshAll();
    }

    private void WalkTree(VisualElement element)
    {
        // Named input controls become binding sources
        if (!string.IsNullOrEmpty(element.name) && IsInputElement(element))
        {
            _inputs[element.name] = element;
            RegisterValueChangeHandler(element);
        }

        // Labels with SmartFormat {placeholders} become binding targets
        if (element is Label label && IsTemplate(label.text))
        {
            _labelTemplates[label] = label.text;
        }

        foreach (var child in element.Children())
        {
            WalkTree(child);
        }
    }

    private static bool IsInputElement(VisualElement element)
    {
        if (element is TextField)
        {
            return true;
        }

        if (element is DropdownField)
        {
            return true;
        }

        if (element is Slider)
        {
            return true;
        }

        if (element is Toggle)
        {
            return true;
        }

        if (element is Selectable)
        {
            return true;
        }

        return false;
    }

    private static bool IsTemplate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // SmartFormat uses {name} syntax
        return text.Contains('{') && text.Contains('}');
    }

    private void RegisterValueChangeHandler(VisualElement element)
    {
        switch (element)
        {
            case TextField tf:
                tf.RegisterValueChangedCallback(_ => RefreshAll());
                break;
            case DropdownField dd:
                dd.RegisterValueChangedCallback(_ => RefreshAll());
                break;
            case Slider sl:
                sl.RegisterValueChangedCallback(_ => RefreshAll());
                break;
            case Toggle tg:
                tg.RegisterValueChangedCallback(_ => RefreshAll());
                break;
            case Selectable sel:
                sel.RegisterValueChangedCallback(_ => RefreshAll());
                break;
            default:
                break;
        }
    }

    private void RefreshAll()
    {
        if (_disposed)
        {
            return;
        }

        // Временный замер (репорт: лаги в маркет-окне): путь драга слайдера
        // гоняет все шаблоны через SmartFormat+NCalc на каждый шаг. В лог
        // попадают только медленные обновления, без спама.
        var refreshWatch = System.Diagnostics.Stopwatch.StartNew();

        // Collect current values from all input controls by name
        var values = new Dictionary<string, object>(_inputs.Count);
        foreach (var kvp in _inputs)
        {
            values[kvp.Key] = GetControlValue(kvp.Value) ?? string.Empty;
        }

        // Re-evaluate all label templates
        foreach (var kvp in _labelTemplates)
        {
            try
            {
                kvp.Key.text = _formatter.Format(kvp.Value, values);
            }
            catch
            {
                // Leave the template text as-is if formatting fails
            }
        }

        if (refreshWatch.ElapsedMilliseconds > 5)
        {
            UnityEngine.Debug.Log(
                $"[WindowBinding] RefreshAll: {_labelTemplates.Count} шаблонов на {_inputs.Count} входах за {refreshWatch.ElapsedMilliseconds} мс.");
        }
    }

    private static object? GetControlValue(VisualElement element)
    {
        return element switch
        {
            TextField tf => tf.value,
            DropdownField dd => dd.value,
            Slider sl => sl.value,
            Toggle tg => tg.value,
            Selectable sel => sel.value,
            _ => null,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inputs.Clear();
        _labelTemplates.Clear();
    }
}
