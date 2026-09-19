#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kern.Tools.Imgui.Profiling;

internal static class UiLayoutTodoWriter
{
    public static string? WriteTodo(
        string logDirectory,
        string? logPath,
        int frames,
        int spikeFrames,
        double peakLayoutMilliseconds,
        int elementCount,
        long totalChanges,
        List<UiLayoutTracker.Stat> sortedStats)
    {
        var md = new StringBuilder(16384);
        md.AppendLine("# Перераскладки интерфейса — что править")
            .AppendLine()
            .Append("Снято: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(Application.isEditor ? ", редактор" : ", сборка")
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height).AppendLine()
            .AppendLine()
            .Append("- кадров под наблюдением: ").Append(frames).AppendLine()
            .Append("- кадров с раскладкой ≥ ").Append(UiLayoutTracker.SpikeMilliseconds.ToString("F0")).Append(" мс: ").Append(spikeFrames).AppendLine()
            .Append("- пик раскладки: ").Append(peakLayoutMilliseconds.ToString("F2")).AppendLine(" мс")
            .Append("- элементов под наблюдением: ").Append(elementCount).AppendLine()
            .Append("- изменений геометрии: ").Append(totalChanges).AppendLine()
            .Append("- полный журнал: `").Append(logPath ?? "не записан").AppendLine("`")
            .AppendLine();

        if (sortedStats.Count == 0)
        {
            md.AppendLine("Ни один элемент не менял геометрию.");
        }

        int index = 0;
        foreach (UiLayoutTracker.Stat stat in sortedStats)
        {
            index++;
            double perFrame = frames > 0 ? (double)stat.Changes / frames : 0d;
            md.Append("## ").Append(index).Append(". ").AppendLine(stat.Label)
                .AppendLine()
                .Append("- [ ] ").AppendLine(Advice(stat))
                .Append("- путь: `").Append(stat.FullPath).AppendLine("`")
                .Append("- тип: ").AppendLine(stat.ElementType)
                .Append("- изменений: ").Append(stat.Changes)
                .Append(" (").Append(perFrame.ToString("P1")).Append(" кадров), размер ").Append(stat.SizeChanges)
                .Append(", только позиция ").Append(stat.PositionOnlyChanges)
                .Append(", в пиковых кадрах ").Append(stat.SpikeChanges).AppendLine()
                .Append("- ширина ").Append(stat.MinWidth.ToString("F0")).Append("…").Append(stat.MaxWidth.ToString("F0"))
                .Append(", высота ").Append(stat.MinHeight.ToString("F0")).Append("…").Append(stat.MaxHeight.ToString("F0"))
                .AppendLine();
            if (stat.TextSamples.Count > 0)
            {
                md.Append("- тексты: ");
                for (int i = 0; i < stat.TextSamples.Count; i++)
                {
                    md.Append(i > 0 ? ", " : string.Empty).Append('«').Append(Sanitize(stat.TextSamples[i])).Append('»');
                }

                md.AppendLine();
            }

            md.AppendLine();
        }

        try
        {
            Directory.CreateDirectory(logDirectory);
            string path = Path.Combine(logDirectory, "ui_layout_todo.md");
            File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
            return path;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UiLayoutTracker] TODO раскладки не записан: {exception.Message}");
            return null;
        }
    }

    private static string Advice(UiLayoutTracker.Stat stat)
    {
        if (stat.SizeChanges > 0 && stat.TextSamples.Count > 0)
        {
            return "Размер зависит от текста: задать фиксированную ширину или min-width под самый длинный текст, " +
                "цифры — моноширинными, чтобы смена значения не перераскладывала соседей.";
        }

        if (stat.SizeChanges == 0)
        {
            return "Меняется только позиция: двигать через style.translate, а не left/top/margin — " +
                "translate не запускает раскладку.";
        }

        return "Меняется размер без текста: найти, кто пишет width/height/display/flex этому элементу " +
            "или его детям, и писать только при реальном изменении значения.";
    }

    private static string Sanitize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}
