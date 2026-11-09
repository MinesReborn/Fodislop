#nullable enable

using System;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Screens;

namespace Kern.UI.Map;

/// <summary>Owns WorldMap's UI Toolkit bindings and presentation-only panel state.</summary>
internal sealed class WorldMapPanel : IDisposable
{
    private const float PositionWriteEpsilon = 0.5f;

    private UIDocument? _document;
    private VisualElement? _overlay;
    private VisualElement? _playerMarker;
    private Image? _image;
    private Image? _pathOverlay;
    private Button? _closeButton;
    private Button? _followButton;
    private EventCallback<WheelEvent>? _wheelCallback;
    private EventCallback<GeometryChangedEvent>? _geometryCallback;
    private Action _closeRequested = null!;
    private Action _followPlayer = null!;
    private Action? _geometryChanged;
    private bool _bindingFailureReported;
    private float _lastMarkerLeft = float.MinValue;
    private float _lastMarkerTop = float.MinValue;

    // Плашка координат в шапке (макет .map-coords): клетка под курсором, вне карты — позиция игрока
    private Label? _coords;
    private Label? _scale;
    private float _shownScale = -1f;
    private EventCallback<PointerMoveEvent>? _pointerMoveCallback;
    private EventCallback<PointerLeaveEvent>? _pointerLeaveCallback;
    private Vector2Int? _playerCell;
    private Vector2Int? _shownCell;
    private float _viewCenterX;
    private float _viewCenterY;
    private float _cellsPerPixel;
    private int _texWidth;
    private int _texHeight;

    // «Моя метка» (макет #mapMark): инструмент включает установку, следующий клик по карте ставит метку
    // Создаётся при привязке: панель живёт в поле MonoBehaviour, а VisualElement нельзя создавать в его конструкторе
    private WorldMapGridLayer? _grid;
    private Button? _markButton;
    private VisualElement? _markPin;
    private Vector2Int? _mark;

    private Button? _zoomInButton;
    private Button? _zoomOutButton;
    private Action? _zoomIn;
    private Action? _zoomOut;

    public VisualElement? Overlay => _overlay;

    public Image? Image => _image;

    /// <summary>Прозрачный слой поверх карты: нить клик-маршрута.</summary>
    public Image? PathOverlay => _pathOverlay;

    public bool IsBound => _overlay != null && _image != null;

    public bool IsDocumentDisabled => _document != null && !_document.enabled;

    public bool IsMarking { get; private set; }

    public bool TryBind(
        UIDocument? document,
        Action closeRequested,
        Action followPlayer,
        Action zoomIn,
        Action zoomOut,
        EventCallback<WheelEvent> wheelCallback,
        Action? geometryChanged = null)
    {
        if (IsBound)
        {
            return true;
        }

        _document = document;
        if (_document == null)
        {
            ReportBindingFailure("World map panel cannot bind because its UIDocument was not injected.");
            return false;
        }

        if (_document.rootVisualElement.panel == null)
        {
            // Панель ещё не привязана к UI Toolkit; WorldMapRenderer подписывается на AttachToPanelEvent и повторит привязку.
            return false;
        }

        VisualElement? overlay = _document.rootVisualElement.Q<VisualElement>("WorldMapOverlay");
        if (overlay == null)
        {
            ReportBindingFailure("World map panel cannot bind because WorldMapOverlay is missing from its UIDocument.");
            return false;
        }

        Image? image = overlay.Q<Image>("WorldMapImage");
        Button? closeButton = overlay.Q<Button>("WorldMapCloseButton");
        Button? followButton = overlay.Q<Button>("WorldMapFollowPlayerButton");
        Button? zoomInButton = overlay.Q<Button>("WorldMapZoomInButton");
        Button? zoomOutButton = overlay.Q<Button>("WorldMapZoomOutButton");
        Label? coords = overlay.Q<Label>("WorldMapCoords");
        Label? scale = overlay.Q<Label>("WorldMapScale");
        Button? markButton = overlay.Q<Button>("WorldMapMarkButton");
        VisualElement? markPin = overlay.Q<VisualElement>("WorldMapMarkPin");
        if (image == null || closeButton == null || followButton == null ||
            zoomInButton == null || zoomOutButton == null || coords == null || scale == null ||
            markButton == null || markPin == null)
        {
            ReportBindingFailure(
                "World map panel cannot bind because a required map control is missing.");
            return false;
        }

        _overlay = overlay;
        _playerMarker = overlay.Q<VisualElement>("WorldMapPlayerMarker");
        _image = image;
        _closeButton = closeButton;
        _followButton = followButton;
        _coords = coords;
        _scale = scale;
        _markButton = markButton;
        _markPin = markPin;
        _markButton.clicked += ToggleMarking;
        _image.image = null;
        _pointerMoveCallback = evt => ShowCellUnderPointer(evt.localPosition);
        _pointerLeaveCallback = _ => ShowCell(_playerCell);
        _image.RegisterCallback(_pointerMoveCallback);
        _image.RegisterCallback(_pointerLeaveCallback);

        // Прозрачный слой поверх карты: нить клик-маршрута рисуется в отдельной
        // текстуре, чтобы не вмешиваться в инкрементальный рендер самой карты.
        _pathOverlay = new Image { name = "WorldMapPathOverlay", pickingMode = PickingMode.Ignore };
        _pathOverlay.style.position = Position.Absolute;
        _pathOverlay.style.left = 0f;
        _pathOverlay.style.top = 0f;
        _pathOverlay.style.width = Length.Percent(100f);
        _pathOverlay.style.height = Length.Percent(100f);
        image.Add(_pathOverlay);
        _grid = new WorldMapGridLayer();
        image.Add(_grid.Element);

        _closeRequested = closeRequested;
        _followPlayer = followPlayer;
        _closeButton.clicked += _closeRequested;
        _followButton.clicked += _followPlayer;
        _zoomInButton = zoomInButton;
        _zoomOutButton = zoomOutButton;
        _zoomIn = zoomIn;
        _zoomOut = zoomOut;
        _zoomInButton.clicked += _zoomIn;
        _zoomOutButton.clicked += _zoomOut;
        _wheelCallback = wheelCallback;
        _geometryChanged = geometryChanged;
        if (_geometryChanged != null)
        {
            _geometryCallback = _ => _geometryChanged();
            _image.RegisterCallback(_geometryCallback);
        }

        _bindingFailureReported = false;
        _document.rootVisualElement.RegisterCallback(
            _wheelCallback,
            TrickleDown.TrickleDown);
        return true;
    }

