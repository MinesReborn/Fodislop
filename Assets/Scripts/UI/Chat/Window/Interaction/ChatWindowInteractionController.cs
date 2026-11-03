#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Input;

namespace Kern.UI.Chat;

// Перемещение и растягивание окна глобального чата с запоминанием геометрии.
//
// Механика — существующие паттерны проекта, без новых абстракций:
// драг как в PlayerHUDView (pointer capture, дельта от точки захвата, inline
// left/top, кламп в panel.visualTree.layout), ресайз через C#-создаваемые
// зоны захвата, но зон восемь: четыре грани и четыре угла, с browser-моделью
// якорей (тянешь грань — противоположная стоит на месте).
//
// Пока игрок ни разу не двигал окно (и в конфиге нет сохранённой
// геометрии), его позицию задаёт Chat.uss (привязки left+right+bottom).
// Восстановление из конфига или первое же перетаскивание/растягивание
// пинит бокс целиком в inline-стили (left/top/width/height + right/bottom
// в Auto) — с этой минуты панель ведётся только отсюда, и геометрия
// переживает перезапуск клиента через InterfaceSettings (как позиция
// панели HUD).
internal sealed class ChatWindowInteractionController
{
    internal const float MinWidth = 280f;
    internal const float MinHeight = 120f;

    private const float DragStripHeight = 22f;
    private const float EdgeThickness = 5f;
    private const float CornerSize = 12f;

    private readonly VisualElement _treeRoot;
    private readonly VisualElement _panel;
    private readonly IClientConfigManager _config;

    // Актуальный бокс панели в координатах родителя. resolvedStyle читать
    // сразу после записи inline-стилей нельзя - он обновляется только на
    // раскладке, поэтому во время взаимодействия геометрию ведём здесь.
    private Rect _box;

    // Геометрия переведена с USS-привязок на inline (перетаскиванием,
    // ресайзом или восстановлением из конфига). До этого окно стоит по
    // стилям и само подстраивается под экран - клампить нечего.
    private bool _pinned;

    // Бокс восстановлен из конфига и игрок его ещё не трогал. В этом
    // состоянии каждая смена экрана пересчитывает бокс из конфига заново,
    // пока первое перетаскивание/ресайз не отдаст геометрию игроку.
    private bool _restored;

    private bool _dragging;
    private int _dragPointerId = -1;
    private Vector2 _pointerStart;
    private Vector2 _panelStart;

    private bool _resizing;
    private int _resizePointerId = -1;
    private ChatResizeCursor.BoxEdge _resizeEdge;
    private Rect _resizeStartBox;

    public ChatWindowInteractionController(VisualElement treeRoot, VisualElement panel, IClientConfigManager config)
    {
        _treeRoot = treeRoot;
        _panel = panel;
        _config = config;

        BuildDragStrip();
        BuildResizeZones();

        // Подписка на корень дерева, а не на панель: панель стартует скрытой
        // (display: none) и GeometryChangedEvent на ней не придёт до первого
        // показа; корень растянут на весь экран всегда. Инлайн-геометрия
        // спокойно применяется и до показа - панель появится сразу на месте.
        if (_config.Config.Interface.HasChatWindowLayout)
        {
            _treeRoot.RegisterCallback<GeometryChangedEvent>(OnTreeGeometryChanged);
        }
    }

    // Скрытие окна (TAB): снимаем курсор, если он висел над зоной, -
    // PointerLeave при display: none может не прийти.
    public void OnPanelHidden() => ResizeCursors.Reset();

    public void Dispose()
    {
        _treeRoot.UnregisterCallback<GeometryChangedEvent>(OnTreeGeometryChanged);
        ResizeCursors.UnbindAll();
    }

    // ─── Построение зон захвата ───

    private void BuildDragStrip()
    {
        var strip = new VisualElement { name = "ChatDragStrip" };
        strip.AddToClassList("gchat-drag-strip");
        strip.style.height = DragStripHeight;

        strip.RegisterCallback<PointerDownEvent>(OnDragStripPointerDown);
        strip.RegisterCallback<PointerMoveEvent>(OnDragStripPointerMove);
        strip.RegisterCallback<PointerUpEvent>(OnDragStripPointerUp);
        strip.RegisterCallback<PointerCaptureOutEvent>(OnDragCaptureLost);
        ResizeCursors.Bind(strip, ResizeCursors.Shape.MoveAll);

        _panel.Add(strip);
    }

