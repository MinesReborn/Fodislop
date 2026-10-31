#nullable enable

using System;
using UnityEngine;

namespace Kern.Core.Lifecycle;

[Serializable]
public sealed class ManagerBinding
{
    [SerializeField]
    private string? _managerType;

    [SerializeField]
    private string? _serviceGroup;

    [SerializeField]
    private MonoBehaviour? _target;

    public ManagerBinding(string managerType, string serviceGroup, MonoBehaviour target)
    {
        _managerType = managerType;
        _serviceGroup = serviceGroup;
        _target = target;
    }

    public string? ManagerType => _target != null
        ? _target.GetType().AssemblyQualifiedName
        : _managerType;

    public string? ServiceGroup => _serviceGroup;

    public MonoBehaviour? Target => _target;
}
