#nullable enable

using System;
using Fodinae.Core;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator
{
    /// <summary>
    /// 8-directional joystick for Observer operators.
    /// Direction buttons show operator icons. Click → absolute Cell*, drag → Shift*.
    /// Center shows Cell icon, drag → relative operators (Forward/Lefthand/Righthand short, Shift* long).
    /// Icons update during interaction to preview what will be placed.
    /// </summary>
    public class ObserverJoystick
    {
        private readonly VisualElement _root;
        private bool _isDragging;
        private bool _isActive;
        private int _activeSource = -1;
        private int _dragTargetDir = -1;
        private Vector2 _pointerStart;
        private const float DragThresh = 15f;
        private const float NearFarThresh = 40f;
        // Размер рута продублирован в .prog-joy-root (200px); константа нужна
        // для центрирования рута в ShowAt до первого лайаута.
        private const float RootSize = 200f;

        private readonly VisualElement?[] _dirItems = new VisualElement?[8];
        private readonly Label?[] _dirLabels = new Label?[8];
        private VisualElement? _centerItem;
        private Label? _centerLabel;

        // Pre-loaded textures
        private readonly Texture2D?[] _dirClickTex = new Texture2D?[8];
        private readonly Texture2D?[] _dirDragTex = new Texture2D?[8];
        private readonly Texture2D?[] _centerDragCellTex = new Texture2D?[8];
        private readonly Texture2D?[] _centerDragShiftTex = new Texture2D?[8];
        private Texture2D? _centerTex;

        public event System.Action<ProgAction>? OnOperatorSelected;

        // Direction button click → absolute Cell* (compass directions, N→clockwise)
        private static readonly ProgAction[] DirClickOps =
        {
            ProgAction.CellUp,        // 0  N
            ProgAction.CellUpRight,   // 1  NE
            ProgAction.CellRight,     // 2  E
            ProgAction.CellDownRight, // 3  SE
            ProgAction.CellDown,      // 4  S
            ProgAction.CellDownLeft,  // 5  SW
            ProgAction.CellLeft,      // 6  W
            ProgAction.CellUpLeft,    // 7  NW
        };

        // Direction button drag → absolute Shift* (compass directions)
        private static readonly ProgAction[] DirDragOps =
        {
            ProgAction.ShiftUp,        // 0  N
            ProgAction.ShiftRight,     // 1  NE
            ProgAction.ShiftRight,     // 2  E
            ProgAction.ShiftDown,      // 3  SE
            ProgAction.ShiftDown,      // 4  S
            ProgAction.ShiftLeft,      // 5  SW
            ProgAction.ShiftLeft,      // 6  W
            ProgAction.ShiftUp,        // 7  NW
        };

        // Center drag toward direction → (short=Cell* relative, long=Shift*)
        private static readonly (ProgAction cell, ProgAction shift)[] CenterDragOps =
        {
            (ProgAction.CellRighthand, ProgAction.ShiftRighthand),   // 0  N    → Righthand (swapped)
            (ProgAction.Cell,          ProgAction.ShiftUp),           // 1  NE   → ShiftUp
            (ProgAction.CellForward,   ProgAction.ShiftForward),     // 2  E    → Forward
            (ProgAction.Cell,          ProgAction.ShiftRight),       // 3  SE   → ShiftRight
            (ProgAction.CellLefthand,  ProgAction.ShiftLefthand),    // 4  S    → Lefthand (swapped)
            (ProgAction.Cell,          ProgAction.ShiftDown),        // 5  SW   → ShiftDown
            (ProgAction.Cell,          ProgAction.ShiftBackwards),   // 6  W    → Backwards
            (ProgAction.Cell,          ProgAction.ShiftLeft),        // 7  NW   → ShiftLeft
        };

        private static readonly ProgAction CenterClickOp = ProgAction.Cell;

        private static readonly string[] DirLabels =
        {
            "\u2191", "\u2197", "\u2192", "\u2198", "\u2193", "\u2199", "\u2190", "\u2196",
        };

        // Atan2 round value → our direction index lookup
        // raw: 0=E,1=NE,2=N,3=NW,4=W,5=SW,6=S,7=SE
        // ours: 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW
        private static readonly int[] _atan2ToDir = { 2, 1, 0, 7, 6, 5, 4, 3 };

        public VisualElement Root => _root;
        public bool IsShown => _root.parent != null;

        public ObserverJoystick(IProgrammatorTextureCatalog textures)
        {
            if (textures == null)
            {
                throw new ArgumentNullException(nameof(textures));
            }

            // Pre-load all textures
            for (int i = 0; i < 8; i++)
            {
                _dirClickTex[i] = textures.GetTexture(DirClickOps[i]);
                _dirDragTex[i] = textures.GetTexture(DirDragOps[i]);
                _centerDragCellTex[i] = textures.GetTexture(CenterDragOps[i].cell);
                _centerDragShiftTex[i] = textures.GetTexture(CenterDragOps[i].shift);
            }

            _centerTex = textures.GetTexture(CenterClickOp);

            // Статический скелет (рут, 8 кнопок направлений, центральная кнопка)
            // живёт в ObserverJoystick.uxml, геометрия — в .prog-joy-item--* / .prog-joy-center
            // (Programmator.uss). Здесь только клон и биндинги; иконки и ховер — динамика.
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.ObserverJoystickUxml) ??
                throw new InvalidOperationException(
                    "[ObserverJoystick] Resources/UI/ObserverJoystick.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("prog-joy-root");
            tree.pickingMode = PickingMode.Ignore;
            _root = tree;

            // Direction buttons
            for (int i = 0; i < 8; i++)
            {
                int idx = i;
                VisualElement item = _root.Q<VisualElement>($"JoyDir{i}") ??
                    throw new InvalidOperationException(
                        $"[ObserverJoystick] JoyDir{i} is missing from ObserverJoystick.uxml.");
                Label label = item.Q<Label>() ??
                    throw new InvalidOperationException(
                        $"[ObserverJoystick] JoyDir{i} label is missing from ObserverJoystick.uxml.");
                _dirItems[idx] = item;
                _dirLabels[idx] = label;

                // Set initial icon (click operator)
                SetItemIcon(item, label, _dirClickTex[idx], DirLabels[idx]);

                item.RegisterCallback<PointerDownEvent>(evt =>
                {
                    evt.StopPropagation();
                    BeginDrag(evt.position, idx);

                    // Icon stays as Cell* until actual drag movement
                });
            }

            // Center button
            _centerItem = _root.Q<VisualElement>("JoyCenter") ??
                throw new InvalidOperationException(
                    "[ObserverJoystick] JoyCenter is missing from ObserverJoystick.uxml.");
            _centerLabel = _centerItem.Q<Label>() ??
                throw new InvalidOperationException(
                    "[ObserverJoystick] JoyCenter label is missing from ObserverJoystick.uxml.");

            // Set initial center icon
            SetItemIcon(_centerItem, _centerLabel, _centerTex, "\u25CB");

            _centerItem.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopPropagation();
                BeginDrag(evt.position, 8);
            });

            // Root-level move: threshold + angle + icon preview
            _root.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isActive)
                {
                    return;
                }

                float dist = Vector2.Distance(evt.position, _pointerStart);

                // One-way drag latch for operator placement decision
                if (!_isDragging && dist >= DragThresh)
                {
                    _isDragging = true;
                }

                if (_activeSource == 8)
                {
                    // Center: update direction + icon preview
                    if (_isDragging)
                    {
                        float dx = evt.position.x - _pointerStart.x;
                        float dy = evt.position.y - _pointerStart.y;
                        float a = Mathf.Atan2(dy, dx);
                        if (a < 0)
                        {
                            a += Mathf.PI * 2f;
                        }

                        int raw = (int)Mathf.Round(a / (Mathf.PI / 4f)) % 8;
                        _dragTargetDir = _atan2ToDir[raw];

                        var ops = CenterDragOps[_dragTargetDir];
                        Texture2D? previewTex;
                        if (dist >= NearFarThresh && ops.shift != ProgAction.Cell)
                        {
                            previewTex = _centerDragShiftTex[_dragTargetDir];
                        }
                        else if (ops.cell != ProgAction.Cell && ops.cell != CenterClickOp)
                        {
                            previewTex = _centerDragCellTex[_dragTargetDir];
                        }
                        else
                        {
                            previewTex = null;
                        }

                        SetItemIcon(_centerItem, _centerLabel, previewTex ?? _centerTex, "\u25CB");
                    }
                }
                else if (_activeSource >= 0 && _activeSource < 8)
                {
                    // Direction button: icon based on CURRENT distance
                    // Shows Cell* near start, Shift* far — reverts when cursor returns
                    SetItemIcon(_dirItems[_activeSource], _dirLabels[_activeSource],
                        dist >= DragThresh ? _dirDragTex[_activeSource] : _dirClickTex[_activeSource],
                        DirLabels[_activeSource]);
                }
            });

            // Root-level up: resolve
            _root.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isActive)
                {
                    return;
                }

                _root.ReleasePointer(evt.pointerId);

                if (_activeSource == 8)
                {
                    if (_isDragging && _dragTargetDir >= 0)
                    {
                        float dist = Vector2.Distance(evt.position, _pointerStart);
                        var ops = CenterDragOps[_dragTargetDir];

                        if (dist >= NearFarThresh && ops.shift != ProgAction.Cell)
                        {
                            OnOperatorSelected?.Invoke(ops.shift);
                        }
                        else if (ops.cell != ProgAction.Cell && ops.cell != CenterClickOp)
                        {
                            OnOperatorSelected?.Invoke(ops.cell);
                        }
                    }
                    else
                    {
                        OnOperatorSelected?.Invoke(CenterClickOp);
                    }
                }
                else if (_activeSource >= 0 && _activeSource < 8)
                {
                    if (_isDragging)
                    {
                        OnOperatorSelected?.Invoke(DirDragOps[_activeSource]);
                    }
                    else
                    {
                        OnOperatorSelected?.Invoke(DirClickOps[_activeSource]);
                    }

                    // Restore direction icon to click operator
                    SetItemIcon(_dirItems[_activeSource], _dirLabels[_activeSource],
                        _dirClickTex[_activeSource], DirLabels[_activeSource]);
                }

                Reset();

                // Restore center icon
                SetItemIcon(_centerItem, _centerLabel, _centerTex, "\u25CB");
            });

            _root.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_isActive)
                {
                    // Restore all icons
                    for (int i = 0; i < 8; i++)
                    {
                        SetItemIcon(_dirItems[i], _dirLabels[i], _dirClickTex[i], DirLabels[i]);
                    }

                    SetItemIcon(_centerItem, _centerLabel, _centerTex, "\u25CB");
                    Reset();
                }
            });
        }

        private void BeginDrag(Vector2 position, int source)
        {
            _activeSource = source;
            _isActive = true;
            _isDragging = false;
            _dragTargetDir = -1;
            _pointerStart = position;
            _root.CapturePointer(0);
        }

        private void Reset()
        {
            _isActive = false;
            _isDragging = false;
            _activeSource = -1;
            _dragTargetDir = -1;
        }

        private static void SetItemIcon(VisualElement? item, Label? label, Texture2D? tex, string fallback)
        {
            if (item == null || label == null)
            {
                return;
            }

            // Remove any existing Image child
            for (int i = item.childCount - 1; i >= 0; i--)
            {
                if (item[i] is Image)
                {
                    item.RemoveAt(i);
                }
            }

            if (tex != null)
            {
                var img = new Image();
                img.image = tex;
                img.scaleMode = ScaleMode.ScaleToFit;
                img.AddToClassList("prog-radial-fill");
                img.pickingMode = PickingMode.Ignore;
                item.Add(img);
                label.text = string.Empty;
            }
            else
            {
                label.text = fallback;
            }
        }


        public void ShowAt(VisualElement parent, Vector2 screenPos)
        {
            Reset();

            // Restore default icons
            for (int i = 0; i < 8; i++)
            {
                SetItemIcon(_dirItems[i], _dirLabels[i], _dirClickTex[i], DirLabels[i]);
            }

            SetItemIcon(_centerItem, _centerLabel, _centerTex, "\u25CB");

            _root.style.left = screenPos.x - (RootSize / 2f);
            _root.style.top = screenPos.y - (RootSize / 2f);
            parent.Add(_root);
        }

        public void Hide()
        {
            Reset();
            if (_root.parent != null)
            {
                _root.RemoveFromHierarchy();
            }
        }
    }
}
