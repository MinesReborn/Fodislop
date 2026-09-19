#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Rendering.PostProcessing.Workbench;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;
using Unity.Profiling;

namespace Kern.Rendering.PostProcessing
{
    [DisallowMultipleComponent]
    public class PostProcessController : MonoBehaviour
    {
        private static readonly ProfilerMarker _PostProcessLateUpdateMarker = new("Kern.PostProcess.LateUpdate");

        [SerializeField]
        private Volume? _volume;

        private Camera? _mainCamera;
        // NonSerialized обязателен. При перекомпиляции во время Play Unity
        // сохраняет и восстанавливает приватные поля компонента: bool переживал
        // перезагрузку домена, а внедрённый IClientConfigManager — нет (интерфейс
        // не сериализуется). Update видел «подготовка завершена» при пустой
        // зависимости и бросал исключение каждый кадр. Теперь флаг сбрасывается
        // вместе с внедрением, и Update просто выходит.
        [System.NonSerialized]
        private bool _volumeSetupCompleted;
        private bool _missingConfigReported;

        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;
        private readonly GradingWorkbench _gradingWorkbench = new();

        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        [Inject]
        private void Construct(Volume volume)
        {
            _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        }

        public float BloomIntensity
        {
            get => GetRequired(_bloom, nameof(_bloom)).intensity.value;
            set
            {
                BloomComponent bloom = GetRequired(_bloom, nameof(_bloom));
                bloom.intensity.overrideState = true;
                bloom.intensity.value = Mathf.Clamp(value, 0f, 5f);
                bloom.active = bloom.intensity.value > 0f;
            }
        }

        public float VignetteIntensity
        {
            get => GetRequired(_vignette, nameof(_vignette)).intensity.value;
            set
            {
                VignetteComponent vignette = GetRequired(_vignette, nameof(_vignette));
                vignette.intensity.overrideState = true;
                vignette.intensity.value = Mathf.Clamp01(value);
                vignette.active = vignette.intensity.value > 0f;
            }
        }

