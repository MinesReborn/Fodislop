#nullable enable

using UnityEngine;

namespace Kern.Game;

public class WorldEntitySpriteHandle
{
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;
    private Matrix4x4 _lastLocalToWorld = Matrix4x4.identity;
    private Sprite? _lastSprite;
    private Color _lastColor;
    private bool _lastEnabled;
    private bool _hasSnapshot;

    private Vector3 _framePosition;
    private Quaternion _frameRotation = Quaternion.identity;
    private Vector3 _frameScale = Vector3.one;
    private Matrix4x4 _frameLocalToWorld = Matrix4x4.identity;
    private bool _frameAlive;

    internal WorldEntitySpriteHandle(
        Transform transform,
        int sortingOrder,
        bool isStatic = false,
        bool emitsLight = false)
    {
        Transform = transform;
        SortingOrder = sortingOrder;
        IsStatic = isStatic;
        EmitsLight = emitsLight;
    }

    // The sprite is also a light source in the world lighting fields.
    internal bool EmitsLight { get; }

    internal Transform Transform { get; }

    internal int SortingOrder { get; }

    internal bool IsStatic { get; }

    internal Sprite? Sprite { get; private set; }

    internal Color Color { get; private set; } = Color.white;

    internal bool Enabled { get; private set; }

    public void SetSprite(Sprite? sprite)
    {
        Sprite = sprite;
        if (sprite == null)
        {
            Enabled = false;
        }

        if (IsStatic)
        {
            _hasSnapshot = false;
        }
    }

    public void SetColor(Color color)
    {
        Color = color;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled && Sprite != null;
        if (IsStatic)
        {
            _hasSnapshot = false;
        }
    }

    public void MarkTransformDirty()
    {
        _hasSnapshot = false;
    }

    internal bool FrameAlive => _frameAlive;

    internal Vector3 FramePosition => _framePosition;

    internal Matrix4x4 FrameLocalToWorld => _frameLocalToWorld;

    internal void RefreshFrameState()
    {
        if (Transform == null)
        {
            _frameAlive = false;
            return;
        }

        _frameAlive = true;

        // Неподвижному спрайту хватает снятого однажды снимка: его трансформ
        // не спрашивают вовсе, пока владелец сам не скажет, что тот сдвинулся.
        if (IsStatic && _hasSnapshot)
        {
            _framePosition = _lastPosition;
            _frameRotation = _lastRotation;
            _frameScale = _lastScale;
            _frameLocalToWorld = _lastLocalToWorld;
            return;
        }

        // Отключённый слот не рисуется и не отбирается в видимые: всё, от
        // чего зависит перестроение, — Enabled/Sprite/Color — лежит в
        // управляемой памяти и сверяется без единого вызова в движок.
        // Свежая матрица подтянется полным чтением в кадре включения:
        // смена Enabled всегда помечает хендл изменённым.
        if (!Enabled && _hasSnapshot)
        {
            _framePosition = _lastPosition;
            _frameRotation = _lastRotation;
            _frameScale = _lastScale;
            _frameLocalToWorld = _lastLocalToWorld;
            return;
        }

        _framePosition = Transform.position;
        _frameRotation = Transform.rotation;
        if (_hasSnapshot &&
            _framePosition == _lastPosition &&
            _frameRotation == _lastRotation)
        {
            // Позиция и поворот бит-в-бит те же, а цепочка родителей
            // статична (корни SceneObjectFactory создаются сценой и кодом
            // не двигаются): мировая матрица не могла измениться, и самый
            // дорогой вызов кадра — localToWorldMatrix — пропускается.
            _frameScale = Transform.lossyScale;
            if (_frameScale == _lastScale)
            {
                _frameLocalToWorld = _lastLocalToWorld;
                return;
            }
        }
        else
        {
            _frameScale = Transform.lossyScale;
        }

        _frameLocalToWorld = Transform.localToWorldMatrix;
    }

    internal Vector3 GetWorldPosition()
    {
        return _framePosition;
    }

    internal bool HasChanged()
    {
        if (!_frameAlive)
        {
            return _hasSnapshot;
        }

        if (!_hasSnapshot)
        {
            return true;
        }

        if (_lastEnabled != Enabled)
        {
            return true;
        }

        if (!Enabled)
        {
            return false;
        }

        if (_lastSprite != Sprite || _lastColor != Color)
        {
            return true;
        }

        if (IsStatic)
        {
            return false;
        }

        return _lastPosition != _framePosition ||
            _lastRotation != _frameRotation ||
            _lastScale != _frameScale;
    }

    internal void CaptureState()
    {
        if (!_frameAlive)
        {
            _lastPosition = Vector3.zero;
            _lastRotation = Quaternion.identity;
            _lastScale = Vector3.one;
        }
        else if (Enabled && (!IsStatic || !_hasSnapshot))
        {
            _lastPosition = _framePosition;
            _lastRotation = _frameRotation;
            _lastScale = _frameScale;
            _lastLocalToWorld = _frameLocalToWorld;
        }

        _lastSprite = Sprite;
        _lastColor = Color;
        _lastEnabled = Enabled;
        _hasSnapshot = true;
    }
}
