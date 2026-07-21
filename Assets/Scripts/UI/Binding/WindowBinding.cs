using System;
using System.Collections.Generic;
using Fodinae.UI.Controls;
using SmartFormat;
using SmartFormat.Extensions;
using UnityEngine.UIElements;

namespace Fodinae.UI.Binding
{
    /// <summary>
    /// Manages SmartFormat binding for a single GUI window.
    /// Scans the VisualElement tree for named input controls (value sources)
    /// and labels containing SmartFormat templates (value consumers).
    /// When any input changes, all templates are re-evaluated.
    /// </summary>
    public class WindowBinding : IDisposable
    {
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

        /// <summary>
        /// Bind to a fully-built window VisualElement tree.
        /// Discovers inputs and templates, then performs the initial format pass.
        /// </summary>
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
            }
        }

        private void RefreshAll()
        {
            if (_disposed)
            {
                return;
            }

            // Collect current values from all input controls by name
            var values = new Dictionary<string, object>(_inputs.Count);
            foreach (var kvp in _inputs)
            {
                values[kvp.Key] = GetControlValue(kvp.Value);
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
        }

        private static object GetControlValue(VisualElement element)
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
}
