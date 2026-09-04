#nullable enable

using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

internal sealed class MinimapView : IDisposable
{
    private readonly TemplateContainer _tree;
    private readonly VisualElement _root;
    private readonly Label _coordinates;
    private readonly StringBuilder _coordinatesBuilder = new(16);
    private int _lastDisplayedX = int.MinValue;
    private int _lastDisplayedY = int.MinValue;

    private MinimapView(
        TemplateContainer tree,
        VisualElement root,
        Label coordinates)
    {
        _tree = tree;
        _root = root;
        _coordinates = coordinates;
    }

    public static MinimapView Create(
        UIDocument document,
        Texture2D texture,
        Action openMap)
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/Minimap") ??
            throw new InvalidOperationException("[Minimap] Resources/UI/Minimap.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;
        VisualElement root = tree.Q<VisualElement>("MinimapPanel") ??
            throw new InvalidOperationException("[Minimap] MinimapPanel is missing from Minimap.uxml.");
        Label coordinates = tree.Q<Label>("MinimapCoordinates") ??
            throw new InvalidOperationException("[Minimap] MinimapCoordinates is missing from Minimap.uxml.");
        Image image = tree.Q<Image>("MinimapImage") ??
            throw new InvalidOperationException("[Minimap] MinimapImage is missing from Minimap.uxml.");
        image.image = texture;
        root.RegisterCallback<ClickEvent>(evt =>
        {
            openMap();
            evt.StopPropagation();
        });
        document.rootVisualElement.Add(tree);
        var view = new MinimapView(tree, root, coordinates);
        view.SetVisible(false);
        return view;
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
    }

    public void SetVisible(bool visible)
    {
        _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Dispose()
    {
        _tree.RemoveFromHierarchy();
    }
}
