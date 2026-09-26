#nullable enable

using System;
using Kern.World.Terrain;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

// Отладка террейна: по одному терму на вид.
//
// Световое окно отвечает на вопрос «сколько света пришло». На вопрос «почему
// пиксель тёмный» оно не отвечает: гасить может кайма рельефа, вырезанный
// силуэт, контактное затенение или не тот слой под клеткой. Здесь каждый вид
// показывает один терм или категорию, без обычного освещения и текстуры поверх.
public sealed class TerrainDebugWindow : ToolWindow
{
    private static readonly TerrainDebugView[] _views =
        (TerrainDebugView[])Enum.GetValues(typeof(TerrainDebugView));

    private readonly TerrainRenderer? _terrainRenderer;

    private Vector2 _scroll;

    public TerrainDebugWindow(TerrainRenderer? terrainRenderer)
        : base("Отладка террейна", new Rect(600f, 16f, 300f, 420f))
    {
        _terrainRenderer = terrainRenderer;

        // Открыто сразу, как окно кадровой статистики. Остальные окна
        // стартуют скрытыми и включаются галочкой на тулбаре; диагностика,
        // которую открывают, чтобы разобрать конкретный артефакт, так
        // теряется: её ищут глазами по кадру, а не по списку.
        Visible = true;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(290f, 300f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        TerrainDebugViewState.Reset();
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            TerrainDebugView active = TerrainDebugViewState.Active;

            if (active != TerrainDebugView.Off)
            {
                ToolChrome.Banner("КАДР ПОДМЕНЁН · ОТЛАДКА ТЕРРЕЙНА", ToolTheme.Error);
                GUILayout.Space(4f);
            }

            DrawViewRow(TerrainDebugView.Off, active);

            ToolChrome.SectionHeader("ГЕОМЕТРИЯ");
            bool surfaceSectionStarted = false;
            foreach (TerrainDebugView view in _views)
            {
                if (view == TerrainDebugView.Off)
                {
                    continue;
                }

                if (TerrainDebugViewState.IsSurfacePipelineView(view))
                {
                    if (!surfaceSectionStarted)
                    {
                        ToolChrome.SectionHeader("ПОВЕРХНОСТЬ И ЕЁ ЭТАПЫ");
                        surfaceSectionStarted = true;
                    }
                }

                DrawViewRow(view, active);
            }

            GUILayout.Space(6f);
            GUILayout.Label(TerrainDebugViewState.Legend(active), MutedLabelStyle);

            DrawGeometry();
        }
    }

    private static void DrawViewRow(TerrainDebugView view, TerrainDebugView active)
    {
        using (ToolLayout.Horizontal())
        {
            bool on = view == active;
            ToolChrome.StatusPip(on
                ? (view == TerrainDebugView.Off ? ToolTheme.Success : ToolTheme.Warning)
                : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            if (GUILayout.Toggle(on, TerrainDebugViewState.Describe(view), ToolTheme.SegmentedButton) && !on)
            {
                TerrainDebugViewState.Set(view);
            }
        }
    }

    // Пересборка мешей рядом с видами намеренно: отладочный вид показывает
    // то, что уже лежит в буферах клеток, и после правки данных его надо
    // перечитать, иначе смотришь на прошлый кадр и делаешь из него вывод.
    private void DrawGeometry()
    {
        if (_terrainRenderer == null)
        {
            return;
        }

        ToolChrome.SectionHeader("ГЕОМЕТРИЯ");
        if (GUILayout.Button("Пересобрать меши", SecondaryButtonStyle))
        {
            _terrainRenderer.ApplyClientConfig();
        }

        GUILayout.Label(
            "Данные клеток пересчитываются и уходят в буферы заново. " +
            "Нужно после смены настроек террейна: вид рисует то, что в " +
            "буферах уже лежит.",
            MutedLabelStyle);
    }
}
