#nullable enable

using System.Collections.Generic;
using System.Text;
using Kern.Tools.Imgui.Profiling;

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
        // Время, сборку, экран и устройство пишет общая шапка DiagnosticReport.
        var report = new StringBuilder(8192);

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
