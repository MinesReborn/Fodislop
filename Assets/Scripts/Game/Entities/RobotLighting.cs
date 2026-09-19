#nullable enable

using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotLighting
{
    private static int _nextDynamicLightID;

    private readonly int _dynamicLightID;
    private bool _dynamicLightEnabled;
    private float _dynamicLightIntensity;
    private Color _dynamicLightColor;
    private bool _hasSubmittedDynamicLight;
    private bool _dynamicLightSettingsLoaded;
    private LightingEngine? _lastDynamicLightEngine;
    private uint _lastDynamicLightGeneration;
    private Vector2 _lastDynamicLightPosition;
    private Color _lastDynamicLightColor;
    private float _lastDynamicLightIntensity;

    public RobotLighting()
    {
        _dynamicLightID = Interlocked.Increment(ref _nextDynamicLightID);
        _dynamicLightEnabled = LightingConfigHolder.DynamicLightEnabled;
        _dynamicLightIntensity = LightingConfigHolder.DynamicLightIntensity;
        _dynamicLightColor = LightingConfigHolder.DynamicLightColor;
    }

    public float DynamicLightIntensity => _dynamicLightIntensity;
    public Color DynamicLightColor => _dynamicLightColor;

    public void InitializeSettings(LightingEngine? lightingEngine)
    {
        if (_dynamicLightSettingsLoaded)
        {
            return;
        }

        _dynamicLightIntensity = LightingConfigHolder.DynamicLightIntensity;
        _dynamicLightColor = LightingConfigHolder.DynamicLightColor;
        _dynamicLightSettingsLoaded = true;
    }

    public void ResetPreferences(LightingEngine? lightingEngine)
    {
        _dynamicLightIntensity = LightingConfigHolder.DynamicLightIntensity;
        _dynamicLightColor = LightingConfigHolder.DynamicLightColor;
        _dynamicLightSettingsLoaded = true;
    }

    public void SetEnabled(bool enabled, LightingEngine? lightingEngine)
    {
        if (_dynamicLightEnabled == enabled)
        {
            return;
        }

        _dynamicLightEnabled = enabled;
        if (!enabled)
        {
            Remove(lightingEngine);
        }
    }

    public void SetColor(Color color, LightingEngine? lightingEngine)
    {
        _dynamicLightColor = new Color(
            Mathf.Max(0f, color.r),
            Mathf.Max(0f, color.g),
            Mathf.Max(0f, color.b),
            1f);
    }

    public void Update(Vector3 position, LightingEngine? lighting)
    {
        if (!_dynamicLightEnabled || lighting == null || !lighting.IsRuntimeConfigReady)
        {
            if (_hasSubmittedDynamicLight)
            {
                lighting?.RemoveDynamicLight(_dynamicLightID);
            }

            _hasSubmittedDynamicLight = false;
            return;
        }

        if (!_dynamicLightSettingsLoaded)
        {
            _dynamicLightIntensity = lighting.DynamicLightIntensity;
            _dynamicLightColor = lighting.DynamicLightColor;
            _dynamicLightSettingsLoaded = true;
        }

        Vector2 pos2D = new(position.x, position.y);
        uint generation = lighting.DynamicLightGeneration;
        if (_hasSubmittedDynamicLight &&
            ReferenceEquals(_lastDynamicLightEngine, lighting) &&
            _lastDynamicLightGeneration == generation &&
            _lastDynamicLightPosition == pos2D &&
            _lastDynamicLightColor == _dynamicLightColor &&
            _lastDynamicLightIntensity == _dynamicLightIntensity)
        {
            return;
        }

        lighting.SetDynamicLight(
            _dynamicLightID,
            pos2D,
            _dynamicLightColor,
            _dynamicLightIntensity);
        _lastDynamicLightEngine = lighting;
        _lastDynamicLightGeneration = generation;
        _lastDynamicLightPosition = pos2D;
        _lastDynamicLightColor = _dynamicLightColor;
        _lastDynamicLightIntensity = _dynamicLightIntensity;
        _hasSubmittedDynamicLight = true;
    }

    public void Remove(LightingEngine? lighting)
    {
        if (_hasSubmittedDynamicLight && lighting != null)
        {
            lighting.RemoveDynamicLight(_dynamicLightID);
            _hasSubmittedDynamicLight = false;
        }
    }
}
