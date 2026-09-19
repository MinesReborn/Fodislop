#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Kern.UI;

public sealed class WorldLabels(UIDocument document, IGameplayCamera camera) : IWorldLabels, ILateTickable, IDisposable
{
    private readonly List<Entry> _entries = [];
    private VisualElement? _root;
    private VisualElement? _container;

    public IWorldLabel Create(bool chatBubble)
    {
        if (_root == null)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>("UI/WorldLabels")
                ?? throw new InvalidOperationException("Missing UI/WorldLabels.");
            _root = template.CloneTree();
            _root.AddToClassList("world-labels");
            _root.pickingMode = PickingMode.Ignore;
            document.rootVisualElement.Insert(0, _root);
            _container = _root.Q("WorldLabels");
        }

        // Labels are a dynamic collection, not static screen structure.
        var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
        label.AddToClassList(chatBubble ? "world-label-chat" : "world-label-name");
        _container!.Add(label);
        var entry = new Entry(this, label);
        _entries.Add(entry);
        return entry;
    }

    public void LateTick()
    {
        if (!document.enabled || _root?.panel == null)
        {
            return;
        }

        // Камера принадлежит Bootstrap и может быть уничтожена раньше этой сцены:
        // порядок разрушения сцен при выходе и в тестах не гарантирован.
        Camera? view = camera.Camera;
        if (view == null)
        {
            return;
        }

        foreach (Entry entry in _entries)
        {
            Vector3 viewport = view.WorldToViewportPoint(entry.Position);
            bool visible = entry.Visible && viewport.z > 0f &&
                viewport.x >= -0.15f && viewport.x <= 1.15f &&
                viewport.y >= -0.15f && viewport.y <= 1.15f;
            if (!visible)
            {
                entry.ApplyHidden();
                continue;
            }

            Vector2 panelPosition = RuntimePanelUtils.CameraTransformWorldToPanel(
                _root.panel, entry.Position, view);
            Vector2 local = _container!.WorldToLocal(panelPosition);
            entry.ApplyVisible(new Vector3(local.x, local.y, 0f));
        }
    }

    public void Dispose()
    {
        _entries.Clear();
        _root?.RemoveFromHierarchy();
        _root = null;
        _container = null;
    }

    private sealed class Entry(WorldLabels owner, Label label) : IWorldLabel
    {
        private const float PositionApplyEpsilonPx = 0.5f;

        public Label Label { get; } = label;
        public Vector3 Position { get; private set; }
        public bool Visible { get; private set; } = true;

        private Vector3 _lastAppliedPosition;
        private bool _lastAppliedVisible;
        private bool _hasApplied;

        public void SetText(string text) => Label.text = text;
        public void SetPosition(Vector3 position) => Position = position;
        public void SetVisible(bool visible) => Visible = visible;
        public void SetOpacity(float opacity) => Label.style.opacity = Mathf.Clamp01(opacity);

        // Запись в style.translate помечает стили элемента грязными без
        // сравнения значений, поэтому безусловная запись каждый кадр держала
        // всю панель в состоянии style-dirty: дерево пересчитывало стили,
        // раскладку и перекраску, даже когда метки стояли на месте. Пишем
        // только при смене видимости или сдвиге сверх половины пикселя —
        // тем же приёмом, что MissionArrowUI.
        public void ApplyVisible(Vector3 position)
        {
            bool visibilityChanged = !_hasApplied || !_lastAppliedVisible;
            bool positionChanged = !_hasApplied ||
                (position - _lastAppliedPosition).sqrMagnitude >
                    PositionApplyEpsilonPx * PositionApplyEpsilonPx;
            if (!visibilityChanged && !positionChanged)
            {
                return;
            }

            UIState.SetHidden(Label, false);
            if (positionChanged)
            {
                Label.style.translate = new Translate(position.x, position.y);
            }

            _lastAppliedVisible = true;
            _lastAppliedPosition = position;
            _hasApplied = true;
        }

        public void ApplyHidden()
        {
            if (_hasApplied && !_lastAppliedVisible)
            {
                return;
            }

            UIState.SetHidden(Label, true);
            _lastAppliedVisible = false;
            _hasApplied = true;
        }

        public void Dispose()
        {
            owner._entries.Remove(this);
            Label.RemoveFromHierarchy();
        }
    }
}
