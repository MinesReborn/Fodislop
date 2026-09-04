#nullable enable

using System.Threading;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Manages dynamic emission light source for a Robot entity in the LightingEngine.
/// </summary>
public sealed class RobotLighting
{
    private static int _nextDynamicLightId;

    private readonly int _dynamicLightId;
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

    private const float DynamicLightPositionEpsilon = 0.00390625f;

    public RobotLighting(bool emitsDynamicLight, float defaultIntensity, Color defaultColor)
    {
        _dynamicLightId = Interlocked.Increment(ref _nextDynamicLightId);
        _dynamicLightEnabled = emitsDynamicLight;
        _dynamicLightIntensity = defaultIntensity;
        _dynamicLightColor = defaultColor;
    }

    public float DynamicLightIntensity => _dynamicLightIntensity;
    public Color DynamicLightColor => _dynamicLightColor;

    /// <remarks>
    /// Запасное значение — авторский дефолт секции освещения. Раньше он
    /// приходил снимком `ProjectDefaults.asset`; теперь авторское значение и
    /// есть новый экземпляр секции, поэтому источник тот же, а параметра
    /// больше не нужно.
    /// </remarks>
    public void InitializeSettings(LightingEngine? lightingEngine)
    {
        if (_dynamicLightSettingsLoaded)
        {
            return;
        }

        bool engineReady = lightingEngine?.IsRuntimeConfigReady == true;
        var defaults = new WorldLightingSettings();
        _dynamicLightIntensity = engineReady
            ? lightingEngine!.DynamicLightIntensity
            : defaults.DynamicLightIntensity;
        _dynamicLightColor = engineReady
            ? lightingEngine!.DynamicLightColor
            : defaults.DynamicLightColor;
        _dynamicLightSettingsLoaded = engineReady;
    }

    public void ResetPreferences(LightingEngine? lightingEngine)
    {
        var defaults = new WorldLightingSettings();
        _dynamicLightIntensity = lightingEngine?.DynamicLightIntensity ??
            defaults.DynamicLightIntensity;
        _dynamicLightColor = lightingEngine?.DynamicLightColor ??
            defaults.DynamicLightColor;
        _dynamicLightSettingsLoaded = true;
    }

    public void SetIntensity(float intensity, LightingEngine? lightingEngine)
    {
        _dynamicLightIntensity = Mathf.Clamp(intensity, 0f, 4f);
        lightingEngine?.SetDynamicLightSettings(_dynamicLightIntensity, _dynamicLightColor);
    }

    public void SetColor(Color color, LightingEngine? lightingEngine)
    {
        _dynamicLightColor = new Color(
            Mathf.Max(0f, color.r),
            Mathf.Max(0f, color.g),
            Mathf.Max(0f, color.b),
            1f);
        lightingEngine?.SetDynamicLightSettings(_dynamicLightIntensity, _dynamicLightColor);
    }

    public void Update(Vector3 position, LightingEngine? lighting)
    {
        if (!_dynamicLightEnabled || lighting == null || !lighting.IsRuntimeConfigReady)
        {
            if (_hasSubmittedDynamicLight)
            {
                lighting?.RemoveDynamicLight(_dynamicLightId);
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
            (_lastDynamicLightPosition - pos2D).sqrMagnitude <=
                DynamicLightPositionEpsilon * DynamicLightPositionEpsilon &&
            _lastDynamicLightColor == _dynamicLightColor &&
            Mathf.Approximately(_lastDynamicLightIntensity, _dynamicLightIntensity))
        {
            return;
        }

        lighting.SetDynamicLight(
            _dynamicLightId,
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
            lighting.RemoveDynamicLight(_dynamicLightId);
            _hasSubmittedDynamicLight = false;
        }
    }
}
