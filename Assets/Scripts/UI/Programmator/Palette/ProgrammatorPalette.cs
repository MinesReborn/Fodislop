#nullable enable

using System;
using Kern.Core;
using Kern.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Localization;
using Kern.UI.Tooltips;

namespace Kern.UI.Programmator;

// Палитра символов для вставки мышью: плавающая панель с секциями категорий
// (заголовок + сетка). Заменяет радиальное меню, у которого внешнее кольцо
// физически не вмещало крупные категории. Логика вставки не здесь: палитра
// только сообщает выбор (OnOperatorClicked) — запись в ячейку делает владелец.
internal sealed class ProgrammatorPalette
{
    // Геометрия известна до лайаута: ширина фиксируется в USS (460px ровно
    // под ряд из 9 пунктов), высота ограничена max-height. Константа для
    // клэмпинга — консервативный максимум, чтобы панель не вылезала за экран.
    private const float PanelWidth = 460f;
    private const float PanelMaxHeight = 600f;

    // Строки палитры фиксированной длины: количество операторов в ряду не
    // зависит от ширины панели/тира (flex-wrap давал плавающее число).
    // 9 ячеек × 38px = 342px влезают даже в compact-ширину; иконки в родном
    // размере (SetNativeSize старого клиента) по центру пункта.
    private const int ColumnsPerRow = 9;

    private readonly ILocalizationService _loc;
    private readonly IProgrammatorTextureCatalog _textures;
    private readonly VisualElement _root;
    private readonly VisualElement _content;
    private readonly Tooltip _tooltip;

    public event Action<ProgAction>? OnOperatorClicked;

    public event Action? OnCloseClicked;

    // Наблюдатель остался на джойстике: перетаскивание кодирует относительные
    // направления (Cell/Shift, руки), которых нет у плоской сетки.
    public event Action? OnJoystickRequested;

    public VisualElement Root => _root;

    public bool IsShown => _root.parent != null;

