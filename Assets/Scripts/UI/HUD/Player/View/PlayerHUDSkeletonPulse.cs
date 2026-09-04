#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Controls placeholder skeleton pulsing animation while player stats are loading.
/// </summary>
internal sealed class PlayerHUDSkeletonPulse
{
    private const float PulseMin = 0.3f;
    private const float PulseMax = 0.7f;
    private const float PulseDuration = 0.8f;

    private readonly List<VisualElement> _elements = [];
    private IVisualElementScheduledItem? _scheduledItem;
    private float _timer;
    private bool _rising = true;

    public void Register(VisualElement? element)
    {
        if (element != null)
        {
            _elements.Add(element);
        }
    }

    public void Start(VisualElement root)
    {
        Stop();
        _timer = 0f;
        _rising = true;

        _scheduledItem = root.schedule.Execute(() =>
        {
            float dt = Time.unscaledDeltaTime;
            _timer += _rising ? dt : -dt;
            if (_timer >= PulseDuration)
            {
                _timer = PulseDuration;
                _rising = false;
            }
            else if (_timer <= 0f)
            {
                _timer = 0f;
                _rising = true;
            }

            float alpha = Mathf.Lerp(PulseMin, PulseMax, _timer / PulseDuration);
            for (int i = 0; i < _elements.Count; i++)
            {
                _elements[i].style.opacity = alpha;
            }
        }).Every(33);
    }

    public void Stop()
    {
        if (_scheduledItem != null)
        {
            _scheduledItem.Pause();
            _scheduledItem = null;
        }

        for (int i = 0; i < _elements.Count; i++)
        {
            _elements[i].style.opacity = 1f;
        }
    }
}
