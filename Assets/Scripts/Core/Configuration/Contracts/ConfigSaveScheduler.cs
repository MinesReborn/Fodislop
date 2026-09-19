#nullable enable

using System;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Core;

public sealed class ConfigSaveScheduler(IClientConfigManager clientConfig)
{
    public const float DebounceSeconds = 0.25f;

    private readonly IClientConfigManager _clientConfig = clientConfig ??
        throw new ArgumentNullException(nameof(clientConfig));

    private bool _pending;
    private float _dueTime;

    public void Queue()
    {
        _pending = true;
        _dueTime = Time.unscaledTime + DebounceSeconds;
    }

    public bool TryFlush(float currentTime)
    {
        if (!_pending || currentTime < _dueTime)
        {
            return false;
        }

        _pending = false;
        Debug.Log("[ConfigSaveScheduler] Debounce expired; flushing config to disk.");
        _clientConfig.Save();
        return true;
    }

    public void Flush()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        Debug.Log("[ConfigSaveScheduler] Immediate flush requested; saving config to disk.");
        _clientConfig.Save();
    }
}
