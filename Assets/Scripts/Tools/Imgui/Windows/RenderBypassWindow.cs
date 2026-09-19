#nullable enable

using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting;
using Kern.Game;
using Kern.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tools.Imgui.Windows;

public sealed class RenderBypassWindow : ToolWindow
{
    private readonly IRuntimeDebugSettings _debugSettings;
    private readonly LightingEngine? _lighting;
    private readonly WorldGizmoOptions _gizmos;
    private readonly SurfaceRenderer? _surfaceRenderer;
    private readonly WorldEntityBatchRenderer? _entityRenderer;
    private readonly UIDocument? _gameUIDocument;
    private bool _hideSurface;
    private bool _hideEntities;
    private bool _hideGameUI;
    private Vector2 _scroll;
    private int _bypassBannerCount = -1;
    private string _bypassBanner = string.Empty;
    private LightingEngine.DebugView? _lightingViewLabelValue;
    private string _lightingViewLabel = string.Empty;

    public RenderBypassWindow(
        IRuntimeDebugSettings debugSettings,
        LightingEngine? lighting,
        WorldGizmoOptions gizmos,
        SurfaceRenderer? surfaceRenderer = null,
        WorldEntityBatchRenderer? entityRenderer = null,
        UIDocument? gameUIDocument = null)
        : base("Диагностика рендера", new Rect(16f, 382f, 260f, 390f))
    {
        _debugSettings = debugSettings;
        _lighting = lighting;
        _gizmos = gizmos;
        _surfaceRenderer = surfaceRenderer;
        _entityRenderer = entityRenderer;
        _gameUIDocument = gameUIDocument;
    }

    public override bool WantsSampling => false;

    public override Vector2 MinimumSize => new(250f, 330f);

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _debugSettings.BypassLightingCompute = false;
        _debugSettings.BypassTerrainDraw = false;
        _debugSettings.BypassCpuMeshRebuild = false;
        _debugSettings.ShowRobotDebugVisuals = false;
        PostProcessRuntimeState.SkipPasses = false;
        _hideSurface = false;
        _hideEntities = false;
        _hideGameUI = false;
        _lighting?.SetDebugView(LightingEngine.DebugView.FinalLighting);
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            DrawBypassWarning();

            ToolChrome.SectionHeader("ОБХОДЫ");
            GUILayout.Label("Активный пункт отключает соответствующий этап.", MutedLabelStyle);
            _debugSettings.BypassLightingCompute = DrawSwitch(
                _debugSettings.BypassLightingCompute, "Расчёт освещения");
            _debugSettings.BypassTerrainDraw = DrawSwitch(
                _debugSettings.BypassTerrainDraw, "Отрисовка террейна");
            _debugSettings.BypassCpuMeshRebuild = DrawSwitch(
                _debugSettings.BypassCpuMeshRebuild, "Пересборка меша");
            PostProcessRuntimeState.SkipPasses = DrawSwitch(
                PostProcessRuntimeState.SkipPasses, "Проходы постпроцесса");
            _hideSurface = DrawSwitch(_hideSurface, "Поверхность");
            _hideEntities = DrawSwitch(_hideEntities, "Сущности мира");
            _hideGameUI = DrawSwitch(_hideGameUI, "Интерфейс игры");
            ApplyVisibility();
            _debugSettings.ShowRobotDebugVisuals = DrawSwitch(
                _debugSettings.ShowRobotDebugVisuals, "Отладка роботов", ToolTheme.FrameGraphColor);

            ToolChrome.SectionHeader("ГИЗМО В МИРЕ");
            _gizmos.ShowGrid = DrawSwitch(_gizmos.ShowGrid, "Сетка чанков", ToolTheme.FrameGraphColor);
            _gizmos.ShowCursor = DrawSwitch(_gizmos.ShowCursor, "Курсор клетки", ToolTheme.FrameGraphColor);

            if (_lighting == null)
            {
                return;
            }

            ToolChrome.SectionHeader("ДИНАМИЧЕСКИЙ СВЕТ");
            bool lit = _lighting.DynamicLightIntensity > 0.01f;
            if (DrawSwitch(lit, "Динамический свет", ToolTheme.Success) != lit)
            {
                ToggleDynamicLight();
            }

