#nullable enable

using System;
using Fodinae.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator;

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

        private ProgrammatorGridUIFactory? _view;
        private ProgrammatorSelectionModel? _selection;
        private ProgrammatorRadialController? _radial;
        private ProgrammatorProgramStore? _programs;
        private ProgrammatorClipboardController? _clipboard;

        private bool _isOpen;

        private bool _uiBuilt;

        public ProgrammatorGrid(
            UIDocument doc,
            ILocalizationService loc,
            ProgrammatorData data,
            UIInputManager uiInput,
            IProgrammatorTextureCatalog textures)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _uiInput = uiInput ?? throw new ArgumentNullException(nameof(uiInput));
            _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        }

        public void Initialize()
        {
            TryBuildUI();
        }

        /// <summary>Переприменяет локализованный текст после смены языка.</summary>
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
            var programs = new ProgrammatorProgramStore(view, selection, radial, _loc, _data);
            radial.OnLastCellPlaced = programs.AdvancePageIfAtEnd;
            var clipboard = new ProgrammatorClipboardController(selection, view.UpdateCell, _data);

            view.Build(selection, radial, programs, clipboard, Hide);

            _view = view;
            _selection = selection;
            _radial = radial;
            _programs = programs;
            _clipboard = clipboard;

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

            if (Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                    !_uiInput.IsChatFocused &&
                    !_uiInput.IsPauseMenuOpen)
                {
                    Show();
                }

                return;
            }

            if ((Keyboard.current.pKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame) &&
                !_radial!.IsShown)
            {
                Hide();
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
                    _programs!.CloseProgram();
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
                    if (_data.Undo())
                    {
                        _programs!.RefreshAllCells();
                    }
                }
                else if (Keyboard.current.yKey.wasPressedThisFrame)
                {
                    if (_data.Redo())
                    {
                        _programs!.RefreshAllCells();
                    }
                }
                else if (Keyboard.current.cKey.wasPressedThisFrame && _selection!.HasAnySelection())
                {
                    _clipboard!.CopySelection();
                }
                else if (Keyboard.current.xKey.wasPressedThisFrame && _selection!.HasAnySelection())
                {
                    _clipboard!.CutSelection();
                }
                else if (Keyboard.current.vKey.wasPressedThisFrame && _clipboard!.HasClipboard)
                {
                    _clipboard.PasteClipboard();
                }

                return;
            }

            // DEL clears selected cells
            if (Keyboard.current.deleteKey.wasPressedThisFrame)
            {
                if (!_selection!.HasAnySelection())
                { /* fall through */
                }
                else if (_selection.SelectedCells.Count > 0)
                {
                    _data.PushUndo();
                    foreach (long key in _selection.SelectedCells)
                    {
                        int r = (int)(key / ProgrammatorData.COLS);
                        int c = (int)(key % ProgrammatorData.COLS);
                        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                  + (r * ProgrammatorData.COLS) + c;
                        _data.Codes[idx] = 0;
                        _view!.UpdateCell(r, c);
                    }

                    _selection.SelectedCells.Clear();
                    _selection.HasSelection = false;
                    return;
                }
                else if (_selection.HasSelection)
                {
                    _data.PushUndo();
                    int minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
                    int maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
                    int minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
                    int maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
                    for (int r = minRow; r <= maxRow; r++)
                    {
                        for (int c = minCol; c <= maxCol; c++)
                        {
                            int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                                      + (r * ProgrammatorData.COLS) + c;
                            _data.Codes[idx] = 0;
                            _view!.UpdateCell(r, c);
                        }
                    }

                    return;
                }
            }

            // Arrow keys for page navigation
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                _programs!.PrevPage();
            }
            else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                _programs!.NextPage();
            }
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
            _programs!.ShowProgramList();
        }

        public void Hide()
        {
            if (_programs!.IsRunning)
            {
                _programs.StopProgram();
            }

            _selection!.ClearSelection();
            _radial!.HideAll();
            _isOpen = false;
            _uiInput.IsProgrammatorOpen = false;
            _programs.HideCreateInput();
            _view!.ProgramListPanel.style.display = DisplayStyle.None;
            _view.Panel.style.display = DisplayStyle.None;
            _view.Popup.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            if (_uiBuilt && _isOpen)
            {
                Hide();
            }

            _uiInput.IsProgrammatorOpen = false;

            // Фабрика зарегистрирована в реестре локализации — снимаем её,
            // чтобы смена языка не долетала до мёртвого попапа.
            _view?.Dispose();
            _view = null;
        }
}
