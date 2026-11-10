#nullable enable

using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Lighting;
using Kern.World.Lighting;

namespace Kern.Game.Visual.Effects.Sprites;

internal sealed class SpriteAnimationWorldEffect : IServerWorldEffect
{
    private readonly SpriteAnimationLibrary _library;
    private readonly SpriteAnimationLibrary.Clip _clip;
    private readonly GameObject _object;
    private readonly MeshFilter _filter;
    private readonly MeshRenderer _renderer;
    private readonly MaterialPropertyBlock _properties = new();
    private readonly double _createdAt;
    private readonly Transform? _follow;
    private readonly Vector3 _sourcePosition;
    private readonly Vector3 _destinationPosition;
    private readonly bool _hasDestination;
    private readonly Vector2 _drift;
    private double _expiresAt;
    private readonly bool _hasFollow;
    private readonly int _boomType;
    private int _frame = -1;
    private bool _isHeld;
    private readonly TransientEffectLight? _light;

    internal SpriteAnimationWorldEffect(string id, Vector3 position, SpriteAnimationLibrary library,
        ISceneObjectFactory objects, Transform? follow = null, int boomType = 0,
        Vector3? destinationPosition = null, LightingEngine? lighting = null)
    {
        _library = library;
        _clip = library.Get(id);
        _sourcePosition = position;
        _destinationPosition = destinationPosition.GetValueOrDefault(position);
        _hasDestination = destinationPosition.HasValue;
        _follow = follow;
        _hasFollow = follow != null;
        _boomType = boomType;
        _createdAt = Time.timeAsDouble;
        Refresh();
        if (id == "RobotSmoke")
        {
            float angle = Random.value * Mathf.PI * 2f;
            _drift = new Vector2(Mathf.Cos(angle) * 0.16f, -(Mathf.Sin(angle) * 0.01f - 0.11875f));
        }
        else if (id is "Smoke0" or "Smoke1" or "Smoke2")
        {
            float angle = Random.value * Mathf.PI * 2f;
            float speed = Random.value * 2f;
            _drift = new Vector2(2f * speed * Mathf.Cos(angle), speed * Mathf.Sin(angle));
        }
        _object = objects.Create("SpriteAnimations/" + id, RuntimeOwner.VisualEffects);
        _object.transform.SetPositionAndRotation(position, Quaternion.identity);
        _object.transform.localScale = _clip.Scale;
        if (follow != null)
        {
            _object.transform.SetParent(follow, worldPositionStays: false);
            _object.transform.localPosition = Vector3.zero;
            _object.transform.localRotation = Quaternion.identity;
        }
        _filter = _object.AddComponent<MeshFilter>();
        _renderer = _object.AddComponent<MeshRenderer>();
        _renderer.sortingOrder = _clip.SortingOrder;
        if (id is "Heal" or "Hurt")
        {
            _light = new TransientEffectLight(lighting ?? throw new System.ArgumentNullException(nameof(lighting)));
        }
        ApplyColor(_clip.Color);
        Update();
    }

    public bool IsDisposed { get; private set; }

    internal void Refresh() => _expiresAt = Time.timeAsDouble + (_clip.Id == "Heal" ? 17.0 / 30.0 : _clip.Duration);

    internal void Hold() => _isHeld = true;

    public void Update()
    {
        if (IsDisposed) { return; }
        float age = (float)(Time.timeAsDouble - _createdAt);
        if ((!_isHeld && Time.timeAsDouble >= _expiresAt) || (_hasFollow && _follow == null)) { Dispose(); return; }
        float clipAge = _clip.Loop ? age % _clip.Duration : age;
        int index = _clip.Frames.Length - 1;
        while (index > 0 && clipAge < _clip.Frames[index].Time) { index--; }
        if (_frame != index)
        {
            _frame = index;
            int mesh = _clip.Frames[index].Mesh;
            _filter.sharedMesh = _library.MeshAt(mesh);
            _renderer.sharedMaterial = _library.MaterialAt(mesh);
        }

        if (_light != null)
        {
            SpriteEmissionFrame frame = _library.EmissionAt(_clip.Frames[index].Mesh);
            Color emission = frame.Radiance;
            emission = new Color(emission.r * _clip.Color.r, emission.g * _clip.Color.g,
                emission.b * _clip.Color.b, 1f) * _clip.Color.a;
            _light.Submit(_object.transform.TransformPoint(new Vector3(frame.Centroid.x, frame.Centroid.y, 0f)),
                _object.transform.lossyScale, emission);
        }

        if (_clip.Id == "RobotSmoke")
        {
            // SmokeParticle applies displacement/growth on alternate 30 Hz ticks.
            float steps = age * 15f;
            _object.transform.position = _sourcePosition + new Vector3(_drift.x * steps, _drift.y * steps + 0.0025f * steps * (steps - 1f) * 0.5f, 0f);
            _object.transform.localScale = _clip.Scale * Mathf.Pow(1.07f, steps);
        }
        else if (_clip.Id is "Smoke0" or "Smoke1" or "Smoke2")
        {
            _object.transform.position = _sourcePosition + new Vector3(_drift.x * age, _drift.y * age, 0f);
        }
        else if (_clip.Id == "Arrow" && _hasDestination)
        {
            float progress = Mathf.Clamp01(age / _clip.Duration);
            Vector3 displacement = _destinationPosition - _sourcePosition;
            _object.transform.position = Vector3.Lerp(_sourcePosition, _destinationPosition, progress);
            if (displacement.sqrMagnitude > 0f)
            {
                _object.transform.rotation = Quaternion.FromToRotation(Vector3.up, displacement.normalized);
            }
        }

        if (_clip.Id == "Boom")
        {
            float t = age / _clip.Duration;
            float scale = BoomScaleAt(t) * BoomSize(_boomType);
            _object.transform.localScale = new Vector3(scale, scale, 1f);
            Color color = BoomColor(_boomType);
            color.a = Mathf.Min(1f, 2f * (1f - t));
            ApplyColor(color);
        }
    }

    private void ApplyColor(Color color)
    {
        _properties.SetColor("_EffectColor", color);
        _renderer.SetPropertyBlock(_properties);
    }

    // Sample the authored per-frame growth at a reference 60 Hz by elapsed
    // time; rendering at another FPS cannot accelerate the recurrence.
    private static float BoomScaleAt(float normalizedAge)
    {
        float referenceFrame = normalizedAge * 30f;
        int complete = Mathf.FloorToInt(referenceFrame);
        float scale = 0;
        for (int frame = 1; frame <= complete; frame++) { scale = Mathf.Lerp(scale, 1f, frame / 30f); }
        float next = Mathf.Lerp(scale, 1f, (complete + 1f) / 30f);
        return Mathf.Lerp(scale, next, referenceFrame - complete);
    }

    private static float BoomSize(int type) => type switch { 0 => 0.875f, 1 => 0.375f, 2 => 2.375f, _ => throw new System.ArgumentOutOfRangeException(nameof(type)) };
    private static Color BoomColor(int type) => type switch { 0 => new Color(1f, Mathf.GammaToLinearSpace(0.8f), 0f), 1 => new Color(1f, Mathf.GammaToLinearSpace(0.2f), 1f), 2 => Color.cyan, _ => throw new System.ArgumentOutOfRangeException(nameof(type)) };

    public void Dispose()
    {
        if (IsDisposed) { return; }
        IsDisposed = true;
        _light?.Dispose();
        if (_renderer != null) { _renderer.enabled = false; }
        if (_object != null) { Object.Destroy(_object); }
    }
}
