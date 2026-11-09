#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

using Kern.Core.UserInterface;
using Kern.Rendering.Diagnostics.Tools;

namespace Kern.UI.Screens;

// Единственная точка, где документ интерфейса привязывается к экрану:
// масштаб панели, безопасная область и классы формы. Каждый экран вызывает
// Bind после того, как положил своё дерево в документ; остальное — здесь.
//
// Экран меняется посреди сессии: поворот на 180°, раскрытие раскладушки,
// переход на внешний экран флипа, сплит-окно. Панель опрашивает снимок
// экрана каждый свой кадр и при отличии переприменяет всё сразу — одна
// проверка вместо подписок в каждом экране.
public static class UIScreen
{
    private static readonly Dictionary<PanelSettings, float> s_userScale = new();
    private static readonly Dictionary<PanelSettings, PanelScaleMode> s_authoredScaleMode = new();
    private static readonly List<VisualElement> s_safeAreaRoots = new();
    private static readonly Dictionary<UIDocument, VisualElement> s_boundDocuments = new();
    private static readonly Dictionary<UIDocument, IPanel> s_documentPanels = new();
    private static readonly Dictionary<UIDocument, PanelSettings> s_documentSettings = new();
    private static readonly Dictionary<IPanel, IVisualElementScheduledItem> s_panelPolls = new();
    private static readonly List<VisualElement> s_visualTrees = new();

    private static float s_textScale = 1f;
    private static ScreenShape s_current;
    private static bool s_hasCurrent;
    private static ScreenShape? s_override;

    // Форма экрана изменилась. Подписчик обязан отписаться вместе со своим
    // деревом: событие статическое и переживает сцены.
    public static event Action<ScreenShape>? Changed;

    public static ScreenShape Current
    {
        get
        {
            if (!s_hasCurrent)
            {
                s_current = Capture();
                s_hasCurrent = true;
            }

            return s_current;
        }
    }

    public static ScreenShape Capture() => s_override ?? CaptureScreen();

    // Проверка раскладки на матрице устройств: экран подменяется снимком
    // нужного устройства, и всё остальное идёт штатным путём. null снимает
    // подмену.
    internal static void OverrideForTests(ScreenShape? shape)
    {
        s_override = shape;
        Reapply(Capture());
    }

    private static ScreenShape CaptureScreen() =>
        ScreenShapeRules.Resolve(
            new Vector2Int(Screen.width, Screen.height),
            Screen.dpi,
            Screen.safeArea,
            UIScalePolicy.UsesPhysicalScale,
            Touchscreen.current != null);

    // configuredScale — UIScale из конфига; null оставляет множитель, уже
    // выставленный этой панели (загрузочный экран конфига ещё не знает).
    public static void Bind(UIDocument document, float? configuredScale, float? textScale = null)
    {
        if (document == null)
        {
            return;
        }

        if (textScale.HasValue)
        {
            SetTextScale(textScale.Value);
        }

        ScreenShape shape = Current;

        // Документ без PanelSettings (собранный кодом в тестах) масштабировать
        // нечем; классы формы и безопасная область ему всё равно нужны.
        PanelSettings? settings = document.panelSettings;
        if (settings != null)
        {
            s_documentSettings[document] = settings;
            if (configuredScale.HasValue)
            {
                s_userScale[settings] = UIScalePolicy.ResolveEffectiveScale(configuredScale.Value);
            }

            ApplyScale(settings, shape);
            DynamicAtlasConfigurator.Apply(settings);
        }

        VisualElement root = document.rootVisualElement;
        s_boundDocuments[document] = root;

        // Микрографика макета (рамки, шкалы ступенями, пунктир, точки, штриховка) — векторно, по свойствам USS
        KernVector.Attach(root);
        if (!s_safeAreaRoots.Contains(root))
        {
            s_safeAreaRoots.Add(root);
            UISafeArea.Attach(root);
            root.RegisterCallback<PointerDownEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
        }

        IPanel? panel = root.panel;
        if (panel == null)
        {
            s_documentPanels.Remove(document);
            return;
        }

        s_documentPanels[document] = panel;

        VisualElement visualTree = panel.visualTree;
        if (!s_visualTrees.Contains(visualTree))
        {
            s_visualTrees.Add(visualTree);
            UILayoutTier.Attach(visualTree);
        }

        UILayoutTier.Apply(visualTree, shape);
        ApplyTextScale(visualTree);

        if (!s_panelPolls.ContainsKey(panel))
        {
            // Every(0) — каждый кадр панели. Опрос — пять полей Screen и
            // сравнение структур; запись стилей случается только при смене.
            s_panelPolls.Add(panel, visualTree.schedule.Execute(Poll).Every(0));
        }
    }

    // Scene-owned UIDocuments must release the panel and its visual tree when
    // their scene unloads. Keeping either in a static collection roots the
    // complete UI tree across every menu/game transition.
    public static void Unbind(UIDocument document)
    {
        if (!s_boundDocuments.Remove(document, out VisualElement root))
        {
            return;
        }

        IPanel? panel = s_documentPanels.Remove(document, out IPanel boundPanel) ? boundPanel : null;
        s_safeAreaRoots.Remove(root);
        UISafeArea.Detach(root);
        root.UnregisterCallback<PointerDownEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
        root.UnregisterCallback<PointerUpEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
        root.UnregisterCallback<ClickEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);

