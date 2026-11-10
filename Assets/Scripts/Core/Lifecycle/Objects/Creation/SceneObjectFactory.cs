#nullable enable

using System;
using UnityEngine;
using VContainer;

namespace Kern.Core.Lifecycle
{

public sealed class SceneObjectFactory : ISceneObjectFactory
{
    private readonly Transform _runtimeRoot;
    private readonly Transform _robotsRoot;
    private readonly Transform _buildingsRoot;
    private readonly Transform _visualEffectRoot;
    private readonly Transform _floatingUIRoot;
    private readonly Transform _audioEventsRoot;
    private readonly IObjectResolver _resolver;

    public SceneObjectFactory(
        Transform runtimeRoot,
        Transform robotsRoot,
        Transform buildingsRoot,
        Transform visualEffectRoot,
        Transform floatingUIRoot,
        Transform audioEventsRoot,
        IObjectResolver resolver)
    {
        _runtimeRoot = runtimeRoot;
        _robotsRoot = robotsRoot;
        _buildingsRoot = buildingsRoot;
        _visualEffectRoot = visualEffectRoot;
        _floatingUIRoot = floatingUIRoot;
        _audioEventsRoot = audioEventsRoot;
        _resolver = resolver;
    }

    public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) => owner switch
    {
        RuntimeOwner.Robots => _robotsRoot,
        RuntimeOwner.Buildings => _buildingsRoot,
        RuntimeOwner.VisualEffects => _visualEffectRoot,
        RuntimeOwner.FloatingUI => _floatingUIRoot,
        RuntimeOwner.AudioEvents => _audioEventsRoot,
        _ => _runtimeRoot,
    };

    public GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Runtime object name is required.", nameof(name));
        }

        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(GetOwner(owner), false);
        return gameObject;
    }

    public T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General)
        where T : MonoBehaviour
    {
        GameObject gameObject = Create(name, owner);
        gameObject.SetActive(false);
        T component = gameObject.AddComponent<T>();
        _resolver.Inject(component);
        gameObject.SetActive(true);
        return component;
    }
}
}