    public void Show()
    {
        if (_overlay != null)
        {
            UIState.Show(_overlay);
        }
    }

    public void Hide()
    {
        if (_overlay != null)
        {
            UIState.Hide(_overlay);
        }
    }

    public void UpdatePlayerMarker(
        ILocalPlayer? player,
        float viewCenterX,
        float viewCenterY,
        float cellsPerPixel,
        int texWidth,
        int texHeight,
        bool blinkVisible)
    {
        if (player is { HasServerPosition: true })
        {
            Vector2Int position = player.Position;
            if (_playerCell != position)
            {
                bool followingPlayer = _shownCell == _playerCell;
                _playerCell = position;
                if (followingPlayer)
                {
                    ShowCell(position);
                }
            }

            UpdatePlayerMarker(
                position.x,
                position.y,
                viewCenterX,
                viewCenterY,
                cellsPerPixel,
                texWidth,
                texHeight,
                blinkVisible);
        }
        else
        {
            HidePlayerMarker();
        }
    }

    public void UpdatePlayerMarker(
        float playerX,
        float playerY,
        float viewCenterX,
        float viewCenterY,
        float cellsPerPixel,
        int texWidth,
        int texHeight,
        bool blinkVisible)
    {
        // Вид карты нужен и плашке координат: по нему клетка под курсором
        _viewCenterX = viewCenterX;
        _viewCenterY = viewCenterY;
        _cellsPerPixel = cellsPerPixel;
        _texWidth = texWidth;
        _texHeight = texHeight;
        ShowScale();
        _grid?.SetView(viewCenterX, viewCenterY, cellsPerPixel, texWidth, texHeight);
        PlaceMarkPin();

        if (_playerMarker == null)
        {
            return;
        }

        if (!blinkVisible || (_image != null && (_image.layout.width <= 0f || _image.layout.height <= 0f)))
        {
            UIState.SetHidden(_playerMarker, true);
            return;
        }

        float halfW = texWidth * 0.5f * cellsPerPixel;
        float halfH = texHeight * 0.5f * cellsPerPixel;
        float leftX = viewCenterX - halfW;
        float rightX = viewCenterX + halfW;
        float topServerY = viewCenterY - halfH;
        float bottomServerY = viewCenterY + halfH;

        if (playerX + 1f < leftX || playerX > rightX ||
            playerY + 1f < topServerY || playerY > bottomServerY)
        {
            UIState.SetHidden(_playerMarker, true);
            return;
        }

        UIState.SetHidden(_playerMarker, false);

        // The map RenderTexture is capped (MapViewportBounds) and the Image scales
        // it to the panel, so texture pixels must be mapped to panel pixels.
        float texLeft = (playerX + 0.5f - viewCenterX) / cellsPerPixel + texWidth * 0.5f;
        float texTop = (playerY + 0.5f - viewCenterY) / cellsPerPixel + texHeight * 0.5f;

        Rect imageRect = _image?.layout ?? new Rect(0f, 0f, texWidth, texHeight);
        float scaleX = imageRect.width > 0f && texWidth > 0 ? imageRect.width / texWidth : 1f;
        float scaleY = imageRect.height > 0f && texHeight > 0 ? imageRect.height / texHeight : 1f;
        float screenX = imageRect.x + (texLeft * scaleX);
        float screenY = imageRect.y + (texTop * scaleY);

        if (Mathf.Abs(screenX - _lastMarkerLeft) > PositionWriteEpsilon ||
            Mathf.Abs(screenY - _lastMarkerTop) > PositionWriteEpsilon)
        {
            _playerMarker.style.left = screenX;
            _playerMarker.style.top = screenY;
            _lastMarkerLeft = screenX;
            _lastMarkerTop = screenY;
        }
    }