        public float Exposure
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).exposure.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                float sanitized = Mathf.Clamp(value, -4f, 4f);
                if (!Mathf.Approximately(colorGrading.exposure.value, sanitized))
                {
                    colorGrading.exposure.overrideState = true;
                    colorGrading.exposure.value = sanitized;
                    UpdateColorGradingActiveState();
                }
            }
        }

        public float Contrast
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).contrast.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                float sanitized = Mathf.Clamp(value, -1f, 1f);
                if (!Mathf.Approximately(colorGrading.contrast.value, sanitized))
                {
                    colorGrading.contrast.overrideState = true;
                    colorGrading.contrast.value = sanitized;
                    UpdateColorGradingActiveState();
                }
            }
        }

        public float Saturation
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).saturation.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                float sanitized = Mathf.Clamp(value, 0f, 2f);
                if (!Mathf.Approximately(colorGrading.saturation.value, sanitized))
                {
                    colorGrading.saturation.overrideState = true;
                    colorGrading.saturation.value = sanitized;
                    UpdateColorGradingActiveState();
                }
            }
        }

        public float EigengrauIntensity
        {
            get => GetRequired(_eigengrau, nameof(_eigengrau)).intensity.value;
            set
            {
                EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
                eigengrau.intensity.overrideState = true;
                eigengrau.intensity.value = Mathf.Clamp01(value);
                eigengrau.active = eigengrau.intensity.value > 0f;
            }
        }

        public float MotionBlurIntensity
        {
            get => GetRequired(_motionBlur, nameof(_motionBlur)).intensity.value;
            set
            {
                MotionBlurComponent motionBlur = GetRequired(_motionBlur, nameof(_motionBlur));
                motionBlur.intensity.overrideState = true;
                motionBlur.intensity.value = Mathf.Clamp01(value);
                motionBlur.active = motionBlur.intensity.value > 0f;
            }
        }

        private void UpdateColorGradingActiveState()
        {
            ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
            colorGrading.active = true;
        }

        private void Awake()
        {
            _mainCamera = _gameplayCamera?.Camera;
        }

        private void OnEnable()
        {
            // Runtime debug state is static because the render pass is owned by
            // the renderer asset. A controller re-enable must nevertheless
            // start from the production frame; otherwise a previously opened
            // grading scope can leave the game in a false-colour/gamut view.
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;

            if (Application.isPlaying &&
                _clientConfigManager != null &&
                _clientConfigManager.Config != null)
            {
                bool alreadySetup = _volumeSetupCompleted;
                EnsureVolumeSetup();
                if (alreadySetup)
                {
                    ApplyClientConfig();
                }
            }
        }

        private void OnDisable()
        {
            _gradingWorkbench.Deactivate();
            PostProcessRuntimeState.BypassPostProcessEffects = false;
            PostProcessRuntimeState.TemporaryBypass = false;
            PostProcessRuntimeState.SetColorGrade(ColorGradeSnapshot.FromLook());
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            PostProcessRuntimeState.CompareSplit = 0f;
            PostProcessRuntimeState.CompareMode = CompareMode.Off;
            PostProcessRuntimeState.CompareBefore = false;
            PostProcessRuntimeState.SetMainCamera(null);
        }

        private void OnDestroy()
        {
            _gradingWorkbench.Dispose();
        }

        public void Start()
        {
            if (!Application.isPlaying || _clientConfigManager?.Config == null)
            {
                return;
            }

            EnsureVolumeSetup();
        }

        public void EnsureVolumeSetup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_volumeSetupCompleted)
            {
                return;
            }

            // Без конфига подготовка НЕ объявляется завершённой.
            //
            // Метод публичный, и вызывали его в том числе оттуда, где внедрение
            // ещё не случилось. Флаг при этом взводился на строку раньше, чем
            // ApplyClientConfig падал на отсутствующем IClientConfigManager, —
            // и дальше уже каждый Update заходил в грейд и бросал исключение
            // заново. Два исключения на кадр со сборкой стека, до конца сессии,
            // от одной неудачной подготовки.
            //
            // Теперь неудача просто не считается успехом: флаг не взводится,
            // Update выходит на нём же, а подготовку повторят Start или OnEnable,
            // когда конфиг появится.
            if (_clientConfigManager?.Config == null)
            {
                if (!_missingConfigReported)
                {
                    _missingConfigReported = true;
                    Debug.LogWarning(
                        "PostProcessController: подготовка тома отложена — " +
                        "IClientConfigManager ещё не внедрён или его конфиг пуст.",
                        this);
                }

                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = _gameplayCamera?.Camera;
            }

            var mainCam = _mainCamera;
            if (mainCam != null)
            {
                EnsureCameraSetup(mainCam);
            }

            if (_volume == null)
            {
                throw new InvalidOperationException("PostProcessController requires a serialized Volume component.");
            }

            VolumeProfile? profile = _volume.sharedProfile ?? _volume.profile;
            if (profile == null)
            {
                throw new InvalidOperationException("PostProcessController requires a runtime VolumeProfile on its serialized Volume.");
            }

            PostProcessDefaults.ValidateVolumeProfile(profile);

            PostProcessDefaults.RequireVolumeComponent(ref _bloom, profile);
            PostProcessDefaults.RequireVolumeComponent(ref _vignette, profile);
            PostProcessDefaults.RequireVolumeComponent(ref _colorGrading, profile);
            _colorGrading.active = true;
            PostProcessDefaults.RequireVolumeComponent(ref _eigengrau, profile);
            PostProcessDefaults.RequireVolumeComponent(ref _motionBlur, profile);
            _volumeSetupCompleted = true;
            ApplyClientConfig();
        }

        public void ApplyClientConfig()
        {
            if (_bloom == null || _vignette == null ||
                _colorGrading == null ||
                _eigengrau == null || _motionBlur == null)
            {
                // Подготовка сама вызовет применение в конце, поэтому
                // здесь возврат: иначе конфиг применился бы дважды за один
                // вызов — ровно та кратность, ради которой всё и правится.
                _volumeSetupCompleted = false;
                EnsureVolumeSetup();
                return;
            }

            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException("PostProcessController requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException("PostProcessController requires an initialized ClientConfig.");

            PostProcessRuntimeState.SetColorGrade(ColorGradeSnapshot.FromLook());

            bool photosensitive = config.Accessibility.ReducePhotosensitivity;

            Debug.Log($"[PostProcessController] ApplyClientConfig: Bloom={config.Effects.BloomEnabled}, Vignette={config.Effects.VignetteEnabled}, MotionBlur={config.Effects.MotionBlurEnabled}");

            BloomComponent bloom = GetRequired(_bloom, nameof(_bloom));
            bloom.threshold.overrideState = true;
            bloom.threshold.value = PostProcessLook.Bloom.Threshold;
            bloom.softKnee.overrideState = true;
            bloom.softKnee.value = PostProcessLook.Bloom.SoftKnee;
            bloom.radius.overrideState = true;
            bloom.radius.value = PostProcessLook.Bloom.Radius;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = PostProcessLook.Bloom.Scatter;
            bloom.tint.overrideState = true;
            bloom.tint.value = PostProcessLook.Bloom.Tint;
            BloomIntensity = config.Effects.BloomEnabled ? PostProcessLook.Bloom.Intensity : 0f;

            VignetteComponent vignette = GetRequired(_vignette, nameof(_vignette));
            vignette.color.overrideState = true;
            vignette.color.value = PostProcessLook.Vignette.Color;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = PostProcessLook.Vignette.Smoothness;
            vignette.center.overrideState = true;
            vignette.center.value = PostProcessLook.Vignette.Center;
            VignetteIntensity = config.Effects.VignetteEnabled ? PostProcessLook.Vignette.Intensity : 0f;


            ApplyColorGrading(
                PostProcessLook.ColorGrading.Exposure,
                PostProcessLook.ColorGrading.Contrast,
                PostProcessLook.ColorGrading.Saturation);

            EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
            eigengrau.color.overrideState = true;
            eigengrau.color.value = PostProcessLook.FilmGrain.Color;
            eigengrau.darknessThreshold.overrideState = true;
            eigengrau.darknessThreshold.value = PostProcessLook.FilmGrain.DarknessThreshold;
            eigengrau.noiseScale.overrideState = true;
            eigengrau.noiseScale.value = PostProcessLook.FilmGrain.NoiseScale;
            eigengrau.animationSpeed.overrideState = true;
            eigengrau.animationSpeed.value = PostProcessLook.FilmGrain.AnimationSpeed;
            EigengrauIntensity = config.Effects.EigengrauEnabled ? PostProcessLook.FilmGrain.Intensity : 0f;

            MotionBlurComponent motionBlur = GetRequired(_motionBlur, nameof(_motionBlur));
            motionBlur.intensity.overrideState = true;
            MotionBlurIntensity =
                config.Effects.MotionBlurEnabled && !photosensitive
                    ? PostProcessLook.MotionBlur.Intensity
                    : 0f;

            PostProcessRuntimeState.InvalidateTemporalHistory();
        }

        private void ApplyColorGrading(
            float authoredExposure,
            float authoredContrast,
            float authoredSaturation)
        {
            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "PostProcessController requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "PostProcessController requires an initialized ClientConfig.");
            PostProcessSettings settings = config.PostProcess ??
                throw new InvalidOperationException(
                    "PostProcessController requires post-process settings in ClientConfig.");

            Exposure = settings.Exposure + authoredExposure;
            Color filter = PostProcessLook.ColorGrading.Filter;
            float contrast = settings.Contrast + authoredContrast;
            float saturation = settings.Saturation * authoredSaturation;

            if (!ColorblindAdaptation.TryApply(
                    config.Accessibility.ColorblindMode, ref filter, ref contrast, ref saturation))
            {
                Debug.LogError(
                    "[PostProcessController] Неизвестный режим цветокоррекции " +
                    $"{config.Accessibility.ColorblindMode}; коррекция не применена.");
            }

            ColorGradingComponent colorGrading = GetRequired(
                _colorGrading,
                nameof(_colorGrading));
            colorGrading.colorFilter.overrideState = true;
            if (colorGrading.colorFilter.value != filter)
            {
                PostProcessRuntimeState.InvalidateTemporalHistory();
            }

            colorGrading.colorFilter.value = filter;
            Contrast = contrast;
            Saturation = saturation;
        }

        private void Update()
        {
            _gradingWorkbench.Tick();
            if (!_volumeSetupCompleted)
            {
                return;
            }

            if (_gradingWorkbench.IsApplying)
            {
                ColorGradeState state = _gradingWorkbench.State;
                state.Sanitize();
                PostProcessRuntimeState.SetColorGrade(state.ToSnapshot());
                ApplyColorGrading(
                    state.EffectiveExposure,
                    state.EffectiveContrast,
                    state.EffectiveSaturation);
            }
            else
            {
                if (_gradingWorkbench.StoppedApplying)
                {
                    ApplyClientConfig();
                }

                // Только когда рабочее место НЕ применяет свой грейд: пока
                // автор крутит ползунки, он обязан видеть ровно то, что крутит,
                // а не сумму своей правки и зоны, где стоит камера.
                ColorGradeZones.Resolution resolution = ColorGradeZoneDriver.Push(
                    _gradingWorkbench.Zones, _mainCamera ??= _gameplayCamera?.Camera);
                ApplyColorGrading(
                    resolution.Exposure,
                    resolution.Contrast,
                    resolution.Saturation);
            }
        }

        private void EnsureCameraSetup(Camera mainCamera)
        {
            HDROutput.ConfigureCamera(mainCamera);
            if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.volumeLayerMask = (1 << RequireVolume().gameObject.layer) |
                    (1 << mainCamera.gameObject.layer);
                cameraData.volumeTrigger = mainCamera.transform;
            }
        }

        private Volume RequireVolume() =>
            _volume ?? throw new InvalidOperationException(
                "PostProcessController requires its authored Volume component.");


        private static T GetRequired<T>(T? component, string fieldName)
            where T : UnityEngine.Object =>
            component ?? throw new InvalidOperationException($"PostProcessController component '{fieldName}' is not initialized.");
    }
}