    public ProgrammatorPalette(ILocalizationService loc, IProgrammatorTextureCatalog textures, UIDocument doc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));

        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
            ResourcePaths.ProgrammatorPaletteUxml) ??
            throw new InvalidOperationException(
                "[ProgrammatorPalette] Resources/UI/Programmator/Palette/Templates/ProgrammatorPalette.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        // Класс позиционирования (absolute, ширина) — на самом контейнере:
        // OpenAt задаёт left/top именно ему. В UXML класс не дублируется.
        tree.AddToClassList("prog-palette");
        tree.pickingMode = PickingMode.Ignore;
        _root = tree;

        _content = _root.Q<VisualElement>("PaletteContent") ??
            throw new InvalidOperationException(
                "[ProgrammatorPalette] PaletteContent is missing from ProgrammatorPalette.uxml.");
        Button closeButton = _root.Q<Button>("PaletteCloseButton") ??
            throw new InvalidOperationException(
                "[ProgrammatorPalette] PaletteCloseButton is missing from ProgrammatorPalette.uxml.");
        closeButton.clicked += () => OnCloseClicked?.Invoke();

        _tooltip = new Tooltip();
        _tooltip.Initialize(doc);

        // Секции статичны (набор категорий и операторов не меняется в рантайме)
        // — строим один раз, а не на каждое открытие.
        BuildSections();

        // Статические ключи Uxml резолвятся сразу при сборке (контракт един
        // для всех экранов; у палитры их нет — заголовки секций локализуются
        // при построении).
        UILocalizer.Apply(tree, _loc);
    }

    public void OpenAt(VisualElement parent, Vector2 anchor)
    {
        float x = Mathf.Clamp(anchor.x + 14f, 0f, Mathf.Max(0f, parent.layout.width - PanelWidth));
        float y = Mathf.Clamp(anchor.y - (PanelMaxHeight / 2f), 0f, Mathf.Max(0f, parent.layout.height - PanelMaxHeight));
        _root.style.left = x;
        _root.style.top = y;
        parent.Add(_root);
    }

    public void Close()
    {
        _tooltip.Hide();
        if (_root.parent != null)
        {
            _root.RemoveFromHierarchy();
        }
    }

    private void BuildSections()
    {
        for (int i = 0; i < ProgrammatorData.CATEGORIES.Length; i++)
        {
            int categoryId = ProgrammatorData.CATEGORIES[i];
            if (!ProgrammatorData.CATEGORY_OPERATORS.TryGetValue(categoryId, out var operators))
            {
                continue;
            }

            var section = new VisualElement();
            section.AddToClassList("prog-palette-section");
            if (i == 0)
            {
                section.AddToClassList("prog-palette-section--first");
            }

            section.Add(BuildSectionHeader(categoryId, categoryId == ProgrammatorData.CAT_OBSERVER));
            section.Add(BuildGrid(categoryId, operators));
            _content.Add(section);
        }
    }

    private VisualElement BuildSectionHeader(int categoryId, bool withJoystickButton)
    {
        var header = new VisualElement();
        header.AddToClassList("prog-palette-section-header");

        if (ProgrammatorData.CATEGORY_COLORS.TryGetValue(categoryId, out Color accent))
        {
            var dot = new VisualElement();
            dot.AddToClassList("prog-palette-section-dot");
            dot.style.backgroundColor = accent;
            header.Add(dot);
        }

        var title = new Label();
        title.AddToClassList("prog-palette-section-title");
        title.text = ProgrammatorData.CATEGORY_NAMES.TryGetValue(categoryId, out var key)
            ? _loc.Get(key)
            : string.Empty;
        title.pickingMode = PickingMode.Ignore;
        header.Add(title);

        if (withJoystickButton)
        {
            var joyButton = new Button(() => OnJoystickRequested?.Invoke());
            joyButton.AddToClassList("prog-palette-joy-btn");
            joyButton.text = "◎";
            joyButton.tooltip = _loc.Get("programmator.palette.joystick");
            header.Add(joyButton);
        }

        return header;
    }

    private VisualElement BuildGrid(int categoryId, ProgAction[] operators)
    {
        var grid = new VisualElement();
        grid.AddToClassList("prog-palette-grid");

        VisualElement? row = null;
        int inRow = 0;
        foreach (ProgAction action in operators)
        {
            if (inRow == ColumnsPerRow)
            {
                row = null;
                inRow = 0;
            }

            row ??= NewRow();
            row.Add(BuildItem(action));
            inRow++;
        }

        return grid;

        VisualElement NewRow()
        {
            var r = new VisualElement();
            r.AddToClassList("prog-palette-row");
            grid.Add(r);
            return r;
        }
    }

    private VisualElement BuildItem(ProgAction action)
    {
        var item = new VisualElement();
        item.AddToClassList("prog-palette-item");
        item.pickingMode = PickingMode.Position;

        // Как в сетке: иконка родного размера (SetNativeSize старого
        // клиента), по центру пункта; left/top выставляет код по исходным
        // размерам арта.
        var glyph = new VisualElement();
        glyph.AddToClassList("prog-palette-item-glyph");
        glyph.pickingMode = PickingMode.Ignore;
        item.Add(glyph);

        if (_textures.GetGlyph(action) is { } icon)
        {
            ApplyIcon(glyph, icon);
        }
        else
        {
            var label = new Label(GetOperatorName(action));
            label.AddToClassList("prog-palette-item-label");
            label.pickingMode = PickingMode.Ignore;
            item.Add(label);
        }

        item.RegisterCallback<PointerDownEvent>(_ => OnOperatorClicked?.Invoke(action));
        item.RegisterCallback<PointerEnterEvent>(evt =>
        {
            _tooltip.Show(BuildTooltipText(action), evt.position);
        });
        item.RegisterCallback<PointerMoveEvent>(evt => _tooltip.UpdatePosition(evt.position));
        item.RegisterCallback<PointerLeaveEvent>(_ => _tooltip.Hide());

        return item;
    }

    private static void ApplyIcon(VisualElement glyph, ProgGlyph icon)
    {
        // Тот же масштаб, что у сетки: родной размер арта × IconScale.
        float iconWidth = icon.Width * ProgrammatorData.IconScale;
        float iconHeight = icon.Height * ProgrammatorData.IconScale;
        glyph.style.backgroundImage = new StyleBackground(icon.Texture);
        // Глиф по исходному размеру арта — Contain даёт ровно 1:1.
        glyph.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        glyph.style.width = iconWidth;
        glyph.style.height = iconHeight;
        glyph.style.left = (ProgrammatorData.CELL_SIZE - iconWidth) / 2f;
        glyph.style.top = (ProgrammatorData.CELL_SIZE - iconHeight) / 2f;
    }

    private string GetOperatorName(ProgAction action)
    {
        return ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var nameKey)
            ? _loc.Get(nameKey)
            : ((int)action).ToString();
    }

    // Тот же формат, что у ячеек сетки: название, описание, хоткей.
    private string BuildTooltipText(ProgAction action)
    {
        string name = GetOperatorName(action);
        string desc = ProgrammatorData.OPERATOR_DESCRIPTIONS.TryGetValue(action, out var descKey)
            ? _loc.Get(descKey)
            : string.Empty;
        string? hotkey = ProgrammatorKeymap.DescribeHotkey(action);
        string hotkeyLine = hotkey != null
            ? _loc.Get("programmator.hotkey", hotkey)
            : _loc.Get("programmator.no_hotkey");
        string text = string.IsNullOrEmpty(desc) ? name : $"{name}\n{desc}";
        return text + "\n" + hotkeyLine;
    }
}