    private void BuildResizeZones()
    {
        AddZone(ChatResizeCursor.BoxEdge.North, "gchat-resize-edge--n",
            top: 0f, left: CornerSize, right: CornerSize, height: EdgeThickness);
        AddZone(ChatResizeCursor.BoxEdge.South, "gchat-resize-edge--s",
            bottom: 0f, left: CornerSize, right: CornerSize, height: EdgeThickness);
        AddZone(ChatResizeCursor.BoxEdge.West, "gchat-resize-edge--w",
            top: CornerSize, bottom: CornerSize, left: 0f, width: EdgeThickness);
        AddZone(ChatResizeCursor.BoxEdge.East, "gchat-resize-edge--e",
            top: CornerSize, bottom: CornerSize, right: 0f, width: EdgeThickness);

        AddZone(ChatResizeCursor.BoxEdge.Northwest, "gchat-resize-corner--nw", top: 0f, left: 0f,
            width: CornerSize, height: CornerSize);
        AddZone(ChatResizeCursor.BoxEdge.Northeast, "gchat-resize-corner--ne", top: 0f, right: 0f,
            width: CornerSize, height: CornerSize);
        AddZone(ChatResizeCursor.BoxEdge.Southwest, "gchat-resize-corner--sw", bottom: 0f, left: 0f,
            width: CornerSize, height: CornerSize);
        AddZone(ChatResizeCursor.BoxEdge.Southeast, "gchat-resize-corner--se", bottom: 0f, right: 0f,
            width: CornerSize, height: CornerSize);
    }

    private void AddZone(
        ChatResizeCursor.BoxEdge edge,
        string className,
        float top = -1f,
        float bottom = -1f,
        float left = -1f,
        float right = -1f,
        float width = -1f,
        float height = -1f)
    {
        var zone = new VisualElement { name = "ChatResizeZone" + edge };
        zone.AddToClassList("gchat-resize-zone");
        zone.AddToClassList(className);

        // -1 значит «не задаём»: абсолютному элементу с заданными left+right
        // ширина растягивается сама.
        if (top >= 0f)
        {
            zone.style.top = top;
        }

        if (bottom >= 0f)
        {
            zone.style.bottom = bottom;
        }

        if (left >= 0f)
        {
            zone.style.left = left;
        }

        if (right >= 0f)
        {
            zone.style.right = right;
        }

        if (width >= 0f)
        {
            zone.style.width = width;
        }

        if (height >= 0f)
        {
            zone.style.height = height;
        }

        zone.RegisterCallback<PointerDownEvent>(evt => OnResizePointerDown(edge, evt));
        zone.RegisterCallback<PointerMoveEvent>(OnResizePointerMove);
        zone.RegisterCallback<PointerUpEvent>(OnResizePointerUp);
        zone.RegisterCallback<PointerCaptureOutEvent>(OnResizeCaptureLost);
        ResizeCursors.Bind(zone, ChatResizeCursor.CursorShape(edge));

        _panel.Add(zone);
    }

    // ─── Восстановление геометрии и кламп в экран ───

    // Единый обработчик смены геометрии корня: применение UI-масштаба
    // (panelSettings.scale), смена разрешения, тир панели. Стартовые события
    // приходят ДО того, как стартовый конвейер применит сохранённый
    // UI-масштаб, а кламп «залипает»: однажды сдвинутый бокс он уже не
    // возвращает. Поэтому пока окно не тронуто игроком, бокс на каждой
    // смене экрана пересчитывается из конфига заново; после первого
    // перетаскивания/ресайза остаётся только кламп в границы экрана.
    private void OnTreeGeometryChanged(GeometryChangedEvent evt)
    {
        if (_panel.panel == null || _dragging || _resizing)
        {
            return;
        }

        Rect screen = _panel.panel.visualTree.layout;
        if (screen.width <= 0f || screen.height <= 0f)
        {
            return;
        }

        if (!_pinned)
        {
            ApplySavedLayout(screen);
        }
        else if (_restored)
        {
            ReapplySavedLayout(screen);
        }
        else
        {
            ClampBoxToScreen(screen);
        }
    }

    // Вписывает сохранённый бокс в текущий логический экран: с прошлого
    // запуска могли смениться разрешение или UI-масштаб.
    private Rect FitSavedBox(Rect screen)
    {
        Kern.Core.InterfaceSettings saved = _config.Config.Interface;
        float width = Mathf.Clamp(saved.ChatWindowWidth, MinWidth, Mathf.Max(MinWidth, screen.width));
        float height = Mathf.Clamp(saved.ChatWindowHeight, MinHeight, Mathf.Max(MinHeight, screen.height));
        float x = Mathf.Clamp(saved.ChatWindowX, 0f, Mathf.Max(0f, screen.width - width));
        float y = Mathf.Clamp(saved.ChatWindowY, 0f, Mathf.Max(0f, screen.height - height));
        return Rect.MinMaxRect(x, y, x + width, y + height);
    }

