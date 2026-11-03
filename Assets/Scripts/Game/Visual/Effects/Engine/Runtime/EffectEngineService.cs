#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Effects;
using UnityEngine;

namespace Kern.Game.Visual.Effects.Engine;

/// <summary>Entry point for authored effects: spawn by id, advance once per frame, render through the VisualEffect pool.</summary>
public sealed class EffectEngineService : IDisposable
{
    private readonly EffectEngine _engine;
    private readonly WorldEffectSpriteSink _sink;
    private bool _disposed;

    public EffectEngineService(
        EffectCatalog catalog,
        IVisualEffectService visualEffectService,
        EffectSpriteSheetCache spriteSheets,
        int maximumSprites = 256)
    {
        _engine = new EffectEngine(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        _sink = new WorldEffectSpriteSink(
            visualEffectService ?? throw new ArgumentNullException(nameof(visualEffectService)),
            spriteSheets ?? throw new ArgumentNullException(nameof(spriteSheets)),
            maximumSprites);
    }

    public int ActiveCount => _engine.ActiveCount;

    public void Play(string effectId, Vector2 position)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EffectEngineService));
        }

        _engine.Spawn(effectId, position);
    }

    public void Update(float deltaSeconds)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EffectEngineService));
        }

        _sink.Render(_engine, deltaSeconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sink.Dispose();
        _engine.Dispose();
    }
}
