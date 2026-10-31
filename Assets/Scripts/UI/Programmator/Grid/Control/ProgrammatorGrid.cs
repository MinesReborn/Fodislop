#nullable enable

using System;
using Kern.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

using Kern.Networking.Processing;
using Kern.UI.Input;

namespace Kern.UI.Programmator;

// Thin lifecycle owner for the programmator popup. UI construction, cell
// rendering, selection, clipboard, radial menu, and program storage each
// live in their own focused type; this class only wires them together once
// and dispatches input.
public sealed class ProgrammatorGrid : IDisposable
    {
        private readonly UIDocument _doc;
        private readonly ILocalizationService _loc;
        private readonly ProgrammatorData _data;
        private readonly UIInputManager _uiInput;
        private readonly IProgrammatorTextureCatalog _textures;
        private readonly ProgrammatorProcessor _protocol;

        private ProgrammatorGridUIFactory? _view;
        private ProgrammatorSelectionModel? _selection;
        private ProgrammatorRadialController? _radial;
        private ProgrammatorProgramStore? _programs;
        private ProgrammatorClipboardController? _clipboard;
        private ProgrammatorEditCommands? _commands;
        private ProgrammatorKeyboardInsert? _keyboardInsert;

        private bool _isOpen;

        private bool _uiBuilt;

        /// <summary>Хранилище программ (имя активной, состояние выполнения) для HUD.</summary>
        internal ProgrammatorProgramStore? Programs => _programs;

        public ProgrammatorGrid(
            UIDocument doc,
            ILocalizationService loc,
            ProgrammatorData data,
            UIInputManager uiInput,
            IProgrammatorTextureCatalog textures,
            ProgrammatorProcessor protocol)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _uiInput = uiInput ?? throw new ArgumentNullException(nameof(uiInput));
            _textures = textures ?? throw new ArgumentNullException(nameof(textures));
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _protocol.OpenRequested += Show;
            // Серверные иконки операторов догружаются асинхронно — когда
            // очередная приходит, перерисовываем текущую страницу.
            _textures.ServerTexturesChanged += OnServerTexturesChanged;
        }

        private void OnServerTexturesChanged()
        {
            if (_uiBuilt)
            {
                _programs?.RefreshAllCells();
            }
        }

        public void Initialize()
        {
            TryBuildUI();
        }

        public void RefreshLocalization()
        {
            _view?.ApplyLocalizedText();
        }

        private void TryBuildUI()
        {
            if (_uiBuilt)
            {
                return;
            }

            if (_doc == null)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                // Тихий возврат ожидаем: TryBuildUI вызывается из Start, когда
                // панель уже создана; гард защищает от вызова до активации
                // документа.
                return;
            }

            var view = new ProgrammatorGridUIFactory(_doc, _loc, _data, _textures);
            var selection = new ProgrammatorSelectionModel(view.SetSelectionBorder, _data);
            var radial = new ProgrammatorRadialController(_doc, view.UpdateCell, _loc, _data, _textures);
            var programs = new ProgrammatorProgramStore(
                view,
                selection,
                radial,
                _loc,
                _data,
                _protocol);
            radial.OnLastCellPlaced = programs.AdvancePageIfAtEnd;
            var clipboard = new ProgrammatorClipboardController(selection, view.UpdateCell, _data);
            var keyboardInsert = new ProgrammatorKeyboardInsert(_data, view.UpdateCell, IsTextFieldFocused);

            view.Build(selection, radial, programs, clipboard, Hide, CloseToProgramList);
            var commands = new ProgrammatorEditCommands(_data, selection, clipboard, programs, view);
            view.BindTouchEditing(commands, selection, radial);

            _view = view;
            _selection = selection;
            _radial = radial;
            _programs = programs;
            _clipboard = clipboard;
            _commands = commands;
            _keyboardInsert = keyboardInsert;
            programs.RunStateChanged += OnProgramRunStateChanged;

            if (_view == null)
            {
                return;
            }

            _view.Popup.style.display = DisplayStyle.None;
            _uiBuilt = true;
        }

        public void Tick()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
                if (!_uiBuilt)
                {
                    return;
                }
            }

            if (!Application.isFocused || _uiInput.IsKeyCaptureInProgress || Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                if (Keyboard.current.pKey.wasPressedThisFrame &&
                    !_uiInput.IsChatFocused &&
                    !_uiInput.IsPauseMenuOpen)
                {
                    _protocol.RequestProgramList();
                    return;
                }

                // R — глобальный пуск/остановка программы (PROG_KEY старого
                // клиента → ProgPanel.OnPlayStop); окно она не открывает.
                if (Keyboard.current.rKey.wasPressedThisFrame &&
                    !_uiInput.IsChatFocused &&
                    !_uiInput.IsPauseMenuOpen &&
                    !IsTextFieldFocused())
                {
                    ToggleProgramRun();
                }

                return;
            }

            if (Keyboard.current.pKey.wasPressedThisFrame &&
                !_radial!.IsShown &&
                !IsTextFieldFocused())
            {
                Hide();
                return;
            }

            // Над ячейкой R занята раскладкой символов (повороты); вне ячейки
            // R, как и при закрытом окне, пускает/останавливает программу.
            if (Keyboard.current.rKey.wasPressedThisFrame &&
                !_keyboardInsert!.IsCellHovered &&
                !_radial!.IsShown &&
                !IsTextFieldFocused())
            {
                ToggleProgramRun();
                return;
            }

            // ESC closes the programmator or goes back to list
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_selection!.HasSelection && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_selection.SelectedCells.Count > 0 && !_radial!.IsShown)
                {
                    _selection.ClearSelection();
                    return;
                }

                if (_view!.Panel.style.display == DisplayStyle.Flex)
                {
                    CloseToProgramList();
                    return;
                }

                Hide();
                return;
            }

            if (_radial!.IsShown)
            {
                // DEL clears the cell when radial menu is open
                if (Keyboard.current.deleteKey.wasPressedThisFrame)
                {
                    _radial.HandleDeleteKeyWhileShown();
                    return;
                }

                return;
            }

            // Ctrl shortcuts
            if (Keyboard.current.ctrlKey.isPressed)
            {
                if (Keyboard.current.zKey.wasPressedThisFrame)
                {
                    _commands!.Undo();
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    _commands!.Redo();
                }
                else if (Keyboard.current.cKey.wasPressedThisFrame)
                {
                    _commands!.Copy();
                }
                else if (Keyboard.current.xKey.wasPressedThisFrame)
                {
                    _commands!.Cut();
                }
                else if (Keyboard.current.vKey.wasPressedThisFrame)
                {
                    _commands!.Paste();
                }

                return;
            }

            // DEL clears selected cells
            if (Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                _commands!.DeleteSelectionOrHovered();
                return;
            }

            // Стрелки: Shift+стрелки над ячейкой сдвигают цепочку операторов
            // от курсора, без Shift — листание страниц. В полях ввода стрелки
            // двигают каретку и ничего не переключают.
            if (!IsTextFieldFocused())
            {
                bool shiftHeld = Keyboard.current.shiftKey.isPressed;
                if (shiftHeld && _data.HoveredCell >= 0)
                {
                    if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                    {
                        ShiftHoveredCode(-1, 0);
                    }
                    else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                    {
                        ShiftHoveredCode(1, 0);
                    }
                    else if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                    {
                        ShiftHoveredCode(0, -1);
                    }
                    else if (Keyboard.current.downArrowKey.wasPressedThisFrame)
                    {
                        ShiftHoveredCode(0, 1);
                    }
                }
                else if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                {
                    _programs!.PrevPage();
                }
                else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                {
                    _programs!.NextPage();
                }
            }

            // Наведение на ячейку + клавиша раскладки — предпросмотр символа.
            // Стоит после Ctrl-ветки: комбинации Ctrl+X/C/V не попадают сюда.
            _keyboardInsert!.Tick(Keyboard.current);
        }

        public void Show()
        {
            if (!_uiBuilt)
            {
                TryBuildUI();
            }

            if (!_uiBuilt || _view == null)
            {
                // UI ещё не готов (DI-инъекция не завершилась) — кнопка просто не
                // открывает программатор в этот раз; TryBuildUI ретраится из Update.
                return;
            }

            _isOpen = true;
            _uiInput.IsProgrammatorOpen = true;
            _view.Popup.style.display = DisplayStyle.Flex;

            // Окно — поверх HUD: дерево HUD добавлено в документ позже, и без
            // этого кнопки HUD перекрывали бы список программ и сетку.
            _view.Popup.parent?.BringToFront();
        }

        public void Hide()
        {
            // Закрытие окна НЕ останавливает программу: она выполняется на
            // сервере, управляется R/■ и инструкцией Terminate. Остановка
            // здесь убивала программу сразу после первого шага робота
            // (репорт: «двигается лишь на 1 клетку»).
            _selection!.ClearSelection();
            _radial!.HideAll();
            _keyboardInsert!.Reset();
            _isOpen = false;
            _uiInput.IsProgrammatorOpen = false;
            _programs!.HideCreateInput();
            _view!.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.None;
            _view.Popup.style.display = DisplayStyle.None;
        }

        private void CloseToProgramList()
        {
            _programs!.CloseProgram();
            Hide();
            _protocol.RequestProgramList();
        }

        // R: пуск/остановка активной программы — то же, что кнопки ▶/■
        // тулбара. Работает и при закрытом окне. Пока из списка не открыта
        // ни одна программа, пускать нечего — R не делает ничего.
        private void ToggleProgramRun()
        {
            if (_programs!.IsRunning)
            {
                _programs.StopProgram();
                return;
            }

            if (!_programs.HasActiveProgram)
            {
                return;
            }

            _programs.RunProgram();
        }

        // Shift+стрелки над ячейкой — сдвиг цепочки операторов от курсора
        // (перенос ProgrammatorView.ShiftCode старого клиента). Цепочка —
        // подряд стоящие непустые ячейки от курсора по направлению движения;
        // вся она сдвигается на одну ячейку, только если за ней в той же
        // строке/столбце есть пустая. Упёрлась в край — ничего не двигается.
        // Над пустой ячейкой сдвигать нечего.
        private void ShiftHoveredCode(int dx, int dy)
        {
            int hovered = _data.HoveredCell;
            if (hovered < 0)
            {
                return;
            }

            int row = hovered / ProgrammatorData.COLS;
            int col = hovered % ProgrammatorData.COLS;
            int pageBase = _data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE;

            int chain = 0;
            bool hasGap = false;
            while (true)
            {
                int r = row + (chain * dy);
                int c = col + (chain * dx);
                if (r < 0 || r >= ProgrammatorData.ROWS || c < 0 || c >= ProgrammatorData.COLS)
                {
                    break;
                }

                if (_data.Codes[pageBase + (r * ProgrammatorData.COLS) + c] == 0)
                {
                    hasGap = true;
                    break;
                }

                chain++;
            }

            if (!hasGap || chain == 0)
            {
                return;
            }

            _data.PushUndo();
            for (int k = chain; k > 0; k--)
            {
                int src = pageBase
                          + ((row + ((k - 1) * dy)) * ProgrammatorData.COLS)
                          + col + ((k - 1) * dx);
                int dst = pageBase
                          + ((row + (k * dy)) * ProgrammatorData.COLS)
                          + col + (k * dx);
                _data.Codes[dst] = _data.Codes[src];
                _data.Labels[dst] = _data.Labels[src];
                _data.Values[dst] = _data.Values[src];
            }

            _data.Codes[pageBase + hovered] = 0;
            _data.Labels[pageBase + hovered] = null;
            _data.Values[pageBase + hovered] = null;

            for (int k = 0; k <= chain; k++)
            {
                _view!.UpdateCell(row + (k * dy), col + (k * dx));
            }
        }

        // Запуск программы из окна: окно убирается с экрана, но программа
        // продолжает работать — ею управляют R/■ в HUD и на панели.
        private void OnProgramRunStateChanged(bool running)
        {
            if (running)
            {
                Hide();
            }
        }

    // Клавиши раскладки не должны срабатывать, пока пользователь печатает
    // в CreateInput или PageInput.
    // Клавиши раскладки не должны срабатывать, пока пользователь печатает
    // в CreateInput, PageInput или в поле значения ячейки. TextField и
    // IntegerField не наследуют TextElement — проверяются явно.
    private bool IsTextFieldFocused()
    {
        return _doc.rootVisualElement != null &&
               _doc.rootVisualElement.focusController.focusedElement
                   is TextElement or TextField or IntegerField;
    }

        public void Dispose()
        {
            if (_uiBuilt && _isOpen)
            {
                Hide();
            }

            _uiInput.IsProgrammatorOpen = false;
            _protocol.OpenRequested -= Show;
            _textures.ServerTexturesChanged -= OnServerTexturesChanged;
            _radial?.Dispose();

            if (_programs != null)
            {
                _programs.RunStateChanged -= OnProgramRunStateChanged;
            }

            // Фабрика зарегистрирована в реестре локализации — снимаем её,
            // чтобы смена языка не долетала до мёртвого попапа.
            _programs?.Dispose();
            _view?.Dispose();
            _view = null;
        }
}
