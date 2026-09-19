#nullable enable

using System;
using Unity.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui;

public abstract class ToolWindow : IDisposable
{
    public const float CollapsedHeight = ToolTheme.HeaderHeight + 4f;

    private readonly Rect _initialRect;
    private bool _initialStateCaptured;
    private bool _initialVisible;
    private bool _visible;
    private string? _drawError;
    private string? _pendingDrawError;
    private bool _retryRequested;
    private Vector2? _pendingSize;
    private bool _collapsed;
    private float _expandedHeight;

    protected ToolWindow(string title, Rect initialRect)
    {
        Title = title;
        DisplayTitle = title.ToUpperInvariant();
        Rect = initialRect;
        _initialRect = initialRect;
        DrawFunction = DrawWindow;
    }

    // Один делегат на окно. GUI.Window(..., window.DrawWindow, ...) создавал
    // новый на каждое окно в каждом событии IMGUI.
    internal GUI.WindowFunction DrawFunction { get; }

    public string Title { get; }

    public string DisplayTitle { get; }

    public Rect Rect;

    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            if (value)
            {
                _expandedHeight = Rect.height;
            }

            _collapsed = value;
            _pendingSize = new Vector2(
                Rect.width,
                value ? CollapsedHeight : Mathf.Max(MinimumSize.y, _expandedHeight));
            ToolWindows.NotifyLayoutChanged();
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible != value)
            {
                _visible = value;
                OnVisibilityChanged(value);
            }
        }
    }

    internal int ID { get; set; }

    public virtual bool WantsSampling => Visible;

    public virtual Vector2 MinimumSize => new(240f, 150f);

    protected virtual bool CanClose => true;

    protected virtual bool CanResize => true;

    public bool CanRestoreVisibility => CanClose;

    protected static GUIStyle SectionLabelStyle => ToolTheme.SectionLabel;

    protected static GUIStyle RichLabelStyle => ToolTheme.RichLabel;

    protected static GUIStyle WrappedLabelStyle => ToolTheme.WrappedLabel;

    protected static GUIStyle MutedLabelStyle => ToolTheme.MutedLabel;

    protected static GUIStyle MetricLabelStyle => ToolTheme.MetricLabel;

    protected static GUIStyle ActiveButtonStyle => ToolTheme.ActiveButton;

    protected static GUIStyle SecondaryButtonStyle => ToolTheme.SecondaryButton;

    protected static GUIStyle DangerButtonStyle => ToolTheme.DangerButton;

    protected static GUIStyle SegmentedButtonStyle => ToolTheme.SegmentedButton;

    protected static GUIStyle CardStyle => ToolTheme.Card;

    public virtual void Tick()
    {
    }

    public void ResetPosition()
    {
        Rect = _initialRect;
        _collapsed = false;
        _expandedHeight = 0f;
        _pendingSize = null;
    }

    internal void CaptureInitialState()
    {
        if (_initialStateCaptured)
        {
            return;
        }

        _initialStateCaptured = true;
        _initialVisible = Visible;
    }

    internal void ResetForPlaySession()
    {
        Rect = _initialRect;
        Visible = _initialVisible;
        _drawError = null;
        _pendingDrawError = null;
        _retryRequested = false;
        _pendingSize = null;
        _collapsed = false;
        _expandedHeight = 0f;
        OnPlaySessionReset();
    }

    protected virtual void OnPlaySessionReset()
    {
    }

    protected virtual void OnVisibilityChanged(bool visible)
    {
    }

    public void Dispose()
    {
        OnDispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void OnDispose()
    {
    }

    protected abstract void DrawContent();

    internal Rect ApplyPendingSize(Rect drawnRect)
    {
        if (!_pendingSize.HasValue)
        {
            return drawnRect;
        }

        drawnRect.size = _pendingSize.Value;
        _pendingSize = null;
        return drawnRect;
    }

    // Стоимость окна за последний кадр, в котором оно рисовалось: сумма по всем
    // событиям IMGUI. Секундомер, а не маркер профайлера — число не смешано с
    // окнами редактора и есть в сборке без ENABLE_PROFILER.
    public double DrawMilliseconds { get; private set; }

    public int DrawEvents { get; private set; }

    private ProfilerMarker _drawMarker;
    private bool _drawMarkerCreated;
    private long _costTicks;
    private int _costEvents;
    private int _costFrame = -1;

    internal void DrawWindow(int id)
    {
        if (!_drawMarkerCreated)
        {
            _drawMarkerCreated = true;
            _drawMarker = new ProfilerMarker(ProfilerCategory.Gui, "Kern.Tools." + Title);
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        long allocatedBefore = AllocatedNow();
        _drawMarker.Begin();
        try
        {
            DrawWindowCore(id);
        }
        finally
        {
            _drawMarker.End();
            RollCostFrame();
            _costTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _costEvents++;
            _drawAllocatedBytes += System.Math.Max(0L, AllocatedNow() - allocatedBefore);
        }
    }

    // Мусор окна за последний кадр: отдельно Tick и отрисовка. Без этого
    // счётчик «мусор за кадр» смешивал игру с самими инструментами, а все
    // отчёты сняты с открытыми окнами.
    public long TickAllocatedBytes { get; private set; }

    public long DrawAllocatedBytes { get; private set; }

    private long _tickAllocatedBytes;
    private long _drawAllocatedBytes;

    // GC.GetAllocatedBytesForCurrentThread под Boehm всегда 0 — первый отчёт
    // показал 0.0 КБ у всех окон. Занятая управляемая куча растёт на каждую
    // аллокацию до сборки; отрицательная разница (сборка внутри замера)
    // отбрасывается.
    private static long AllocatedNow() => UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();

    internal void TickMeasured()
    {
        long allocatedBefore = AllocatedNow();
        try
        {
            Tick();
        }
        finally
        {
            RollCostFrame();
            _tickAllocatedBytes += System.Math.Max(0L, AllocatedNow() - allocatedBefore);
        }
    }

    private void RollCostFrame()
    {
        int frame = Time.frameCount;
        if (frame == _costFrame)
        {
            return;
        }

        if (_costFrame >= 0)
        {
            DrawMilliseconds = _costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            DrawEvents = _costEvents;
            TickAllocatedBytes = _tickAllocatedBytes;
            DrawAllocatedBytes = _drawAllocatedBytes;
        }

        _costFrame = frame;
        _costTicks = 0;
        _costEvents = 0;
        _tickAllocatedBytes = 0;
        _drawAllocatedBytes = 0;
    }

    private void DrawWindowCore(int id)
    {
        if (_retryRequested && Event.current.type == EventType.Layout)
        {
            _retryRequested = false;
            _drawError = null;
            _pendingDrawError = null;
        }

        if (_pendingDrawError != null && Event.current.type == EventType.Layout)
        {
            _drawError = _pendingDrawError;
            _pendingDrawError = null;
        }

        if (Event.current.type == EventType.MouseDown &&
            new Rect(0f, 0f, Rect.width, Rect.height).Contains(Event.current.mousePosition))
        {
            GUI.BringWindowToFront(id);
            ToolWindows.NotifyWindowFocused(this);
        }

        bool focused = ToolWindows.IsFocused(this);
        var local = new Rect(0f, 0f, Rect.width, Rect.height);
        ToolChrome.DrawHeaderMarker(ToolTheme.HeaderHeight, focused);
        ToolChrome.DrawHeaderRule(Rect.width, ToolTheme.HeaderHeight, focused);
        ToolChrome.DrawCornerBrackets(local, focused);

        // Кнопка закрытия отодвинута от правого края на ширину среза: на самом
        // углу рамки её нет, и кнопка висела бы в пустоте.
        float closeX = Rect.width - 32f;
        float collapseX = CanClose ? closeX - 26f : closeX;
        GUI.Label(
            new Rect(13f, 0f, Mathf.Max(0f, collapseX - 19f), ToolTheme.HeaderHeight),
            DisplayTitle,
            ToolTheme.WindowTitle);
        if (CanClose && GUI.Button(
                new Rect(closeX, 5f, 24f, 20f),
                "×",
                ToolTheme.CloseButton))
        {
            ToolWindows.RequestVisibility(this, visible: false);
        }

        if (GUI.Button(
                new Rect(collapseX, 5f, 24f, 20f),
                Collapsed ? "+" : "−",
                ToolTheme.CloseButton))
        {
            Collapsed = !Collapsed;
        }

        if (Collapsed)
        {
            // Свёрнутое окно не рисует содержимое и не растягивается, но ручку
            // перетаскивания сохраняет: полоса заголовка — это всё, что от
            // него осталось, и она обязана остаться подвижной.
            GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
            return;
        }

        if (_drawError != null)
        {
            GUILayout.Space(2f);
            GUILayout.Label(
                "Окно не смогло отрисоваться. Остальные инструменты продолжают работать.",
                ToolTheme.ErrorLabel);
            GUILayout.TextArea(_drawError, GUILayout.MinHeight(60f));
            if (GUILayout.Button("Повторить", ActiveButtonStyle))
            {
                _retryRequested = true;
            }

            DrawResizeGrip();
            GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
            return;
        }

        Color previousColor = GUI.color;
        Color previousBackgroundColor = GUI.backgroundColor;
        Color previousContentColor = GUI.contentColor;
        bool previousEnabled = GUI.enabled;
        int previousDepth = GUI.depth;
        Matrix4x4 previousMatrix = GUI.matrix;
        try
        {
            DrawContent();
        }
        catch (ExitGUIException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (_pendingDrawError == null)
            {
                _pendingDrawError = $"{exception.GetType().Name}: {exception.Message}";
                Debug.LogException(exception);
            }

            // После исключения GUILayout-кэш текущего события уже неполон.
            // Продолжать Repaint с ним нельзя: ошибка одного окна породит
            // вторичную ArgumentException про несовпавшее число контролов и
            // визуально уронит весь реестр. ExitGUI отдаёт Unity управление и
            // следующий Layout строит безопасный экран ошибки с нуля.
            GUIUtility.ExitGUI();
        }
        finally
        {
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackgroundColor;
            GUI.contentColor = previousContentColor;
            GUI.enabled = previousEnabled;
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
        }

        DrawResizeGrip();

        // Ручка — только полоса заголовка. Перетаскивание за содержимое
        // означало бы, что окно уезжает при каждом промахе мимо ползунка.
        GUI.DragWindow(new Rect(0f, 0f, Rect.width, ToolTheme.HeaderHeight));
    }

    private void DrawResizeGrip()
    {
        if (!CanResize)
        {
            return;
        }

        const float gripSize = 18f;
        Rect grip = new(Rect.width - gripSize, Rect.height - gripSize, gripSize, gripSize);
        int controlID = GUIUtility.GetControlID(ID ^ 0x5E51, FocusType.Passive);
        Event currentEvent = Event.current;
        switch (currentEvent.GetTypeForControl(controlID))
        {
            case EventType.MouseDown:
                if (currentEvent.button == 0 && grip.Contains(currentEvent.mousePosition))
                {
                    GUIUtility.hotControl = controlID;
                    currentEvent.Use();
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlID)
                {
                    Vector2 size = _pendingSize ?? Rect.size;
                    size.x = Mathf.Max(MinimumSize.x, size.x + currentEvent.delta.x);
                    size.y = Mathf.Max(MinimumSize.y, size.y + currentEvent.delta.y);
                    _pendingSize = size;
                    currentEvent.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlID)
                {
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                }

                break;

            case EventType.Repaint:
                DrawResizeGlyph(grip, GUIUtility.hotControl == controlID);
                break;

            default:
                break;
        }
    }

    private static void DrawResizeGlyph(Rect grip, bool active)
    {
        Color previousColor = GUI.color;
        Texture2D pixel = ToolPalette.White;

        // Три диагональных штриха, а не сплошной уголок: уголок здесь уже есть —
        // его рисует обвязка, — и второй такой же читался бы как сбой рамки.
        for (int i = 0; i < 3; i++)
        {
            float offset = 4f + i * 3f;
            GUI.color = active
                ? ToolPalette.Accent
                : ToolPalette.Fade(ToolPalette.Accent, 0.70f - i * 0.18f);
            GUI.DrawTexture(new Rect(grip.xMax - offset - 1f, grip.yMax - offset, 3f, 2f), pixel);
        }

        GUI.color = previousColor;
    }
}
