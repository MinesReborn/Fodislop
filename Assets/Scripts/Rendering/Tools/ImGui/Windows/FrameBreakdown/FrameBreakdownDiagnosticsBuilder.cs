#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Tools.ImGui.Profiling;
using UnityEngine;

namespace Kern.Tools.ImGui.Windows;

internal static class FrameBreakdownDiagnosticsBuilder
{
    private const double BudgetMilliseconds = FrameBudget.TargetFrameMilliseconds;

    private static readonly List<KeyValuePair<string, int>> s_subtreeSorted = [];
    private static readonly List<KeyValuePair<Texture, int>> s_textureSorted = [];

    public static void BuildLayoutRows(
        List<FrameBreakdownRow> rows,
        UILayoutTracker layout)
    {
        const int keep = 40;
        layout.Rebuild(keep);
        rows.Clear();
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "РАСКЛАДКА ИНТЕРФЕЙСА ИГРЫ"));
        if (!layout.LayoutMarkerAvailable)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Warning, "Маркер PanelSettings.ValidateLayout ещё не сработал — пики не определить."));
        }

        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Раскладка сейчас {layout.LastLayoutMilliseconds:F2} мс, пик {layout.PeakLayoutMilliseconds:F2} мс",
            (float)(layout.LastLayoutMilliseconds / Math.Max(BudgetMilliseconds, layout.PeakLayoutMilliseconds)),
            ToolTheme.Error));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Кадров {layout.Frames}, из них с раскладкой ≥ {UILayoutTracker.SpikeMilliseconds:F0} мс: {layout.SpikeFrames}"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Элементов под наблюдением {layout.ElementCount}; сдвинулось в последнем кадре {layout.ChangedInLastFrame}, " +
            $"максимум за кадр {layout.PeakChangedInFrame}, всего изменений {layout.TotalChanges}"));

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, $"Журнал каждой перераскладки: {layout.LogPath ?? "не открыт"}"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            layout.LastTodoPath != null
                ? $"TODO: {layout.LastTodoPath} (пишется и при закрытии окна)"
                : "TODO запишется кнопкой или при закрытии окна."));

        AddUICompositionRows(rows, layout);

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "КТО ДВИГАЕТСЯ — СНАЧАЛА В ПИКОВЫХ КАДРАХ"));
        if (layout.Sorted.Count == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Ни один элемент не менял геометрию с начала наблюдения."));
            return;
        }

        int top = Math.Max(1, layout.Sorted[0].Changes);
        foreach (UILayoutTracker.Stat stat in layout.Sorted)
        {
            double perFrame = layout.Frames > 0 ? (double)stat.Changes / layout.Frames : 0d;
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{stat.Label}   в пиках {stat.SpikeChanges}, всего {stat.Changes} ({perFrame:P0} кадров), " +
                $"размер {stat.SizeChanges}, сейчас {stat.LastRect.width:F0}×{stat.LastRect.height:F0} @ {stat.LastRect.x:F0},{stat.LastRect.y:F0}",
                (float)stat.Changes / top,
                ToolTheme.Error));
        }
    }

    private static void AddUICompositionRows(List<FrameBreakdownRow> rows, UILayoutTracker layout)
    {
        UILayoutTracker.RenderStats stats = layout.Stats;
        int atlasLimit = stats.AtlasLimit > 0 ? stats.AtlasLimit : 64;
        int bigTextures = 0;
        foreach (Texture texture in stats.Textures.Keys)
        {
            if (texture.width > atlasLimit || texture.height > atlasLimit)
            {
                bigTextures++;
            }
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "СОСТАВ ВИДИМОГО ИНТЕРФЕЙСА (РАЗ В СЕКУНДУ)"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Видимых элементов {stats.Visible}; текстов {stats.Texts} ({stats.TextCharacters} символов), " +
            $"с обводкой {stats.OutlinedTexts}, с тенью {stats.ShadowedTexts}"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Картинок {stats.Images}, разных текстур {stats.Textures.Count}, из них крупнее {atlasLimit} px (вне атласа) {bigTextures}; " +
            "слотов текстур на пакет 8"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Обрезающих контейнеров {stats.Clips} (со скруглением — стенсил — {stats.RoundedClips}); " +
            $"полупрозрачных {stats.Translucent}; сдвинутых translate {stats.Translated}"));

        s_subtreeSorted.Clear();
        s_subtreeSorted.AddRange(stats.VisibleBySubtree);
        s_subtreeSorted.Sort(static (left, right) => right.Value.CompareTo(left.Value));
        int maxSubtree = s_subtreeSorted.Count > 0 ? Math.Max(1, s_subtreeSorted[0].Value) : 1;
        for (int i = 0; i < s_subtreeSorted.Count && i < 12; i++)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"· {s_subtreeSorted[i].Key}   видимых {s_subtreeSorted[i].Value}",
                (float)s_subtreeSorted[i].Value / maxSubtree,
                ToolTheme.Accent));
        }

        s_textureSorted.Clear();
        s_textureSorted.AddRange(stats.Textures);
        s_textureSorted.Sort(static (left, right) => right.Value.CompareTo(left.Value));
        for (int i = 0; i < s_textureSorted.Count && i < 15; i++)
        {
            Texture texture = s_textureSorted[i].Key;
            bool outsideAtlas = texture.width > atlasLimit || texture.height > atlasLimit;
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"· текстура «{texture.name}» {texture.width}×{texture.height}   элементов {s_textureSorted[i].Value}" +
                (outsideAtlas ? "   · вне атласа" : string.Empty)));
        }
    }

    public static void BuildSpikeRows(List<FrameBreakdownRow> rows, SpikeSweep spikes)
    {
        rows.Clear();
        if (spikes.Running)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Header,
                $"ЛОВЛЮ ВСПЛЕСКИ: {spikes.Processed} ИЗ {spikes.Total} МАРКЕРОВ"));
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"Пока поймано {spikes.SpikesSeen} просевших кадров из {spikes.FramesSeen}. " +
                "Стойте на месте и не трогайте окна до конца прохода."));
            return;
        }

        if (!spikes.HasResults)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                "Кнопка «Поймать всплески» записывает все маркеры покадрово пачками дольше секунды, " +
                $"находит провисы (не короче {FrameBudget.StallMinimumMilliseconds:F0} мс и в {FrameBudget.StallOverBaseline:F1} раза дольше медианы) и показывает, " +
                "какие маркеры в них выросли сильнее всего. Проход идёт около полуминуты."));
            return;
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ПРОСЕВШИЕ КАДРЫ"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"· {spikes.SpikesSeen} из {spikes.FramesSeen} кадров; обычный кадр {spikes.MedianFrameMilliseconds:F1} мс, " +
            $"просевший {spikes.SpikeFrameMilliseconds:F1} мс (по {spikes.FrameSource})"));
        if (spikes.SpikesSeen == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Выбросов за проход не было."));
            return;
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, $"ЧТО ВЫРОСЛО В ПРОСЕВШИХ КАДРАХ ({spikes.Hits.Count})"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            "«+X мс» — насколько маркер дороже своей обычной медианы в просевшем кадре. " +
            "Родитель и ребёнок оба в списке; ищите самый глубокий с тем же приростом."));
        double scale = 1.0;
        foreach (SpikeSweep.Hit hit in spikes.Hits)
        {
            scale = Math.Max(scale, hit.ExcessMilliseconds);
        }

        foreach (SpikeSweep.Hit hit in spikes.Hits)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{hit.Marker.Name}   +{hit.ExcessMilliseconds:F2} мс  (обычно {hit.BaselineMilliseconds:F2}, " +
                $"кадров {hit.SpikeFrames}, {hit.Marker.Category.Name})",
                (float)(hit.ExcessMilliseconds / scale),
                ToolTheme.Error,
                hit.Marker.Name));
        }
    }

    public static void BuildHotRows(List<FrameBreakdownRow> rows, HotMarkerSweep sweep)
    {
        rows.Clear();
        if (sweep.Running)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Header,
                $"ПРОЧЁСЫВАНИЕ: {sweep.Processed} ИЗ {sweep.Total} МАРКЕРОВ"));
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Каждая пачка копит несколько кадров; результаты появятся после прохода."));
            return;
        }

        if (!sweep.HasResults)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                "Кнопка «Прочесать все маркеры» по очереди записывает все временные маркеры " +
                "Unity и оставляет самые горячие: пассы RenderGraph, UI, всё неразмеченное. " +
                "Проход занимает несколько секунд."));
            return;
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, $"САМЫЕ ГОРЯЧИЕ {sweep.Hits.Count} ИЗ {sweep.Total}"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            "Пачки измерены в разные кадры; значения суммируют потоки и вложенные вызовы. " +
            "Это список кандидатов, не раскладка одного кадра. UI включает окна редактора."));
        double scale = BudgetMilliseconds;
        foreach (HotMarkerSweep.Hit hit in sweep.Hits)
        {
            scale = Math.Max(scale, hit.AverageMilliseconds);
        }

        foreach (HotMarkerSweep.Hit hit in sweep.Hits)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{hit.Marker.Name}   {hit.AverageMilliseconds:F2} мс  (пик {hit.PeakMilliseconds:F2}, {hit.Marker.Category.Name})",
                (float)(hit.AverageMilliseconds / scale),
                ToolTheme.Error,
                hit.Marker.Name));
        }
    }
}
