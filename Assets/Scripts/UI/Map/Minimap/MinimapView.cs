#nullable enable

using System;
using System.Text;
using Kern.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MinimapView : IDisposable
{
    private readonly TemplateContainer _tree;
    private readonly VisualElement _root;
    private readonly Label _coordinates;
    private readonly Image _image;
    private readonly StringBuilder _coordinatesBuilder = new(16);
    private int _lastDisplayedX = int.MinValue;
    private int _lastDisplayedY = int.MinValue;

    private MinimapView(
        TemplateContainer tree,
        VisualElement root,
        Label coordinates,
        Image image)
    {
        _tree = tree;
        _root = root;
        _coordinates = coordinates;
        _image = image;
    }

    public static MinimapView Create(
        UIDocument document,
        Texture texture,
        Texture pathOverlay,
        Action<int, int> moveRequested)
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
            ProjectRuntimeContracts.ResourcePaths.MinimapUxml) ??
            throw new InvalidOperationException("[Minimap] Resources/UI/Gameplay/Minimap.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;
        VisualElement root = tree.Q<VisualElement>("MinimapPanel") ??
            throw new InvalidOperationException("[Minimap] MinimapPanel is missing from Minimap.uxml.");
        root.pickingMode = PickingMode.Position;
        Label coordinates = tree.Q<Label>("MinimapCoordinates") ??
            throw new InvalidOperationException("[Minimap] MinimapCoordinates is missing from Minimap.uxml.");
        Image image = tree.Q<Image>("MinimapImage") ??
            throw new InvalidOperationException("[Minimap] MinimapImage is missing from Minimap.uxml.");
        image.image = texture;

        // Остаток клик-маршрута поверх клеток: прозрачная текстура того же
        // размера, растянутая на картинку. Клики проходят сквозь неё.
        var pathImage = new Image
        {
            name = "MinimapPathOverlay",
            image = pathOverlay,
            pickingMode = PickingMode.Ignore,
            scaleMode = ScaleMode.StretchToFill,
        };
        pathImage.style.position = Position.Absolute;
        pathImage.style.left = 0;
        pathImage.style.top = 0;
        pathImage.style.right = 0;
        pathImage.style.bottom = 0;
        image.Add(pathImage);

        // Клик по блоку миникарты — движение к этому блоку (та же логика, что
        // у ЛКМ по миру). Здесь вычисляется только пиксель текстуры; конвертацию
        // в серверную клетку относительно центра (робота) делает контроллер.
        image.RegisterCallback<ClickEvent>(evt =>
        {
            Rect bound = image.worldBound;
            if (bound.width <= 0f || bound.height <= 0f)
            {
                return;
            }

            // localPosition — позиция указателя в системе координат картинки.
            Vector2 local = evt.localPosition;
            float relX = local.x / bound.width;
            float relY = local.y / bound.height;
            if (relX < 0f || relX >= 1f || relY < 0f || relY >= 1f)
            {
                return;
            }

            // Строка 0 текстуры рисуется внизу элемента, поэтому экранная ось Y
            // инвертируется: верх элемента — последняя строка текстуры.
            int texX = Mathf.Clamp(Mathf.FloorToInt(relX * texture.width), 0, texture.width - 1);
            int texY = Mathf.Clamp(Mathf.FloorToInt((1f - relY) * texture.height), 0, texture.height - 1);

            moveRequested(texX, texY);
            evt.StopPropagation();
        });
        root.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        document.rootVisualElement.Add(tree);
        var view = new MinimapView(tree, root, coordinates, image);
        view.SetVisible(false);
        return view;
    }

    /// <summary>
    /// Просит перерисовать текстуру миникарты. Текстура пишется на месте, без
    /// смены ссылки, поэтому элемент нужно явно пометить грязным.
    /// </summary>
    public void MarkDirty()
    {
        _image.MarkDirtyRepaint();
        foreach (VisualElement child in _image.Children())
        {
            child.MarkDirtyRepaint();
        }
    }

    public void UpdateCoordinates(int x, int y)
    {
        if (_lastDisplayedX == x && _lastDisplayedY == y)
        {
            return;
        }

        _lastDisplayedX = x;
        _lastDisplayedY = y;
        _coordinatesBuilder.Clear();
        _coordinatesBuilder.Append(x).Append(':').Append(y);
        _coordinates.text = _coordinatesBuilder.ToString();

        // Та же перераскладка, что у счётчика FPS: «9:12» уже, чем «128:340».
        FPSCounter.HoldWidth(_coordinates, ref _widestCoordinates);
    }

    private float _widestCoordinates;

    public void SetVisible(bool visible)
    {
        _tree.pickingMode = PickingMode.Ignore;
        _root.pickingMode = PickingMode.Position;
        UIState.SetHidden(_root, !visible);
    }

    public void Dispose()
    {
        _tree.RemoveFromHierarchy();
    }
}
