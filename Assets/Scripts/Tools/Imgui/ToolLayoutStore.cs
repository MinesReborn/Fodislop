#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kern.Tools.Imgui;

public static class ToolLayoutStore
{
    private const string FileName = "tool_layout.json";

    private static readonly Dictionary<string, LayoutEntry> _Entries = [];
    private static bool _loaded;
    private static bool _dirty;
    private static float _scale;

    public static float Scale
    {
        get
        {
            EnsureLoaded();
            return _scale;
        }
        set
        {
            EnsureLoaded();
            _scale = IsFinite(value) ? Mathf.Clamp(value, 1f, 2.5f) : 0f;
            _dirty = true;
        }
    }

    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static void Load(ToolWindow window)
    {
        EnsureLoaded();
        if (!_Entries.TryGetValue(window.Title, out LayoutEntry entry))
        {
            return;
        }

        var rect = new Rect(entry.X, entry.Y, entry.Width, entry.Height);

        // Сохранённое значение приходит из прошлого запуска и не обязано быть
        // осмысленным: экран мог смениться, а файл — пережить обрыв записи.
        // Мусор молча отбрасывается, окно остаётся на месте по умолчанию.
        // Границы экрана дальше наложит сам реестр.
        if (IsFinite(rect) && rect.width > 1f && rect.height > 1f)
        {
            window.Rect = rect;
        }

        // Видимость восстанавливается только у окон, которые можно закрыть.
        // Список инструментов закрыть нельзя, он и есть путь ко всем
        // остальным: сохранённое «скрыт» вернуло бы состояние, из которого нет
        // выхода ничем, кроме стирания файла вручную.
        if (window.CanRestoreVisibility)
        {
            window.Visible = entry.Visible;
        }

        window.Collapsed = entry.Collapsed;
    }

    public static void Save(ToolWindow window)
    {
        if (!IsFinite(window.Rect))
        {
            return;
        }

        EnsureLoaded();
        _Entries[window.Title] = new LayoutEntry
        {
            Title = window.Title,
            X = window.Rect.x,
            Y = window.Rect.y,
            Width = window.Rect.width,
            Height = window.Rect.height,
            Visible = window.Visible,
            Collapsed = window.Collapsed,
        };
        _dirty = true;
    }

    public static void Discard(ToolWindow window)
    {
        EnsureLoaded();
        if (_Entries.Remove(window.Title))
        {
            _dirty = true;
        }
    }

    public static void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        var payload = new LayoutFile { Windows = new List<LayoutEntry>(_Entries.Values), Scale = _scale };
        string temporaryPath = Path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(payload, prettyPrint: true));
            File.Copy(temporaryPath, Path, overwrite: true);
            File.Delete(temporaryPath);
            _dirty = false;
        }
        catch (Exception exception)
        {
            // Потеря раскладки отладочных окон не стоит ни одного прерванного
            // кадра игры, поэтому здесь предупреждение, а не исключение.
            Debug.LogWarning($"[ToolLayoutStore] Раскладка не сохранена: {exception.Message}");
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            var payload = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path));
            if (payload?.Windows == null)
            {
                return;
            }

            foreach (LayoutEntry entry in payload.Windows)
            {
                if (!string.IsNullOrEmpty(entry.Title))
                {
                    _Entries[entry.Title] = entry;
                }
            }

            _scale = IsFinite(payload.Scale) && payload.Scale >= 1f && payload.Scale <= 2.5f
                ? payload.Scale : 0f;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ToolLayoutStore] Раскладка не прочитана: {exception.Message}");
        }
    }

    private static bool IsFinite(Rect rect) =>
        IsFinite(rect.x) && IsFinite(rect.y) && IsFinite(rect.width) && IsFinite(rect.height);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    [Serializable]
    private struct LayoutEntry
    {
        public string Title;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public bool Visible;
        public bool Collapsed;
    }

    [Serializable]
    private sealed class LayoutFile
    {
        public List<LayoutEntry>? Windows;
        public float Scale;
    }
}
