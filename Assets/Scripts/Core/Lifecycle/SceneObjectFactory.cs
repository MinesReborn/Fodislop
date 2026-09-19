#nullable enable

using System;
using UnityEngine;
using VContainer;

namespace Kern.Core.Lifecycle;

public sealed class SceneObjectFactory(
    Transform runtimeRoot,
    Transform robotsRoot,
    Transform buildingsRoot,
    Transform vfxRoot,
    Transform floatingUIRoot,
    Transform audioEventsRoot,
    IObjectResolver resolver) : ISceneObjectFactory
{
    public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) => owner switch
    {
        RuntimeOwner.Robots => robotsRoot,
        RuntimeOwner.Buildings => buildingsRoot,
        RuntimeOwner.VFX => vfxRoot,
        RuntimeOwner.FloatingUI => floatingUIRoot,
        RuntimeOwner.AudioEvents => audioEventsRoot,
        _ => runtimeRoot,
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
        resolver.Inject(component);
        gameObject.SetActive(true);
        return component;
    }
}
