using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Programmator
{
    public class ProgrammatorGrid : MonoBehaviour
    {
        private UIDocument _doc;
        private VisualElement _popup;
        private VisualElement _gridContainer;
        private VisualElement[,] _cells;
        private RadialMenu _radial;
        private bool _isOpen;
        private bool _radialShown;
        private const float CELLSIZE = 30f;
        private const float CELLGAP = 2f;

        protected void Start()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                return;
            }

            CreateUI();
            _popup.style.display = DisplayStyle.None;
        }

        private void CreateUI()
        {
            _popup = new VisualElement();
            _popup.style.position = Position.Absolute;
            _popup.style.left = 0;
            _popup.style.top = 0;
            _popup.style.right = 0;
            _popup.style.bottom = 0;
            _popup.style.justifyContent = Justify.Center;
            _popup.style.alignItems = Align.Center;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            dimmer.pickingMode = PickingMode.Ignore;
            _popup.Add(dimmer);

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.minWidth = 584;
            panel.style.minHeight = 520;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.marginBottom = 10;

            var title = new Label("Программатор");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            title.style.flexGrow = 1;
            topRow.Add(title);

            var closeBtn = new Button(() => Hide());
            closeBtn.text = "×";
            closeBtn.style.width = 24;
            closeBtn.style.height = 24;
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            closeBtn.style.fontSize = 18;
            closeBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;
            closeBtn.RegisterCallback<MouseEnterEvent>(_ =>
                closeBtn.style.color = Color.white);
            closeBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f));
            topRow.Add(closeBtn);

            panel.Add(topRow);

            var gridScroll = new ScrollView();
            gridScroll.style.flexGrow = 1;
            gridScroll.style.maxHeight = ProgrammatorData.ROWS * (CELLSIZE + (CELLGAP * 2));

            _gridContainer = new VisualElement();
            _gridContainer.style.flexDirection = FlexDirection.Row;
            _gridContainer.style.flexWrap = Wrap.Wrap;
            _gridContainer.style.width = ProgrammatorData.COLS * (CELLSIZE + (CELLGAP * 2));

            _cells = new VisualElement[ProgrammatorData.ROWS, ProgrammatorData.COLS];

            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    int row = i, col = j;
                    var cell = new VisualElement();
                    cell.style.width = CELLSIZE;
                    cell.style.height = CELLSIZE;
                    cell.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                    cell.style.borderTopWidth = 1;
                    cell.style.borderBottomWidth = 1;
                    cell.style.borderLeftWidth = 1;
                    cell.style.borderRightWidth = 1;
                    cell.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                    cell.style.marginLeft = CELLGAP;
                    cell.style.marginRight = CELLGAP;
                    cell.style.marginTop = CELLGAP;
                    cell.style.marginBottom = CELLGAP;

                    cell.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        ProgrammatorData.HoveredCell = (row * ProgrammatorData.COLS) + col;
                        HighlightCell(row, col, true);
                    });
                    cell.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        if (ProgrammatorData.HoveredCell == (row * ProgrammatorData.COLS) + col)
                        {
                            HighlightCell(row, col, false);
                            ProgrammatorData.HoveredCell = -1;
                        }
                    });

                    _cells[row, col] = cell;
                    _gridContainer.Add(cell);
                }
            }

            gridScroll.Add(_gridContainer);
            panel.Add(gridScroll);

            _popup.Add(panel);
            _doc.rootVisualElement.Add(_popup);

            _radial = new RadialMenu(ProgrammatorData.WOPERATORS);
            _radial.OnItemClicked += OnRadialItemClicked;
        }

        private void HighlightCell(int row, int col, bool highlight)
        {
            var cell = _cells[row, col];
            if (highlight)
            {
                cell.style.borderTopColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderBottomColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderLeftColor = new Color(1f, 0.84f, 0f, 1f);
                cell.style.borderRightColor = new Color(1f, 0.84f, 0f, 1f);
            }
            else
            {
                cell.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1f);
                cell.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }
        }

        private void UpdateCell(int row, int col)
        {
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLSPERPAGE)
                      + (row * ProgrammatorData.COLS) + col;
            int id = ProgrammatorData.Codes[idx];
            var cell = _cells[row, col];

            var tex = ProgrammatorTextureRegistry.GetTexture(id);
            if (tex != null)
            {
                cell.style.backgroundImage = new StyleBackground(tex);
                cell.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                cell.style.backgroundColor = Color.clear;
            }
            else if (id == 0)
            {
                cell.style.backgroundImage = null;
                cell.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            }
            else
            {
                cell.style.backgroundImage = null;
                cell.style.backgroundColor = new Color(0.3f, 0.1f, 0.1f, 1f);
            }
        }

        private void OnRadialItemClicked(int selectedId)
        {
            if (ProgrammatorData.HoveredCell < 0)
            {
                return;
            }

            int row = ProgrammatorData.HoveredCell / ProgrammatorData.COLS;
            int col = ProgrammatorData.HoveredCell % ProgrammatorData.COLS;
            int idx = (ProgrammatorData.CurrentPage * ProgrammatorData.CELLSPERPAGE)
                      + (row * ProgrammatorData.COLS) + col;
            ProgrammatorData.Codes[idx] = selectedId;
            UpdateCell(row, col);
        }

        protected void Update()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (!_radialShown)
            {
                if (Keyboard.current.wKey.wasPressedThisFrame && ProgrammatorData.HoveredCell >= 0)
                {
                    int row = ProgrammatorData.HoveredCell / ProgrammatorData.COLS;
                    int col = ProgrammatorData.HoveredCell % ProgrammatorData.COLS;
                    var cellCenter = _cells[row, col].worldBound.center;
                    _radial.ShowAt(_doc.rootVisualElement, cellCenter);
                    _radialShown = true;
                }
            }
            else
            {
                if (Keyboard.current.wKey.wasReleasedThisFrame)
                {
                    _radial.Hide();
                    _radialShown = false;
                }
            }
        }

        public void Show()
        {
            _isOpen = true;
            RefreshAllCells();
            _popup.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            _radial.Hide();
            _radialShown = false;
            _isOpen = false;
            _popup.style.display = DisplayStyle.None;
        }

        private void RefreshAllCells()
        {
            for (int i = 0; i < ProgrammatorData.ROWS; i++)
            {
                for (int j = 0; j < ProgrammatorData.COLS; j++)
                {
                    UpdateCell(i, j);
                }
            }
        }
    }
}
