#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator;

// Builds the programmator UI Toolkit tree: the static layout (popup, panel,
// toolbar, program list, create dialog) lives in Programmator.uxml; this
// factory clones it, wires the handlers and owns the dynamic grid cells
// (rendering, hover highlighting, tooltips, page label). Cross-cutting
// behavior — what happens when a cell is clicked, a button is pressed, etc. —
// is supplied by the owner as concrete collaborator instances passed to
// Build().
internal sealed class ProgrammatorGridUIFactory : ILocalizableUI
{
    private readonly UIDocument _doc;
    private readonly ILocalizationService _loc;
    private readonly ProgrammatorData _data;
    private readonly IProgrammatorTextureCatalog _textures;

    private VisualElement? _popup;
    private VisualElement? _gridContainer;
    private VisualElement?[,]? _cells;
    private Label?[,]? _cellLabels;
    private Tooltip? _tooltip;
    private Label? _pageLabel;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private Button? _runBtn;
    private Button? _stopBtn;
    private VisualElement? _panel;
    private VisualElement? _programListPanel;
    private ScrollView? _listScroll;
    private Label? _programTitle;
    private TextField? _createInput;
    private VisualElement? _createDialog;
    private const float CELLSIZE = 32f;
    private const float CELL_GAP = 2f;