    private void WriteBoxStyles()
    {
        _panel.style.left = _box.x;
        _panel.style.top = _box.y;
        _panel.style.width = _box.width;
        _panel.style.height = _box.height;
    }

    // Первое применение сохранённой геометрии: inline-бокс + Auto на
    // right/bottom - дальше смены экрана ведёт обработчик выше.
    private void ApplySavedLayout(Rect screen)
    {
        Kern.Core.InterfaceSettings saved = _config.Config.Interface;

        // Мусор в сохранении (NaN/бесконечность) - сбрасываем: окно вернётся
        // к геометрии из стилей, подписка на смены экрана больше не нужна.
        if (float.IsNaN(saved.ChatWindowX) || float.IsNaN(saved.ChatWindowY) ||
            float.IsNaN(saved.ChatWindowWidth) || float.IsNaN(saved.ChatWindowHeight) ||
            float.IsInfinity(saved.ChatWindowX) || float.IsInfinity(saved.ChatWindowY) ||
            float.IsInfinity(saved.ChatWindowWidth) || float.IsInfinity(saved.ChatWindowHeight))
        {
            _treeRoot.UnregisterCallback<GeometryChangedEvent>(OnTreeGeometryChanged);
            _config.UpdateSection(
                config => config.Interface,
                settings => settings.HasChatWindowLayout = false);
            return;
        }

        _box = FitSavedBox(screen);
        WriteBoxStyles();
        _panel.style.right = new StyleLength(StyleKeyword.Auto);
        _panel.style.bottom = new StyleLength(StyleKeyword.Auto);
        _pinned = true;
        _restored = true;
    }

    // Пересчёт восстановленного бокса из конфига на каждую смену экрана:
    // до первого вмешательства игрока окно обязано стоять ровно там, куда
    // его поставили в прошлый раз, на каком бы экране оно ни восстанавливалось.
    private void ReapplySavedLayout(Rect screen)
    {
        Rect fitted = FitSavedBox(screen);
        if (Mathf.Approximately(fitted.x, _box.x) && Mathf.Approximately(fitted.y, _box.y) &&
            Mathf.Approximately(fitted.width, _box.width) && Mathf.Approximately(fitted.height, _box.height))
        {
            return;
        }

        _box = fitted;
        WriteBoxStyles();
    }

    // Ручной бокс (игрок уже двигал окно): только удерживаем в границах.
    private void ClampBoxToScreen(Rect screen)
    {
        float width = Mathf.Clamp(_box.width, MinWidth, Mathf.Max(MinWidth, screen.width));
        float height = Mathf.Clamp(_box.height, MinHeight, Mathf.Max(MinHeight, screen.height));
        float x = Mathf.Clamp(_box.x, 0f, Mathf.Max(0f, screen.width - width));
        float y = Mathf.Clamp(_box.y, 0f, Mathf.Max(0f, screen.height - height));

        // Раскладка корня меняется часто (тиры, панель) - пишем стили только
        // когда бокс действительно вышел за границы.
        if (Mathf.Approximately(x, _box.x) && Mathf.Approximately(y, _box.y) &&
            Mathf.Approximately(width, _box.width) && Mathf.Approximately(height, _box.height))
        {
            return;
        }

        _box = Rect.MinMaxRect(x, y, x + width, y + height);
        WriteBoxStyles();
    }

    // layout - посчитанные значения текущей раскладки: для панели на
    // привязках right/bottom это валидные left/top/width/height.
    private bool PinBoxFromStyles()
    {
        Rect layout = _panel.layout;
        if (layout.width <= 0f || layout.height <= 0f)
        {
            return false;
        }

        // Игрок взял окно в руки: дальше бокс живёт только здесь, режим
        // «пересчитывать из конфига на смене экрана» выключается.
        _restored = false;
        _box = layout;
        WriteBoxStyles();

        // Inline Auto перебивает USS-привязки right: 160px / bottom: 8px,
        // иначе панель продолжала бы растягиваться между четырьмя якорями.
        _panel.style.right = new StyleLength(StyleKeyword.Auto);
        _panel.style.bottom = new StyleLength(StyleKeyword.Auto);
        _pinned = true;
        return true;
    }

    // ─── Перемещение за верхнюю полосу ───

    private void OnDragStripPointerDown(PointerDownEvent evt)
    {
        if (_dragging || _resizing || evt.button != 0 || _panel.panel == null || !PinBoxFromStyles())
        {
            return;
        }

        _dragPointerId = evt.pointerId;
        _pointerStart = new Vector2(evt.position.x, evt.position.y);
        _panelStart = new Vector2(_box.x, _box.y);
        _dragging = true;
        evt.StopPropagation();
        ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
    }