    private void ToggleMarking() => SetMarking(!IsMarking);

    private void SetMarking(bool on)
    {
        IsMarking = on;
        _markButton?.EnableInClassList("is-pressed", on);
    }

    // Клик по карте в режиме установки: метка встаёт на клетку, режим выключается
    public void PlaceMark(Vector2Int cell)
    {
        _mark = cell;
        SetMarking(false);
        PlaceMarkPin();
    }

    private void PlaceMarkPin()
    {
        if (_markPin == null || _image == null)
        {
            return;
        }

        Rect imageRect = _image.layout;
        if (_mark is not Vector2Int mark || _cellsPerPixel <= 0f || _texWidth <= 0 || _texHeight <= 0 ||
            imageRect.width <= 0f || imageRect.height <= 0f)
        {
            UIState.SetHidden(_markPin, true);
            return;
        }

        float texX = (mark.x + 0.5f - _viewCenterX) / _cellsPerPixel + _texWidth * 0.5f;
        float texY = (mark.y + 0.5f - _viewCenterY) / _cellsPerPixel + _texHeight * 0.5f;
        float x = imageRect.x + texX * imageRect.width / _texWidth;
        float y = imageRect.y + texY * imageRect.height / _texHeight;
        bool inside = x >= imageRect.xMin && x <= imageRect.xMax && y >= imageRect.yMin && y <= imageRect.yMax;
        UIState.SetHidden(_markPin, !inside);
        if (inside)
        {
            _markPin.style.left = x;
            _markPin.style.top = y;
        }
    }

    // Клетка под курсором: панель → пиксель текстуры карты → клетка сервера (обратное к положению метки игрока)
    private void ShowCellUnderPointer(Vector2 local)
    {
        if (_image == null || _cellsPerPixel <= 0f || _texWidth <= 0 || _texHeight <= 0 ||
            _image.layout.width <= 0f || _image.layout.height <= 0f)
        {
            return;
        }

        float texX = local.x * _texWidth / _image.layout.width;
        float texY = local.y * _texHeight / _image.layout.height;
        int cellX = Mathf.FloorToInt(_viewCenterX + (texX - _texWidth * 0.5f) * _cellsPerPixel);
        int cellY = Mathf.FloorToInt(_viewCenterY + (texY - _texHeight * 0.5f) * _cellsPerPixel);
        ShowCell(new Vector2Int(cellX, cellY));
    }

    // «×8»: сколько пикселей экрана приходится на клетку при текущем масштабе
    private void ShowScale()
    {
        if (_scale == null || _image == null || _cellsPerPixel <= 0f || _texWidth <= 0 || _image.layout.width <= 0f)
        {
            return;
        }

        float pixelsPerCell = _image.layout.width / _texWidth / _cellsPerPixel;
        float rounded = pixelsPerCell >= 1f ? Mathf.Round(pixelsPerCell) : Mathf.Round(pixelsPerCell * 100f) / 100f;
        if (Mathf.Approximately(rounded, _shownScale))
        {
            return;
        }

        _shownScale = rounded;
        _scale.text = "×" + rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ShowCell(Vector2Int? cell)
    {
        if (_coords == null || cell == _shownCell)
        {
            return;
        }

        _shownCell = cell;
        _coords.text = cell is Vector2Int c ? $"{c.x}:{c.y}" : string.Empty;
        _coords.EnableInClassList("is-empty", cell == null);
    }

    public void HidePlayerMarker()
    {
        if (_playerMarker != null)
        {
            UIState.SetHidden(_playerMarker, true);
        }
    }

    public void Dispose()
    {
        if (_image != null && _geometryCallback != null)
        {
            _image.UnregisterCallback(_geometryCallback);
            _geometryCallback = null;
        }

        if (_document?.rootVisualElement != null && _wheelCallback != null)
        {
            _document.rootVisualElement.UnregisterCallback(
                _wheelCallback,
                TrickleDown.TrickleDown);
        }

        if (_image != null && _pointerMoveCallback != null && _pointerLeaveCallback != null)
        {
            _image.UnregisterCallback(_pointerMoveCallback);
            _image.UnregisterCallback(_pointerLeaveCallback);
        }

        if (_closeButton != null)
        {
            _closeButton.clicked -= _closeRequested;
        }

        if (_followButton != null)
        {
            _followButton.clicked -= _followPlayer;
        }

        if (_markButton != null)
        {
            _markButton.clicked -= ToggleMarking;
        }

        if (_zoomInButton != null)
        {
            _zoomInButton.clicked -= _zoomIn;
        }

        if (_zoomOutButton != null)
        {
            _zoomOutButton.clicked -= _zoomOut;
        }

        _grid?.Element.RemoveFromHierarchy();
        _grid = null;
        _pathOverlay?.RemoveFromHierarchy();
        _pathOverlay = null;
    }

    private void ReportBindingFailure(string message)
    {
        if (_bindingFailureReported)
        {
            return;
        }

        _bindingFailureReported = true;
        Debug.LogError(message);
    }
}
