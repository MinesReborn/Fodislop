#nullable enable

using UnityEngine;

namespace Kern.Core.Lifecycle;

public enum RuntimeOwner
{
    General,
    Robots,
    Buildings,
    VFX,
    FloatingUI,
    AudioEvents,
}

public interface ISceneObjectFactory
{
    Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General);

    GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General);

    T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General)
        where T : MonoBehaviour;
}