    private void OnDragStripPointerMove(PointerMoveEvent evt)
    {
        if (!_dragging || evt.pointerId != _dragPointerId || _panel.panel == null)
        {
            return;
        }

        Vector2 next = _panelStart + (new Vector2(evt.position.x, evt.position.y) - _pointerStart);

        // Окно не выходит за пределы экрана (размер экрана - корень панели).
        Rect screen = _panel.panel.visualTree.layout;
        next.x = Mathf.Clamp(next.x, 0f, Mathf.Max(0f, screen.width - _box.width));
        next.y = Mathf.Clamp(next.y, 0f, Mathf.Max(0f, screen.height - _box.height));

        _box.x = next.x;
        _box.y = next.y;
        _panel.style.left = next.x;
        _panel.style.top = next.y;
        evt.StopPropagation();
    }

    private void OnDragStripPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _dragPointerId)
        {
            return;
        }

        _dragging = false;
        _dragPointerId = -1;
        ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
        SaveLayout();
    }

    private void OnDragCaptureLost(PointerCaptureOutEvent evt)
    {
        _dragging = false;
        _dragPointerId = -1;
    }

    // ─── Растягивание за грани и углы ───

    private void OnResizePointerDown(ChatResizeCursor.BoxEdge edge, PointerDownEvent evt)
    {
        if (_dragging || _resizing || evt.button != 0 || _panel.panel == null || !PinBoxFromStyles())
        {
            return;
        }

        _resizeEdge = edge;
        _resizeStartBox = _box;
        _resizePointerId = evt.pointerId;
        _pointerStart = new Vector2(evt.position.x, evt.position.y);
        _resizing = true;
        evt.StopPropagation();
        ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
    }

    private void OnResizePointerMove(PointerMoveEvent evt)
    {
        if (!_resizing || evt.pointerId != _resizePointerId || _panel.panel == null)
        {
            return;
        }

        Vector2 delta = new Vector2(evt.position.x, evt.position.y) - _pointerStart;
        Rect screen = _panel.panel.visualTree.layout;

        // Browser-модель: тянем грань - противоположная стоит на месте.
        // Клампы: минимум размера, окно не вылезает за пределы экрана.
        float left = _resizeStartBox.xMin;
        float top = _resizeStartBox.yMin;
        float right = _resizeStartBox.xMax;
        float bottom = _resizeStartBox.yMax;

        if (_resizeEdge is ChatResizeCursor.BoxEdge.East or ChatResizeCursor.BoxEdge.Northeast or ChatResizeCursor.BoxEdge.Southeast)
        {
            right = Mathf.Clamp(right + delta.x, left + MinWidth, screen.width);
        }

        if (_resizeEdge is ChatResizeCursor.BoxEdge.West or ChatResizeCursor.BoxEdge.Northwest or ChatResizeCursor.BoxEdge.Southwest)
        {
            left = Mathf.Clamp(left + delta.x, 0f, right - MinWidth);
        }

        if (_resizeEdge is ChatResizeCursor.BoxEdge.South or ChatResizeCursor.BoxEdge.Southeast or ChatResizeCursor.BoxEdge.Southwest)
        {
            bottom = Mathf.Clamp(bottom + delta.y, top + MinHeight, screen.height);
        }

        if (_resizeEdge is ChatResizeCursor.BoxEdge.North or ChatResizeCursor.BoxEdge.Northeast or ChatResizeCursor.BoxEdge.Northwest)
        {
            top = Mathf.Clamp(top + delta.y, 0f, bottom - MinHeight);
        }

        _box = Rect.MinMaxRect(left, top, right, bottom);
        _panel.style.left = left;
        _panel.style.top = top;
        _panel.style.width = _box.width;
        _panel.style.height = _box.height;
        evt.StopPropagation();
    }

    private void OnResizePointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _resizePointerId)
        {
            return;
        }

        _resizing = false;
        _resizePointerId = -1;
        ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
        SaveLayout();
    }

    private void OnResizeCaptureLost(PointerCaptureOutEvent evt)
    {
        _resizing = false;
        _resizePointerId = -1;
    }

    // ─── Персистентность ───

    // Сохраняем только на отпускании кнопки: UpdateSection пишет в память,
    // на диск попадает отложенной записью (debounce 0.25с, flush на выходе).
    private void SaveLayout()
    {
        _config.UpdateSection(
            config => config.Interface,
            settings =>
            {
                settings.HasChatWindowLayout = true;
                settings.ChatWindowX = _box.x;
                settings.ChatWindowY = _box.y;
                settings.ChatWindowWidth = _box.width;
                settings.ChatWindowHeight = _box.height;
            });
    }
}
