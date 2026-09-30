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

namespace Kern.Rendering.PostProcessing
{
    [DisallowMultipleComponent]
    public class PostProcessController : MonoBehaviour
    {
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
        private EigengrauComponent? _eigengrau;
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
            PostProcessRuntimeState.SetLut(null, 0f);
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

            PostProcessVolumeUtilities.ValidateVolumeProfile(profile);

            PostProcessVolumeUtilities.RequireVolumeComponent(ref _bloom, profile);
            PostProcessVolumeUtilities.RequireVolumeComponent(ref _vignette, profile);
            PostProcessVolumeUtilities.RequireVolumeComponent(ref _eigengrau, profile);
            _volumeSetupCompleted = true;
            ApplyClientConfig();
        }

        public void ApplyClientConfig()
        {
            if (_bloom == null || _vignette == null || _eigengrau == null)
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

            Debug.Log($"[PostProcessController] ApplyClientConfig: Bloom={config.Effects.BloomEnabled}, Vignette={config.Effects.VignetteEnabled}");

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

            EigengrauComponent eigengrau = GetRequired(_eigengrau, nameof(_eigengrau));
            eigengrau.color.overrideState = true;
            eigengrau.color.value = PostProcessLook.FilmGrain.Color;
            eigengrau.darknessThreshold.overrideState = true;
            eigengrau.darknessThreshold.value = PostProcessLook.FilmGrain.DarknessThreshold;
            eigengrau.noiseScale.overrideState = true;
            eigengrau.noiseScale.value = PostProcessLook.FilmGrain.NoiseScale;
            EigengrauIntensity = config.Effects.EigengrauEnabled ? PostProcessLook.FilmGrain.Intensity : 0f;
        }

        private void Update()
        {
            _gradingWorkbench.Tick();
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
