#nullable enable

using System;
using Kern.Rendering.PostProcessing;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer.Unity;

namespace Kern.Rendering;
public sealed class HDROutputReconciler : IStartable, ITickable, IDisposable
{
    // Probing every frame is pointless: HDR availability changes on the
    // scale of plugging in a monitor, not of a frame.
    private const float ProbeIntervalSeconds = 1f;

    // Через именованный контракт, а не через сырую Camera: тот же объект,
    // но бутстрап заводил IGameplayCamera именно для потребителей DI.
    private readonly IGameplayCamera _camera;
    private double _nextProbeTime;
    private Volume? _volume;
    private VolumeProfile? _profile;
    private Tonemapping? _tonemapping;

    public HDROutputReconciler(IGameplayCamera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public void Start()
    {
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "Display output calibration (runtime)";
        _tonemapping = _profile.Add<Tonemapping>(true);
        _tonemapping.mode.Override(TonemappingMode.Neutral);
        _tonemapping.neutralHDRRangeReductionMode.Override(NeutralRangeReductionMode.BT2390);
        _tonemapping.detectPaperWhite.Override(false);
        _tonemapping.detectBrightnessLimits.Override(false);
        _tonemapping.minNits.Override(0f);
        _tonemapping.hueShiftAmount.Override(0f);
        _volume = _camera.Camera.gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = float.MaxValue;
        _volume.weight = 1f;
        _volume.sharedProfile = _profile;
        UpdateCalibration();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Display.onDisplaysUpdated += OnDisplaysUpdated;
        Application.focusChanged += OnFocusChanged;
        Apply();
    }

    public void Tick()
    {
        UpdateCalibration();
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextProbeTime)
        {
            return;
        }

        _nextProbeTime = now + ProbeIntervalSeconds;
        Apply();
    }

    public void Dispose()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Display.onDisplaysUpdated -= OnDisplaysUpdated;
        Application.focusChanged -= OnFocusChanged;
        if (_volume != null)
        {
            _volume.enabled = false;
            UnityEngine.Object.Destroy(_volume);
        }

        if (_profile != null)
        {
            foreach (VolumeComponent component in _profile.components)
            {
                UnityEngine.Object.Destroy(component);
            }

            UnityEngine.Object.Destroy(_profile);
        }

        _volume = null;
        _profile = null;
        _tonemapping = null;
    }

    private void UpdateCalibration()
    {
        if (_tonemapping == null)
        {
            return;
        }

        _tonemapping.paperWhite.value = PostProcessRuntimeState.DisplayPaperWhiteNits;
        _tonemapping.maxNits.value = PostProcessRuntimeState.DisplayPeakBrightnessNits;
        if (_camera.Camera.TryGetComponent(out UniversalAdditionalCameraData data))
        {
            data.volumeLayerMask |= 1 << _camera.Camera.gameObject.layer;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    private void OnDisplaysUpdated() => HDROutput.Retry();

    private void OnFocusChanged(bool focused)
    {
        if (focused)
        {
            HDROutput.Retry();
            Apply();
        }
    }

    private void Apply()
    {
        Camera camera = _camera.Camera;
        if (camera == null)
        {
            return;
        }

        HDROutput.Reconcile();
        HDROutput.ConfigureCamera(camera);
    }
}
