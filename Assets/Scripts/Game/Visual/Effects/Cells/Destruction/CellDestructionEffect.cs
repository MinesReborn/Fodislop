#nullable enable

using System;
using Kern.Game.Visual.Effects.Lighting;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using MinesServer.Data;
using UnityEngine;

using Kern.Game.Visual.Effects.Pooling;

namespace Kern.Game.Visual.Effects.Cells;

// D and G share their original sprite animation. Sound remains in the audio packet.
public sealed class CellDestructionEffect : IServerWorldEffect
{
    private readonly CellDestructionClip _clip;
    private readonly LightingEngine _lighting;
    private readonly int _lightId;
    private readonly double _createdAtSeconds;
    private readonly GameObject _object;
    private readonly SpriteRenderer _renderer;
    private float _elapsedSeconds;
    private int _frameIndex;

    public CellDestructionEffect(AudioEventType variant, Vector3 position,
        CellDestructionClip clip, ISceneObjectFactory objects, LightingEngine lighting)
    {
        if (!IsCellDestruction(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        _clip = clip;
        _lighting = lighting;
        _lightId = TransientEffectLight.AllocateId();
        _createdAtSeconds = Time.timeAsDouble;
        _object = objects.Create(variant == AudioEventType.Destroy ? "CellDestructionD" : "CellDestructionG", RuntimeOwner.VisualEffects);
        _object.transform.SetPositionAndRotation(position, Quaternion.identity);
        _object.transform.localScale = Vector3.one;
        _renderer = _object.AddComponent<SpriteRenderer>();
        _renderer.sharedMaterial = clip.Material;
        _renderer.sortingOrder = clip.SortingOrder;
        _renderer.color = Color.white;
        _renderer.sprite = clip.Frames[0];
        SubmitEmission();
    }

    public bool IsDisposed { get; private set; }
    internal SpriteRenderer Renderer => _renderer;

    public static bool IsCellDestruction(AudioEventType effect) => effect is AudioEventType.Destroy or AudioEventType.Geology;

    public void Update() => SetElapsed((float)(Time.timeAsDouble - _createdAtSeconds));

    internal void Advance(float deltaTimeSeconds) => SetElapsed(_elapsedSeconds + deltaTimeSeconds);

    private void SetElapsed(float elapsedSeconds)
    {
        if (IsDisposed)
        {
            return;
        }

        // The packet can arrive partway through a frame. Counting the whole
        // frame's delta on birth would age the effect before it ever rendered.
        _elapsedSeconds = elapsedSeconds;
        int frameIndex = _clip.FrameIndexAt(_elapsedSeconds);
        if (frameIndex < 0)
        {
            Dispose();
            return;
        }

        if (_frameIndex != frameIndex)
        {
            _frameIndex = frameIndex;
            _renderer.sprite = _clip.Frames[frameIndex];
        }

        // Also re-submit unchanged frames after a lighting generation reset.
        // The light owner suppresses unchanged source data itself.
        SubmitEmission();
    }

    private void SubmitEmission() => _lighting.SetDynamicLight(
        // The dynamic-light authoring integral is independent of the sprite
        // compositing conversion. Apply the normal world-source gain once.
        _lightId, _object.transform.position, _clip.EmissionAt(_frameIndex), 1f);

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _lighting.RemoveDynamicLight(_lightId);
        _renderer.enabled = false;
        UnityEngine.Object.Destroy(_object);
    }
}
