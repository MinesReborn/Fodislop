#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;
using Unity.Profiling;

namespace Fodinae.Rendering.PostProcessing
{
    [DisallowMultipleComponent]
    public class PostProcessController : MonoBehaviour
    {
        private static readonly ProfilerMarker PostProcessLateUpdateMarker = new("Fodinae.PostProcess.LateUpdate");

        [SerializeField]
        private Volume? _volume;

        private Camera? _mainCamera;
        private bool _volumeSetupCompleted;
        private WorldUiOverlayCameraRig _cameraRig = null!;
        private bool _hasWorldUIProjection;

        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;

        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

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

        public float ChromaticAberrationIntensity
        {
            get => GetRequired(_chromaticAberration, nameof(_chromaticAberration)).intensity.value;
            set
            {
                ChromaticAberrationComponent chromaticAberration = GetRequired(_chromaticAberration, nameof(_chromaticAberration));
                chromaticAberration.intensity.overrideState = true;
                chromaticAberration.intensity.value = Mathf.Clamp01(value);
                chromaticAberration.active = chromaticAberration.intensity.value > 0f;
            }
        }

        public float Exposure
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).exposure.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.exposure.overrideState = true;
                colorGrading.exposure.value = Mathf.Clamp(value, -4f, 4f);
                UpdateColorGradingActiveState();
            }
        }

        public float Contrast
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).contrast.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.contrast.overrideState = true;
                colorGrading.contrast.value = Mathf.Clamp(value, -1f, 1f);
                UpdateColorGradingActiveState();
            }
        }

        public float Saturation
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).saturation.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.saturation.overrideState = true;
                colorGrading.saturation.value = Mathf.Clamp(value, 0f, 2f);
                UpdateColorGradingActiveState();
            }
        }

        public bool ToneMappingEnabled
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).toneMapping.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.active = true;
                colorGrading.toneMapping.overrideState = true;
                colorGrading.toneMapping.value = value;
            }
        }

        public float ToneMappingWhitePoint
        {
            get => GetRequired(_colorGrading, nameof(_colorGrading)).toneMappingWhitePoint.value;
            set
            {
                ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
                colorGrading.active = true;
                colorGrading.toneMappingWhitePoint.overrideState = true;
                colorGrading.toneMappingWhitePoint.value = Mathf.Clamp(
                    value, PostProcessSettings.ToneMappingWhitePointMin, PostProcessSettings.ToneMappingWhitePointMax);
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
            _cameraRig = new WorldUiOverlayCameraRig(_sceneObjects);
        }

        private void OnEnable()
        {
            if (Application.isPlaying &&
                _clientConfigManager != null &&
                _clientConfigManager.Config != null)
            {
                EnsureVolumeSetup();
            }
        }

        private void OnDisable()
        {
            PostProcessRenderPass.SetMainCamera(null);
            _cameraRig?.DisableOverlay();
            _cameraRig?.ReleaseWorldUILayer();
        }

        public void Start()
        {
            if (!Application.isPlaying || _clientConfigManager?.Config == null)
            {
                return;
            }

            EnsureVolumeSetup();
        }

        /// <summary>
        /// Готовит том постпроцесса: камеру, профиль, компоненты эффектов.
        /// </summary>
        /// <remarks>
        /// Метод обязан быть идемпотентным, потому что зовут его четверо:
        /// OnEnable, Start, стартовый конвейер и вкладка эффектов в меню
        /// паузы. Раньше он в конце безусловно применял весь конфиг, и на
        /// запуске тот применялся столько раз, сколько было вызовов — в
        /// логе это видно как пять подряд одинаковых строк ApplyClientConfig.
        ///
        /// Дело не только в шуме: каждое применение проходит по всем
        /// эффектам, а два вызова подряд из OnEnable и Start случаются в
        /// разных кадрах, и повторная запись прячет расхождения — если
        /// что-то между ними сбросило значение, второй проход это молча
        /// затрёт, и причина потеряется.
        ///
        /// Конфиг теперь применяется только тогда, когда подготовка
        /// действительно что-то сделала. Тем, кому нужно именно применение,
        /// а не подготовка, следует звать <see cref="ApplyClientConfig"/>.
        /// </remarks>
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

            VolumeProfile? profile = _volume.profile;
            if (profile == null)
            {
                throw new InvalidOperationException("PostProcessController requires a runtime VolumeProfile on its serialized Volume.");
            }

            PostProcessDefaults.ValidateVolumeProfile(profile);

            PostProcessDefaults.RequireVolumeComponent(ref _bloom, profile);
            PostProcessDefaults.RequireVolumeComponent(ref _vignette, profile);
            PostProcessDefaults.RequireVolumeComponent(ref _chromaticAberration, profile);
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
                _chromaticAberration == null || _colorGrading == null ||
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
            // The graphics preset used to stop at this class's doorstep: every
            // value below is an artistic one from ClientConfig, and nothing
            // here ever read GraphicsQualitySettings. That made the whole
            // post-processing stack cost the same on VeryLow as on Ultra -
            // bloom pyramid, motion blur and all - no matter which preset the
            // player picked, and it kept costing that with world lighting
            // switched off, because the two subsystems are unrelated.
            // Продвинутые эффекты собираются из вида и тумблеров: величины
            // задаёт PostProcessLook, конфиг говорит только «платим или нет».
            PostProcessRenderPass.SetAdvancedSettings(
                AdvancedPostProcessComposer.From(config));

            bool photosensitive = config.Accessibility.ReducePhotosensitivity;
            PostProcessSettings postProcess = config.PostProcess ??
                throw new InvalidOperationException("PostProcessController requires post-process settings in ClientConfig.");

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

            // Хроматика — мерцающий по краям эффект, и при светочувствительности
            // она снимается целиком, а не приглушается.
            ChromaticAberrationIntensity = config.Effects.ChromaticAberrationEnabled && !photosensitive
                ? PostProcessLook.ChromaticAberration.Intensity
                : 0f;

            ColorGradingComponent colorGrading = GetRequired(_colorGrading, nameof(_colorGrading));
            Exposure = postProcess.Exposure;
            Color baseFilter = PostProcessLook.ColorGrading.Filter;
            float contrast = postProcess.Contrast;
            float saturation = postProcess.Saturation;

            switch (config.Accessibility.ColorblindMode)
            {
                case 1:
                    baseFilter = new Color(baseFilter.r * 0.8f + baseFilter.g * 0.2f, baseFilter.g * 0.7f + baseFilter.b * 0.3f, baseFilter.b);
                    break;
                case 2:
                    baseFilter = new Color(baseFilter.r * 0.6f + baseFilter.g * 0.4f, baseFilter.g * 0.9f, baseFilter.b * 1.1f);
                    break;
                case 3:
                    baseFilter = new Color(baseFilter.r * 0.95f, baseFilter.g * 0.85f + baseFilter.b * 0.15f, baseFilter.b * 0.5f + baseFilter.r * 0.5f);
                    break;
                case 4:
                    contrast = Mathf.Min(contrast + 0.35f, 1f);
                    saturation = Mathf.Min(saturation + 0.2f, 2f);
                    break;
                case 0:
                    break;
                default:
                    Debug.LogError($"[PostProcessController] Неизвестный режим цветокоррекции {config.Accessibility.ColorblindMode}; коррекция не применена.");
                    break;
            }

            colorGrading.colorFilter.overrideState = true;
            colorGrading.colorFilter.value = baseFilter;
            ToneMappingEnabled = config.Effects.ToneMappingEnabled;
            ToneMappingWhitePoint = postProcess.ToneMappingWhitePoint;
            Contrast = contrast;
            Saturation = saturation;

            EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
            eigengrau.color.overrideState = true;
            eigengrau.color.value = PostProcessLook.FilmGrain.Color;
            eigengrau.darknessThreshold.overrideState = true;
            eigengrau.darknessThreshold.value = PostProcessLook.FilmGrain.DarknessThreshold;
            eigengrau.noiseScale.overrideState = true;
            eigengrau.noiseScale.value = PostProcessLook.FilmGrain.NoiseScale;
            eigengrau.animationSpeed.overrideState = true;
            eigengrau.animationSpeed.value = PostProcessLook.FilmGrain.AnimationSpeed;
            EigengrauIntensity = config.Effects.FilmGrainEnabled ? PostProcessLook.FilmGrain.Intensity : 0f;

            MotionBlurComponent motionBlur = GetRequired(_motionBlur, nameof(_motionBlur));
            motionBlur.intensity.overrideState = true;
            MotionBlurIntensity =
                config.Effects.MotionBlurEnabled && !photosensitive
                    ? PostProcessLook.MotionBlur.Intensity
                    : 0f;

            // Слой и стек камеры интерфейса пересобираются после применения
            // конфига: смена эффектов может переключить постпроцесс на
            // основной камере, а наложенная обязана остаться вне его.
            Camera? configured = _cameraRig?.ConfiguredMainCamera;
            if (configured != null)
            {
                _cameraRig!.EnsureCameraSetup(configured, RequireVolume());
            }
        }

        public void EnsureEditorVolume()
        {
            EnsureVolumeSetup();
        }

        private void LateUpdate()
        {
            using var marker = PostProcessLateUpdateMarker.Auto();
            if (_mainCamera == null)
            {
                _mainCamera = _gameplayCamera?.Camera;
            }

            Camera? mainCamera = _cameraRig.ConfiguredMainCamera ?? _mainCamera;
            if (mainCamera == null)
            {
                return;
            }

            _cameraRig.Sync(mainCamera, RequireVolume());
        }

        private void EnsureCameraSetup(Camera mainCamera) =>
            _cameraRig.EnsureCameraSetup(mainCamera, RequireVolume());

        private Volume RequireVolume() =>
            _volume ?? throw new InvalidOperationException(
                "PostProcessController requires its authored Volume component.");


        private static T GetRequired<T>(T? component, string fieldName)
            where T : UnityEngine.Object =>
            component ?? throw new InvalidOperationException($"PostProcessController component '{fieldName}' is not initialized.");
    }
}