    public ProgrammatorGridUIFactory(
        UIDocument doc,
        ILocalizationService loc,
        ProgrammatorData data,
        IProgrammatorTextureCatalog textures)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
    }

    public VisualElement Popup => _popup!;
    public VisualElement Panel => _panel!;
    public VisualElement ProgramListPanel => _programListPanel!;
    public Label ProgramTitle => _programTitle!;
    public ScrollView ListScroll => _listScroll!;
    public TextField CreateInput => _createInput!;
    public VisualElement CreateDialog => _createDialog!;
    public Button RunBtn => _runBtn!;
    public Button StopBtn => _stopBtn!;

    public void Build(
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial,
        ProgrammatorProgramStore programs,
        ProgrammatorClipboardController clipboard,
        Action onHide)
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
            ProjectRuntimeContracts.ResourcePaths.ProgrammatorUxml) ??
            throw new InvalidOperationException(
                "[Programmator] Resources/UI/Programmator.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        _doc.rootVisualElement.Add(tree);

        // Статические ключи UXML (programmator.*, common.*) резолвятся сразу при
        // сборке, а не только при смене языка — иначе попап показывает сырые
        // ключи до первого переключения языка.
        UILocalizer.Apply(tree, _loc);

        _popup = tree.Q<VisualElement>("ProgrammatorPopup") ??
            throw new InvalidOperationException("[Programmator] ProgrammatorPopup is missing from Programmator.uxml.");
        _panel = tree.Q<VisualElement>("ProgrammatorPanel") ??
            throw new InvalidOperationException("[Programmator] ProgrammatorPanel is missing from Programmator.uxml.");
        _programTitle = tree.Q<Label>("ProgramTitle") ??
            throw new InvalidOperationException("[Programmator] ProgramTitle is missing from Programmator.uxml.");
        _pageLabel = tree.Q<Label>("PageLabel") ??
            throw new InvalidOperationException("[Programmator] PageLabel is missing from Programmator.uxml.");

        Button prevBtn = tree.Q<Button>("PrevPageButton") ??
            throw new InvalidOperationException("[Programmator] PrevPageButton is missing from Programmator.uxml.");
        prevBtn.clicked += programs.PrevPage;
        _prevBtn = prevBtn;
        Button nextBtn = tree.Q<Button>("NextPageButton") ??
            throw new InvalidOperationException("[Programmator] NextPageButton is missing from Programmator.uxml.");
        nextBtn.clicked += programs.NextPage;
        _nextBtn = nextBtn;

        IntegerField pageInput = tree.Q<IntegerField>("PageInput") ??
            throw new InvalidOperationException("[Programmator] PageInput is missing from Programmator.uxml.");
        pageInput.value = _data.CurrentPage + 1;
        pageInput.RegisterValueChangedCallback(evt =>
        {
            int page = evt.newValue - 1;
            if (page >= 0 && page < _data.PageCount && page != _data.CurrentPage)
            {
                selection.ClearSelection();
                radial.HideMenus();
                _data.CurrentPage = page;
                programs.RefreshAllCells();
            }
            else
            {
                pageInput.SetValueWithoutNotify(_data.CurrentPage + 1);
            }
        });

        Button addPageBtn = tree.Q<Button>("AddPageButton") ??
            throw new InvalidOperationException("[Programmator] AddPageButton is missing from Programmator.uxml.");
        addPageBtn.clicked += programs.AddPageClick;
        Button removePageBtn = tree.Q<Button>("RemovePageButton") ??
            throw new InvalidOperationException("[Programmator] RemovePageButton is missing from Programmator.uxml.");
        removePageBtn.clicked += programs.RemovePageClick;

        Button shiftUpBtn = tree.Q<Button>("ShiftUpButton") ??
            throw new InvalidOperationException("[Programmator] ShiftUpButton is missing from Programmator.uxml.");
        shiftUpBtn.clicked += () => clipboard.ShiftSelection(0, -1);
        Button shiftDownBtn = tree.Q<Button>("ShiftDownButton") ??
            throw new InvalidOperationException("[Programmator] ShiftDownButton is missing from Programmator.uxml.");
        shiftDownBtn.clicked += () => clipboard.ShiftSelection(0, 1);
        Button shiftLeftBtn = tree.Q<Button>("ShiftLeftButton") ??
            throw new InvalidOperationException("[Programmator] ShiftLeftButton is missing from Programmator.uxml.");
        shiftLeftBtn.clicked += () => clipboard.ShiftSelection(-1, 0);
        Button shiftRightBtn = tree.Q<Button>("ShiftRightButton") ??
            throw new InvalidOperationException("[Programmator] ShiftRightButton is missing from Programmator.uxml.");
        shiftRightBtn.clicked += () => clipboard.ShiftSelection(1, 0);

        Button saveBtn = tree.Q<Button>("SaveButton") ??
            throw new InvalidOperationException("[Programmator] SaveButton is missing from Programmator.uxml.");
        saveBtn.clicked += programs.SaveProgram;
        _runBtn = tree.Q<Button>("RunButton") ??
            throw new InvalidOperationException("[Programmator] RunButton is missing from Programmator.uxml.");
        _runBtn.clicked += programs.RunProgram;
        _stopBtn = tree.Q<Button>("StopButton") ??
            throw new InvalidOperationException("[Programmator] StopButton is missing from Programmator.uxml.");
        _stopBtn.clicked += programs.StopProgram;
        _stopBtn.SetEnabled(false);

        Button closeBtn = tree.Q<Button>("ProgrammatorCloseButton") ??
            throw new InvalidOperationException("[Programmator] ProgrammatorCloseButton is missing from Programmator.uxml.");
        closeBtn.clicked += programs.CloseProgram;

        VisualElement gridContainer = tree.Q<VisualElement>("GridContainer") ??
            throw new InvalidOperationException("[Programmator] GridContainer is missing from Programmator.uxml.");
        _gridContainer = gridContainer;

        _cells = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];
        _cellLabels = new Label[ProgrammatorData.ROWS, ProgrammatorData.COLS];

        for (int i = 0; i < ProgrammatorData.ROWS; i++)
        {
            for (int j = 0; j < ProgrammatorData.COLS; j++)
            {
                int row = i, col = j;
                var cell = new VisualElement();
                cell.AddToClassList("prog-cell");

                cell.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    _data.HoveredCell = (row * ProgrammatorData.COLS) + col;
                    if (!selection.IsSelected(row, col))
                    {
                        HighlightCell(row, col, true);
                    }

                    ShowCellTooltip(row, col);
                });
                cell.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (_data.HoveredCell == (row * ProgrammatorData.COLS) + col)
                    {
                        if (!selection.IsSelected(row, col))
                        {
                            HighlightCell(row, col, false);
                        }

                        _data.HoveredCell = -1;
                    }

                    _tooltip?.Hide();
                });

                cell.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    _tooltip?.UpdatePosition(evt.position);
                });

                // LMB — selection
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    if (radial.IsShown)
                    {
                        radial.HideAll();
                        return;
                    }

                    if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed)
                    {
                        selection.ToggleCellSelection(row, col);
                    }
                    else if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
                    {
                        selection.ExtendSelection(row, col);
                    }
                    else
                    {
                        selection.SelectCell(row, col);
                    }
                });

                // RMB — radial menu
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1)
                    {
                        return;
                    }

                    if (radial.IsShown)
                    {
                        radial.HideAll();
                        return;
                    }

                    radial.HandleCellRightClick(row, col, _cells![row, col]!);
                });

                var label = new Label();
                label.AddToClassList("prog-cell-label");
                label.pickingMode = PickingMode.Ignore;
                cell.Add(label);

                _cells[row, col] = cell;
                _cellLabels[row, col] = label;
                gridContainer.Add(cell);
            }
        }

        _programListPanel = tree.Q<VisualElement>("ProgramListPanel") ??
            throw new InvalidOperationException("[Programmator] ProgramListPanel is missing from Programmator.uxml.");
        Button listCloseBtn = tree.Q<Button>("ListCloseButton") ??
            throw new InvalidOperationException("[Programmator] ListCloseButton is missing from Programmator.uxml.");
        listCloseBtn.clicked += () => onHide();
        _listScroll = tree.Q<ScrollView>("ListScroll") ??
            throw new InvalidOperationException("[Programmator] ListScroll is missing from Programmator.uxml.");

        Button createBtn = tree.Q<Button>("CreateButton") ??
            throw new InvalidOperationException("[Programmator] CreateButton is missing from Programmator.uxml.");
        createBtn.clicked += programs.ShowCreateInput;

        _createDialog = tree.Q<VisualElement>("CreateDialog") ??
            throw new InvalidOperationException("[Programmator] CreateDialog is missing from Programmator.uxml.");
        _createInput = tree.Q<TextField>("CreateInput") ??
            throw new InvalidOperationException("[Programmator] CreateInput is missing from Programmator.uxml.");
        _createInput.value = _loc.Get("programmator.program", programs.ProgramCount + 1);
        _createInput.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                programs.CreateNewProgram(_createInput!.value);
            }
        });

        Button dialogCancelBtn = tree.Q<Button>("DialogCancelButton") ??
            throw new InvalidOperationException("[Programmator] DialogCancelButton is missing from Programmator.uxml.");
        dialogCancelBtn.clicked += programs.HideCreateInput;
        Button dialogConfirmBtn = tree.Q<Button>("DialogConfirmButton") ??
            throw new InvalidOperationException("[Programmator] DialogConfirmButton is missing from Programmator.uxml.");
        dialogConfirmBtn.clicked += () => programs.CreateNewProgram(_createInput!.value);

        _tooltip = new Tooltip();
        _tooltip.Initialize(_doc);

        // Register only after every element used by ApplyLocalizedText has been
        // resolved. LocalizationService applies the text immediately on
        // registration, so registering earlier would call UpdatePageLabel
        // while _pageLabel and the pagination buttons are still null.
        _loc.RegisterLocalizable(this);
    }

    /// <summary>
    /// Переприменяет локализованный текст после смены языка: статические ключи
    /// UXML через UILocalizer, динамические (заголовок, страница, подписи ячеек) —
    /// напрямую. Реестр LocalizationService вызывает этот метод при регистрации
    /// и на каждой смене языка; PlayerHUDView тоже делегирует сюда из своего
    /// ApplyLocalizedText (идемпотентно).
    /// </summary>
    public void ApplyLocalizedText()
    {
        if (_popup != null)
        {
            UILocalizer.Apply(_popup, _loc);
        }

        if (_programTitle != null)
        {
            _programTitle.text = _loc.Get("programmator.title");
        }

        UpdatePageLabel();

        if (_cells != null)
        {
            for (int row = 0; row < ProgrammatorData.ROWS; row++)
            {
                for (int col = 0; col < ProgrammatorData.COLS; col++)
                {
                    if (_cells[row, col] != null)
                    {
                        UpdateCell(row, col);
                    }
                }
            }
        }

        if (_popup != null)
        {
            UILocalizer.AssertLocalized(_popup, _loc);
        }
    }

    /// <summary>Снимает фабрику с реестра локализации.</summary>
    public void Dispose()
    {
        _loc.UnregisterLocalizable(this);
    }

    public void UpdateCell(int row, int col)
    {
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        int id = _data.Codes[idx];
        var action = (ProgAction)id;
        var cell = _cells![row, col]!;
        var label = _cellLabels![row, col]!;

        var tex = _textures.GetTexture(action);
        if (tex != null)
        {
            cell.style.backgroundImage = new StyleBackground(tex);
            cell.style.backgroundSize = new BackgroundSize(tex.width, tex.height);
            cell.style.backgroundColor = Color.clear;
            label.text = string.Empty;
        }
        else if (id == 0)
        {
            cell.style.backgroundImage = null;
            cell.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            label.text = string.Empty;
        }
        else
        {
            cell.style.backgroundImage = null;
            cell.style.backgroundColor = new Color(0.3f, 0.1f, 0.1f, 1f);
            string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? _loc.Get(n) : string.Empty;
            label.text = name;
        }
    }

    public void SetSelectionBorder(int row, int col, bool selected)
    {
        _cells![row, col]!.EnableInClassList("prog-cell--selected", selected);
    }

    private void HighlightCell(int row, int col, bool highlight)
    {
        _cells![row, col]!.EnableInClassList("prog-cell--hover", highlight);
    }

    public void UpdatePageLabel()
    {
        _pageLabel!.text = _loc.Get("programmator.page", _data.CurrentPage + 1, _data.PageCount);
        _prevBtn!.SetEnabled(_data.CurrentPage > 0);
        _nextBtn!.SetEnabled(_data.CurrentPage < _data.PageCount - 1);
    }

    private void ShowCellTooltip(int row, int col)
    {
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        int opId = _data.Codes[idx];
        var action = (ProgAction)opId;
        string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? _loc.Get(n) : _loc.Get("programmator.code", opId);
        string desc = ProgrammatorData.OPERATOR_DESCRIPTIONS.TryGetValue(action, out var d) ? _loc.Get(d) : string.Empty;
        string text = string.IsNullOrEmpty(desc)
            ? _loc.Get("programmator.cell", col, row, name)
            : _loc.Get("programmator.cell_desc", col, row, name, desc);
        _tooltip?.Show(text, Vector2.zero);
    }

}
