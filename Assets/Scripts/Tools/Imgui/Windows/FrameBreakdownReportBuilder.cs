#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Tools.Imgui.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

internal static class FrameBreakdownReportBuilder
{
    public static string BuildReport<TTab>(
        IReadOnlyList<(TTab Tab, string Label)> tabs,
        IReadOnlyDictionary<TTab, List<FrameBreakdownRow>> rows,
        TTab searchTab,
        MarkerSearch search)
        where TTab : notnull
    {
        var report = new StringBuilder(8192);
        report.Append("Разбор кадра, ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(Application.isEditor ? ", редактор" : ", сборка")
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height)
            .Append(", ").Append(SystemInfo.graphicsDeviceType)
            .AppendLine();

        foreach ((TTab tab, string label) in tabs)
        {
            if (EqualityComparer<TTab>.Default.Equals(tab, searchTab) && search.QueryTooShort)
            {
                continue;
            }

            report.AppendLine().Append("=== ").Append(label.ToUpperInvariant()).AppendLine(" ===");
            foreach (FrameBreakdownRow row in rows[tab])
            {
                if (row.Kind == FrameBreakdownRowKind.Header)
                {
                    report.AppendLine().Append("## ").AppendLine(row.Text);
                }
                else
                {
                    report.AppendLine(row.Text);
                }
            }
        }

        return report.ToString();
    }
}