        if (panel != null && !HasBoundDocument(panel))
        {
            if (s_panelPolls.Remove(panel, out IVisualElementScheduledItem poll))
            {
                poll.Pause();
            }

            VisualElement visualTree = panel.visualTree;
            UILayoutTier.Detach(visualTree);
            s_visualTrees.Remove(visualTree);
        }

        PanelSettings? settings = s_documentSettings.Remove(document, out PanelSettings boundSettings)
            ? boundSettings
            : null;
        if (settings != null && !HasBoundDocument(settings))
        {
            s_userScale.Remove(settings);
            if (s_authoredScaleMode.Remove(settings, out PanelScaleMode authored))
            {
                settings.scaleMode = authored;
            }
        }
    }

    private static void BlockPointerBehindTools(EventBase evt)
    {
        Pointer? pointer = Pointer.current;
        if (ToolWindows.HasPointerCapture ||
            (pointer != null && ToolWindows.ContainsScreenPoint(pointer.position.ReadValue())))
        {
            evt.StopImmediatePropagation();
        }
    }

    private static bool HasBoundDocument(IPanel panel)
    {
        foreach (IPanel boundPanel in s_documentPanels.Values)
        {
            if (boundPanel == panel)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBoundDocument(PanelSettings settings)
    {
        foreach (PanelSettings boundSettings in s_documentSettings.Values)
        {
            if (boundSettings == settings)
            {
                return true;
            }
        }

        return false;
    }

    // Ползунок масштаба в настройках: новый множитель поверх текущей формы.
    public static void SetUserScale(UIDocument document, float scale)
    {
        PanelSettings? settings = document.panelSettings;
        if (settings == null)
        {
            return;
        }

        s_userScale[settings] = UIScalePolicy.Clamp(scale);
        ApplyScale(settings, Current);
    }

    public static void SetTextScale(float scale)
    {
        s_textScale = Mathf.Round(Mathf.Clamp(scale, 0.75f, 2f) * 20f) / 20f;
        foreach (VisualElement tree in s_visualTrees)
        {
            ApplyTextScale(tree);
        }
    }

    private static void ApplyTextScale(VisualElement tree)
    {
        string selected = "text-scale--" + Mathf.RoundToInt(s_textScale * 100f);
        if (tree.ClassListContains(selected))
        {
            return;
        }

        for (int percent = 75; percent <= 200; percent += 5)
        {
            tree.EnableInClassList("text-scale--" + percent, "text-scale--" + percent == selected);
        }
    }

    private static void ApplyScale(PanelSettings settings, in ScreenShape shape)
    {
        if (!s_userScale.TryGetValue(settings, out float multiplier))
        {
            multiplier = UIScalePolicy.RecommendedDefaultScale;
            s_userScale[settings] = multiplier;
        }

        float scale = multiplier;
        if (shape.UsesPhysicalScale)
        {
            // Пиксель панели = dp. Опорное разрешение ассета задумано под
            // монитор: на телефоне оно дало бы кнопку в 15 dp высотой.
            if (settings.scaleMode != PanelScaleMode.ConstantPixelSize)
            {
                s_authoredScaleMode.TryAdd(settings, settings.scaleMode);
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            }

            scale = ScreenShapeRules.PhysicalPanelScale(shape, multiplier);
        }
        else if (s_authoredScaleMode.Remove(settings, out PanelScaleMode authored))
        {
            // Экран снова не физический (подмена снята): ассет возвращается
            // к авторскому режиму — иначе он остался бы в пикселях устройства.
            settings.scaleMode = authored;
        }

        if (Mathf.Abs(settings.scale - scale) > 0.0001f)
        {
            settings.scale = scale;
        }
    }

    private static void Poll()
    {
        ScreenShape shape = Capture();
        if (s_hasCurrent && shape.SameAs(s_current))
        {
            return;
        }

        Reapply(shape);
    }

    private static void Reapply(ScreenShape shape)
    {
        s_current = shape;
        s_hasCurrent = true;

        foreach (PanelSettings settings in s_userScale.Keys)
        {
            if (settings != null)
            {
                ApplyScale(settings, shape);
            }
        }

        s_visualTrees.RemoveAll(tree => tree.panel == null);
        foreach (VisualElement tree in s_safeAreaRoots)
        {
            UILayoutTier.Apply(tree, shape);
        }

        s_safeAreaRoots.RemoveAll(root => root.panel == null);
        foreach (VisualElement root in s_safeAreaRoots)
        {
            UISafeArea.Apply(root);
        }

        Changed?.Invoke(shape);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        foreach (VisualElement tree in s_visualTrees)
        {
            tree.UnregisterCallback<PointerDownEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
            tree.UnregisterCallback<PointerUpEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
            tree.UnregisterCallback<ClickEvent>(BlockPointerBehindTools, TrickleDown.TrickleDown);
        }

        s_userScale.Clear();
        s_authoredScaleMode.Clear();
        s_safeAreaRoots.Clear();
        foreach (IVisualElementScheduledItem poll in s_panelPolls.Values)
        {
            poll.Pause();
        }

        s_boundDocuments.Clear();
        s_documentPanels.Clear();
        s_documentSettings.Clear();
        s_panelPolls.Clear();
        s_visualTrees.Clear();
        s_textScale = 1f;
        s_hasCurrent = false;
        s_override = null;
        Changed = null;
    }
}
