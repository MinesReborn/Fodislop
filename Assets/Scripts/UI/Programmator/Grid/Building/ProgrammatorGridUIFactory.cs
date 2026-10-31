#nullable enable

using System;
using Kern.Core;
using Kern.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

using Kern.UI.Localization;
using Kern.UI.Screens;
using Kern.UI.Tooltips;

namespace Kern.UI.Programmator;

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
    private bool _touchMultiSelect;
    private VisualElement?[,]? _cellGlyphs;
    private Label?[,]? _cellLabels;
    private (TextField LabelField, TextField NumberField)?[,]? _cellInputs;
    private Tooltip? _tooltip;
    private ProgrammatorSelectionModel? _selection;
    private Label? _pageLabel;
    private Button? _prevBtn;
    private Button? _nextBtn;
    private Button? _runBtn;
    private Button? _stopBtn;
    private Label? _programError;
    private VisualElement? _panel;
    private VisualElement? _programListPanel;
    private ScrollView? _listScroll;
    private Label? _programTitle;
    private TextField? _createInput;
    private VisualElement? _createDialog;

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

    public void ShowProtocolError(string message)
    {
        if (_programError == null)
        {
            return;
        }

        _programError.text = message;
        Kern.UI.Screens.UIState.Show(_programError);
    }

    public void ClearProtocolError()
    {
        if (_programError == null)
        {
            return;
        }

        _programError.text = string.Empty;
        Kern.UI.Screens.UIState.Hide(_programError);
    }

    public void Build(
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial,
        ProgrammatorProgramStore programs,
        ProgrammatorClipboardController clipboard,
        Action onHide,
        Action onCloseToList)
    {
        _selection = selection;
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
            ResourcePaths.ProgrammatorUxml) ??
            throw new InvalidOperationException(
                "[Programmator] Resources/UI/Programmator/Grid/Templates/Programmator.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;
        _doc.rootVisualElement.Add(tree);

        // Статические ключи Uxml (programmator.*, common.*) резолвятся сразу при
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
        _programError = tree.Q<Label>("ProgramError") ??
            throw new InvalidOperationException("[Programmator] ProgramError is missing from Programmator.uxml.");
        Kern.UI.Screens.UIState.Hide(_programError);

        Button closeBtn = tree.Q<Button>("ProgrammatorCloseButton") ??
            throw new InvalidOperationException("[Programmator] ProgrammatorCloseButton is missing from Programmator.uxml.");
        // G7-C: «К списку» закрывает редактор и запрашивает серверный список
        // заново (OpenProgramListClickPacket), а не рисует локальный.
        closeBtn.clicked += onCloseToList;

        VisualElement gridContainer = tree.Q<VisualElement>("GridContainer") ??
            throw new InvalidOperationException("[Programmator] GridContainer is missing from Programmator.uxml.");
        _gridContainer = gridContainer;

        _cells = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];
        _cellGlyphs = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];
        _cellLabels = new Label[ProgrammatorData.ROWS, ProgrammatorData.COLS];
        _cellInputs = new (TextField, TextField)?[ProgrammatorData.ROWS, ProgrammatorData.COLS];

        for (int i = 0; i < ProgrammatorData.ROWS; i++)
        {
            for (int j = 0; j < ProgrammatorData.COLS; j++)
            {
                int row = i, col = j;
                var cell = new VisualElement();
                cell.AddToClassList("prog-cell");

                cell.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    _data.HoveredCell = (row * ProgrammatorData.COLS) + col;
                    if (!selection.IsSelected(row, col))
                    {
                        HighlightCell(row, col, true);
                    }

                    ShowCellTooltip(row, col, evt.position);
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
                    // Протяжка выделения: после нажатия ячейка захватывает
                    // указатель, поэтому движение при зажатой ЛКМ приходит
                    // именно в неё — переводим позицию в координаты сетки.
                    // pressedButtons: бит 0 — левая кнопка.
                    if (selection.IsDragging && (evt.pressedButtons & 1) != 0)
                    {
                        DragSelectTo(evt.position);
                    }

                    _tooltip?.UpdatePosition(evt.position);
                });

                cell.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        selection.EndDragSelect();
                    }
                });

                // LMB — selection
                cell.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    // Клик по встроенному полю значения — это правка значения
                    // (фокус в TextField), а не выделение ячейки.
                    if (evt.target is VisualElement target &&
                        target.GetFirstAncestorOfType<TextField>() != null)
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
                    else
                    {
                        // Обычное нажатие: сбрасывает выделение и начинает
                        // протяжку; клик без движения выделения не оставляет.
                        selection.BeginDragSelect(row, col);
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

                // Символ оператора — вложенный элемент размером с иконку
                // (родной размер арта × IconScale, выставляет UpdateCell).
                // Ignore: события мыши идут мимо него в ячейку.
                var glyph = new VisualElement();
                glyph.AddToClassList("prog-cell-glyph");
                glyph.pickingMode = PickingMode.Ignore;
                cell.Add(glyph);

                _cells[row, col] = cell;
                _cellGlyphs![row, col] = glyph;
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

        // G5-C: ✎/✕ действуют на программу, открытую в пульте. Имя вводится в
        // серверном окне — клиент только адресует программу (см. RenameProgram).
        Button renameProgramBtn = tree.Q<Button>("RenameProgramButton") ??
            throw new InvalidOperationException("[Programmator] RenameProgramButton is missing from Programmator.uxml.");
        renameProgramBtn.clicked += () =>
        {
            if (programs.HasActiveProgram)
            {
                programs.RenameProgram(programs.ActiveIndex);
            }
        };

        Button deleteProgramBtn = tree.Q<Button>("DeleteProgramButton") ??
            throw new InvalidOperationException("[Programmator] DeleteProgramButton is missing from Programmator.uxml.");
        deleteProgramBtn.clicked += () =>
        {
            if (programs.HasActiveProgram)
            {
                programs.DeleteProgram(programs.ActiveIndex);
            }
        };

        _tooltip = new Tooltip();
        _tooltip.Initialize(_doc);

        // Register only after every element used by ApplyLocalizedText has been
        // resolved. LocalizationService applies the text immediately on
        // registration, so registering earlier would call UpdatePageLabel
        // while _pageLabel and the pagination buttons are still null.
        _loc.RegisterLocalizable(this);
    }

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

    public void Dispose()
    {
        _loc.UnregisterLocalizable(this);
    }

    // Сенсорная правка: панель инструментов (отмена, повтор, буфер, удаление,
    // мульти-выбор) и жесты сетки. Видимость панели — Touch.uss; обработчики
    // висят всегда, скрытый элемент нажатий не получает.
    public void BindTouchEditing(
        ProgrammatorEditCommands commands,
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial)
    {
        VisualElement popup = _popup ??
            throw new InvalidOperationException("[Programmator] Build must run before BindTouchEditing.");

        Bind(popup, "ProgUndoButton", commands.Undo);
        Bind(popup, "ProgRedoButton", commands.Redo);
        Bind(popup, "ProgCopyButton", commands.Copy);
        Bind(popup, "ProgCutButton", commands.Cut);
        Bind(popup, "ProgPasteButton", commands.Paste);
        Bind(popup, "ProgDeleteButton", commands.DeleteSelection);

        Button multi = popup.Q<Button>("ProgMultiSelectButton") ??
            throw new InvalidOperationException("[Programmator] ProgMultiSelectButton is missing from Programmator.uxml.");
        multi.clicked += () =>
        {
            _touchMultiSelect = !_touchMultiSelect;
            multi.EnableInClassList("prog-touch-tool--active", _touchMultiSelect);
        };

        VisualElement viewport = popup.Q<VisualElement>("GridScroll") ??
            throw new InvalidOperationException("[Programmator] GridScroll is missing from Programmator.uxml.");
        // Жесты держатся на обработчиках окна сетки — отдельная ссылка не нужна.
        _ = new ProgrammatorTouchGrid(
            viewport,
            _gridContainer!,
            FindCell,
            (row, col) => OnTouchTap(row, col, selection, radial),
            (row, col) => OnTouchLongPress(row, col, selection, radial));
    }

    private static void Bind(VisualElement root, string name, Action action)
    {
        Button button = root.Q<Button>(name) ??
            throw new InvalidOperationException($"[Programmator] {name} is missing from Programmator.uxml.");
        button.clicked += action;
    }

    private Vector2Int? FindCell(VisualElement target)
    {
        for (VisualElement? current = target; current != null; current = current.parent)
        {
            if (!current.ClassListContains("prog-cell"))
            {
                continue;
            }

            for (int row = 0; row < ProgrammatorData.ROWS; row++)
            {
                for (int col = 0; col < ProgrammatorData.COLS; col++)
                {
                    if (_cells![row, col] == current)
                    {
                        return new Vector2Int(row, col);
                    }
                }
            }
        }

        return null;
    }

    // Тап: в режиме мульти-выбора — добавить/убрать клетку (Ctrl+клик мыши);
    // по уже выбранной — радиальное меню (ПКМ мыши); иначе — выбрать.
    private void OnTouchTap(int row, int col, ProgrammatorSelectionModel selection, ProgrammatorRadialController radial)
    {
        if (radial.IsShown)
        {
            radial.HideAll();
            return;
        }

        if (_touchMultiSelect)
        {
            selection.ToggleCellSelection(row, col);
        }
        else if (selection.IsSelected(row, col))
        {
            radial.HandleCellRightClick(row, col, _cells![row, col]!);
        }
        else
        {
            selection.SelectCell(row, col);
        }
    }

    private void OnTouchLongPress(int row, int col, ProgrammatorSelectionModel selection, ProgrammatorRadialController radial)
    {
        if (!selection.IsSelected(row, col))
        {
            selection.SelectCell(row, col);
        }

        radial.HandleCellRightClick(row, col, _cells![row, col]!);
    }

    public void UpdateCell(int row, int col)
    {
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        int id = _data.Codes[idx];
        var action = (ProgAction)id;
        var cell = _cells![row, col]!;
        var glyph = _cellGlyphs![row, col]!;
        var label = _cellLabels![row, col]!;

        // Как в старом клиенте (ProgAction.ChangeTo → Image.SetNativeSize):
        // иконка рисуется в родном размере арта, по центру слота, без рамок
        // и подложек. Размеры текстур разные (обрезанные спрайты 11×9…31×30)
        // — крупность оператора повторяет старый клиент 1:1. Геометрия — по
        // исходным Width/Height глифа; сама текстура может быть суперсэмплена
        // реестром против выпадения однопиксельных линий при дробном скейле
        // панели.
        ProgGlyph? icon = _textures.GetGlyph(action);
        cell.EnableInClassList("prog-cell--empty", icon == null && id == 0);
        cell.EnableInClassList("prog-cell--unknown", icon == null && id != 0);
        if (icon is { } glyphIcon)
        {
            // Иконка: родной размер арта × IconScale — сетка крупнее старого
            // клиента в 1.25 раза, пропорции операторов прежние.
            float iconWidth = glyphIcon.Width * ProgrammatorData.IconScale;
            float iconHeight = glyphIcon.Height * ProgrammatorData.IconScale;
            glyph.style.backgroundImage = new StyleBackground(glyphIcon.Texture);
            glyph.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            glyph.style.width = iconWidth;
            glyph.style.height = iconHeight;
            glyph.style.left = (ProgrammatorData.CELL_SIZE - iconWidth) / 2f;
            glyph.style.top = (ProgrammatorData.CELL_SIZE - iconHeight) / 2f;
            label.text = string.Empty;
        }
        else
        {
            glyph.style.backgroundImage = null;
            string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? _loc.Get(n) : string.Empty;
            label.text = id == 0 ? string.Empty : name;
        }

        UpdateCellInputs(row, col, idx, action, label);
    }

    // Поля значения в ячейке — перенос старого клиента (ProgAction.updateInput):
    // маленькие поля со смещением от центра иконки, иконка оператора остаётся
    // видна. Текст пишется в Labels, число — в Values; обе колонки уже уходят
    // в файл программ и в сетевой пакет на каждую инструкцию. Поля создаются
    // лениво и только для ячеек с оператором ввода.
    private void UpdateCellInputs(int row, int col, int idx, ProgAction action, Label label)
    {
        if (!ProgrammatorInputSpec.TryGet(action, out var spec))
        {
            if (_cellInputs![row, col] is { } hidden)
            {
                hidden.LabelField.style.display = DisplayStyle.None;
                hidden.NumberField.style.display = DisplayStyle.None;
            }

            return;
        }

        var (labelField, numberField) = EnsureCellInputs(row, col);
        label.text = string.Empty;
        labelField.style.display = DisplayStyle.Flex;
        labelField.maxLength = spec.TextMaxLength;
        labelField.SetValueWithoutNotify(_data.Labels[idx] ?? string.Empty);
        SetInputPositionClass(labelField, TextPositionClass(action));

        bool withNumber = spec.Kind == ProgrammatorInputSpec.InputKind.TextAndNumber;
        numberField.style.display = withNumber ? DisplayStyle.Flex : DisplayStyle.None;
        if (withNumber)
        {
            numberField.SetValueWithoutNotify(_data.Values[idx] ?? string.Empty);
            SetInputPositionClass(numberField, "prog-cell-input--pos-compare-num");
        }
    }

    // Позиционные классы соответствуют смещениям ProgAction.updateInput
    // старого клиента; геометрия пересчитана в Programmator.uss.
    private static string TextPositionClass(ProgAction action)
    {
        return action switch
        {
            ProgAction.Label => "prog-cell-input--pos-label",
            ProgAction.Goto => "prog-cell-input--pos-goto",
            ProgAction.Call or ProgAction.CallArg or ProgAction.CallState => "prog-cell-input--pos-call",
            ProgAction.YesNoGoto or ProgAction.NoYesGoto or ProgAction.CallWhenDied => "prog-cell-input--pos-branch",
            ProgAction.DebugPause or ProgAction.DebugShow => "prog-cell-input--pos-debug",
            _ => "prog-cell-input--pos-compare",
        };
    }

    private static readonly string[] s_inputPositionClasses =
    {
        "prog-cell-input--pos-label",
        "prog-cell-input--pos-goto",
        "prog-cell-input--pos-call",
        "prog-cell-input--pos-branch",
        "prog-cell-input--pos-debug",
        "prog-cell-input--pos-compare",
        "prog-cell-input--pos-compare-num",
    };

    private static void SetInputPositionClass(TextField field, string positionClass)
    {
        foreach (string candidate in s_inputPositionClasses)
        {
            field.EnableInClassList(candidate, candidate == positionClass);
        }
    }

    private (TextField LabelField, TextField NumberField) EnsureCellInputs(int row, int col)
    {
        if (_cellInputs![row, col] is { } existing)
        {
            return existing;
        }

        TextField labelField = MakeCellInput(row, col, numeric: false);
        TextField numberField = MakeCellInput(row, col, numeric: true);
        numberField.AddToClassList("prog-cell-input--num");
        _cells![row, col]!.Add(labelField);
        _cells[row, col]!.Add(numberField);
        var created = (labelField, numberField);
        _cellInputs[row, col] = created;
        return created;
    }

    private TextField MakeCellInput(int row, int col, bool numeric)
    {
        var field = new TextField();
        field.AddToClassList("prog-cell-input");
        field.pickingMode = PickingMode.Position;

        // Undo один раз на сессию фокуса: на каждый keystroke не пушим,
        // иначе один ввод текста съедает весь undo-стек.
        bool undoPushed = true;
        field.RegisterCallback<FocusEvent>(_ => undoPushed = false);
        field.RegisterValueChangedCallback(evt =>
        {
            int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            string value = evt.newValue ?? string.Empty;
            if (!undoPushed)
            {
                _data.PushUndo();
                undoPushed = true;
            }

            if (numeric)
            {
                _data.Values[idx] = value;
            }
            else
            {
                _data.Labels[idx] = value;
            }
        });

        if (numeric)
        {
            // Как getNum старого клиента: нечисловое или пустое значение
            // читается как 0 — нормализуем поле при потере фокуса.
            field.RegisterCallback<BlurEvent>(evt =>
            {
                if (int.TryParse(field.value, out int parsed))
                {
                    return;
                }

                field.SetValueWithoutNotify("0");
                int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                          + (row * ProgrammatorData.COLS) + col;
                _data.Values[idx] = "0";
            });
        }

        return field;
    }

    public void SetSelectionBorder(int row, int col, bool selected)
    {
        _cells![row, col]!.EnableInClassList("prog-cell--selected", selected);
    }

    // Позиция указателя (координаты панели) → ячейка сетки. Ячейки лежат
    // в контейнере плотным рядом 40px без рамок и отступов, поэтому границы
    // считаются напрямую; за пределами сетки зажимаем в крайнюю ячейку.
    private void DragSelectTo(Vector2 panelPosition)
    {
        if (_gridContainer == null || _gridContainer.panel == null)
        {
            return;
        }

        Vector2 local = _gridContainer.WorldToLocal(panelPosition);
        int col = Mathf.Clamp(
            Mathf.FloorToInt(local.x / ProgrammatorData.CELL_SIZE),
            0,
            ProgrammatorData.COLS - 1);
        int row = Mathf.Clamp(
            Mathf.FloorToInt(local.y / ProgrammatorData.CELL_SIZE),
            0,
            ProgrammatorData.ROWS - 1);
        _selection!.DragSelectTo(row, col);
    }

    private void HighlightCell(int row, int col, bool highlight)
    {
        _cells![row, col]!.EnableInClassList("prog-cell--hover", highlight);
    }

    public void UpdatePageLabel()
    {
        _pageLabel!.text = _loc.Get("programmator.page", _data.CurrentPage + 1, _data.PageCount);
        _prevBtn!.SetEnabled(_data.CurrentPage > 0);
        // «>» активна и на последней странице, пока есть запас до MaxPages —
        // NextPage тогда открывает новую страницу.
        _nextBtn!.SetEnabled(
            _data.CurrentPage < _data.PageCount - 1 ||
            _data.PageCount < ProgrammatorData.MaxPages);
    }

    // Тултип оператора: название, описание, строка хоткея («Хоткей
    // оператора: [ W ]», для циклов «[ W x 2 ]», вне раскладки — «нет
    // горячей клавиши»).
    private void ShowCellTooltip(int row, int col, Vector2 position)
    {
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        var action = (ProgAction)_data.Codes[idx];
        string name = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? _loc.Get(n) : _loc.Get("programmator.code", (int)action);
        string desc = ProgrammatorData.OPERATOR_DESCRIPTIONS.TryGetValue(action, out var d) ? _loc.Get(d) : string.Empty;

        string? hotkey = ProgrammatorKeymap.DescribeHotkey(action);
        string hotkeyLine = hotkey != null
            ? _loc.Get("programmator.hotkey", hotkey)
            : _loc.Get("programmator.no_hotkey");
        string text = string.IsNullOrEmpty(desc) ? name : $"{name}\n{desc}";
        _tooltip?.Show(text + "\n" + hotkeyLine, position);
    }

}