            DrawLightingViewPicker(_lighting);
        }
    }

    // A/B для замера цены слоя: рендереры включаются и выключаются каждый
    // кадр отрисовки окна, поэтому пересозданные объекты слоя тоже скрываются.
    private void ApplyVisibility()
    {
        SetRenderersEnabled(_surfaceRenderer, !_hideSurface);
        SetRenderersEnabled(_entityRenderer, !_hideEntities);
        if (_gameUIDocument != null && _gameUIDocument.rootVisualElement != null)
        {
            DisplayStyle display = _hideGameUI ? DisplayStyle.None : DisplayStyle.Flex;
            if (_gameUIDocument.rootVisualElement.style.display != display)
            {
                _gameUIDocument.rootVisualElement.style.display = display;
            }
        }
    }

    private static void SetRenderersEnabled(Component? owner, bool enabled)
    {
        if (owner == null)
        {
            return;
        }

        foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer.enabled != enabled)
            {
                renderer.enabled = enabled;
            }
        }
    }

    private void DrawBypassWarning()
    {
        int active = 0;
        if (_debugSettings.BypassLightingCompute)
        {
            active++;
        }

        if (_debugSettings.BypassTerrainDraw)
        {
            active++;
        }

        if (_debugSettings.BypassCpuMeshRebuild)
        {
            active++;
        }

        if (PostProcessRuntimeState.SkipPasses)
        {
            active++;
        }

        active += (_hideSurface ? 1 : 0) + (_hideEntities ? 1 : 0) + (_hideGameUI ? 1 : 0);

        if (active == 0)
        {
            return;
        }

        if (_bypassBannerCount != active)
        {
            _bypassBannerCount = active;
            _bypassBanner = $"КАДР НЕПОЛНЫЙ · ОБХОДОВ: {active}";
        }

        ToolChrome.Banner(_bypassBanner, ToolTheme.Error);
        GUILayout.Space(4f);
    }


    private void DrawLightingViewPicker(LightingEngine lighting)
    {
        ToolChrome.SectionHeader("ВИД ОСВЕЩЕНИЯ");
        bool custom = lighting.ActiveDebugView != LightingEngine.DebugView.FinalLighting;

        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(custom ? ToolTheme.Warning : ToolTheme.Success);
            LightingEngine.DebugView view = lighting.ActiveDebugView;
            if (_lightingViewLabelValue != view)
            {
                _lightingViewLabelValue = view;
                _lightingViewLabel = view.ToString();
            }

            GUILayout.Label(_lightingViewLabel, MutedLabelStyle);
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("◄", SecondaryButtonStyle, ToolLayout.Width(34f)))
            {
                StepLightingView(lighting, -1);
            }

            if (GUILayout.Button("Следующий вид", ActiveButtonStyle))
            {
                StepLightingView(lighting, 1);
            }
        }

        bool controlsEnabled = GUI.enabled;
        GUI.enabled = controlsEnabled && custom;
        if (GUILayout.Button("Вернуть обычный", SecondaryButtonStyle))
        {
            lighting.SetDebugView(LightingEngine.DebugView.FinalLighting);
        }

        GUI.enabled = controlsEnabled;
    }

    private static bool DrawSwitch(bool value, string label, Color? activeColor = null)
    {
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(value
                ? activeColor ?? ToolTheme.Error
                : ToolPalette.Fade(ToolPalette.MutedText, 0.45f));
            return GUILayout.Toggle(value, label, ToolTheme.SegmentedButton);
        }
    }

    private static void StepLightingView(LightingEngine lighting, int step)
    {
        int total = System.Enum.GetValues(typeof(LightingEngine.DebugView)).Length;

        // Плюс длина перед остатком: в C# остаток отрицательного числа
        // отрицателен, и шаг назад с нулевого вида дал бы недопустимый вид.
        int next = ((int)lighting.ActiveDebugView + step + total) % total;
        lighting.SetDebugView((LightingEngine.DebugView)next);
    }

    public void ToggleDynamicLight()
    {
        if (_lighting == null)
        {
            return;
        }

        // DynamicLightIntensity — константа, не настраивается
    }
}
