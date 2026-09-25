#nullable enable

using System;
using System.Collections.Generic;
using Kern.Tools.Imgui.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

internal static class FrameBreakdownSystemBuilder
{
    public static void BuildToolRows(
        List<FrameBreakdownRow> rows,
        List<ToolWindow> toolWindows,
        IReadOnlyList<FrameProbe> interfaceProbes,
        MarkerSearch search,
        Action<List<FrameBreakdownRow>, IReadOnlyList<FrameProbe>, Color, MarkerSearch, bool, double> addProbeList)
    {
        rows.Clear();
        toolWindows.Clear();
        double total = 0d;
        int events = 0;
        foreach (ToolWindow window in ToolWindows.All)
        {
            if (!window.Visible)
            {
                continue;
            }

            toolWindows.Add(window);
            total += window.DrawMilliseconds;
            events += window.DrawEvents;
        }

        toolWindows.Sort(static (left, right) => right.DrawMilliseconds.CompareTo(left.DrawMilliseconds));
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, $"ОКНА ИНСТРУМЕНТОВ — {total:F2} мс ЗА КАДР"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Секундомер внутри каждого окна, {events} событий IMGUI за кадр. Окна редактора сюда не попадают."));
        double scale = Math.Max(total, 1d);
        foreach (ToolWindow window in toolWindows)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{window.Title}   {window.DrawMilliseconds:F2} мс   ({window.DrawEvents} соб.)   " +
                $"мусор: Tick {window.TickAllocatedBytes / 1024d:F1} КБ, отрисовка {window.DrawAllocatedBytes / 1024d:F1} КБ",
                (float)(window.DrawMilliseconds / scale),
                ToolTheme.Accent));
        }

        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Header,
            Application.isEditor ? "МАРКЕРЫ ИНТЕРФЕЙСА — ВМЕСТЕ С ОКНАМИ РЕДАКТОРА" : "МАРКЕРЫ ИНТЕРФЕЙСА"));
        addProbeList(rows, interfaceProbes, ToolTheme.Warning, search, true, 0d);
    }

    public static void BuildMemoryRows(
        List<FrameBreakdownRow> rows,
        IReadOnlyList<FrameProbe> memory,
        IReadOnlyList<FrameProbe> render,
        Action<List<FrameBreakdownRow>, FrameProbe> addCounter)
    {
        rows.Clear();
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ПАМЯТЬ"));
        foreach (FrameProbe probe in memory)
        {
            addCounter(rows, probe);
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "СЧЁТЧИКИ РЕНДЕРА"));
        foreach (FrameProbe probe in render)
        {
            addCounter(rows, probe);
        }
    }

    public static void BuildSceneRows(List<FrameBreakdownRow> rows, SceneCensus censusTracker)
    {
        rows.Clear();
        SceneCensus.Snapshot? census = censusTracker.Last;
        if (census == null)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Перепись идёт, пока открыта эта вкладка; первый снимок — через кадр."));
            return;
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ЖИВАЯ ИЕРАРХИЯ"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Объектов {census.Objects:N0}, активных {census.ActiveObjects:N0}; компонентов {census.Components:N0}"));
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Рендереров {census.Renderers:N0}, включённых {census.RenderersEnabled:N0}, видимых камерой {census.RenderersVisible:N0}"));
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, $"Глубина иерархии до {census.MaxDepth}: {census.DeepestPath}"));
        if (census.MissingScripts > 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Warning, $"Компонентов с потерянным скриптом: {census.MissingScripts}"));
        }

        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"Обход занял {census.WalkMilliseconds:F2} мс, раз в {SceneCensus.IntervalSeconds:F0} с — это цена замера, не кадра игры."));

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "СЦЕНЫ"));
        foreach (SceneCensus.SceneStat scene in census.Scenes)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{scene.Name}   корней {scene.Roots}, объектов {scene.Objects:N0}, активных {scene.ActiveObjects:N0}",
                census.Objects > 0 ? scene.Objects / (float)census.Objects : -1f));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "САМЫЕ КРУПНЫЕ КОРНИ"));
        foreach (SceneCensus.RootStat root in census.Roots)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{root.Scene} / {root.Name}{(root.Active ? string.Empty : "  (выключен)")}   объектов {root.Objects:N0}, активных {root.ActiveObjects:N0}",
                census.Objects > 0 ? root.Objects / (float)census.Objects : -1f));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "КАМЕРЫ"));
        if (census.Cameras.Count == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Warning, "Камер не найдено."));
        }

        foreach (string camera in census.Cameras)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, camera));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "КОМПОНЕНТЫ ПО ТИПАМ"));
        foreach ((string type, int total, int enabled) in census.ComponentTypes)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{type}   {total:N0}, включённых {enabled:N0}",
                census.Components > 0 ? total / (float)census.Components : -1f));
        }
    }
}
