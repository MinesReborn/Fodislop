#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking.Auth;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Сцена Gateway: вход и онбординг перед главным меню.
    ///
    /// Поток сцен: Bootstrap → Gateway → MainMenu → MainGame. Раньше блок входа
    /// жил оверлеем внутри MainMenu.uxml; вынесен в свою сцену, чтобы меню не
    /// тащило чужой жизненный цикл, а ворота выгружались целиком.
    ///
    /// Онбординг показывается один раз — при первом запуске либо когда игрок
    /// открывает его сам. Пишет в те поля ClientConfig, которые действительно
    /// существуют: частоту кадров, вертикальную синхронизацию, пресет графики и
    /// приглушение звука в фоне.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GatewayController : MonoBehaviour, ILocalizableUI
    {
        private const string MainMenuSceneName = ProjectRuntimeContracts.SceneNames.MainMenu;
        private const string OnboardingDonePrefsKey = "OnboardingCompleted1";

        // Состояние ворот. Ровно один класс на корне за раз: раньше видимость
        // была своя у каждого слоя, и ничто не мешало показать вход и онбординг
        // одновременно — онбординг просто ложился поверх формы.
        private const string StateAuthClass = "gateway--auth";
        private const string StateOnboardingClass = "gateway--onboarding";
        private const string StepActiveClass = "onb-step--active";
        private const string PillActiveClass = "onb-pill--active";
        private const string PillDoneClass = "onb-pill--done";
        private const string ButtonHiddenClass = "onb-btn--hidden";

        // Без префикса «Шаг N»: номер и тему шага уже несёт полоса пилюль
        // справа, и повтор только съедал ширину, из-за которой заголовок
        // наезжал на эту самую полосу.
        // Значения — ключи словаря локализации.
        private static readonly string[] StepTitles =
        {
            "gateway.onb.step1_title",
            "gateway.onb.step2_title",
            "gateway.onb.step3_title",
        };

        private static readonly (string Label, int Value)[] FrameRates =
        {
            ("gateway.onb.fps.unlimited", -1),
            ("144 FPS", 144),
            ("120 FPS", 120),
            ("60 FPS", 60),
        };

        /// <summary>
        /// Пользовательский зум интерфейса — прямой аналог зума в браузере.
        ///
        /// PanelSettings работает в режиме ConstantPhysicalSize: размер элемента
        /// привязан к физическому размеру экрана, как CSS-пиксель. Это верно
        /// почти везде, но ломается там, где система врёт про DPI, — прежде
        /// всего на телевизорах и консолях, где Screen.dpi обычно 0 и в дело
        /// идёт fallbackDpi. Зум здесь и есть ручная поправка на такой случай.
        /// </summary>
        private static readonly (string Label, float Value)[] UIScales =
        {
            ("gateway.onb.ui_scale.100", 1.00f),
            ("gateway.onb.ui_scale.115", 1.15f),
            ("gateway.onb.ui_scale.130", 1.30f),
        };

        private UIDocument _document = null!;
        private VisualElement _root = null!;
        private VisualElement? _gatewayRoot;
        private VisualElement? _onboardingOverlay;
        private AuthGate? _authGate;
        private int _step;
        private bool _leaving;
        private bool _initialized;

        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IAuthenticationService _authentication = null!;

        private void OnEnable()
        {
            // Первичная сборка — в Start(): к нему гарантированы и инжекция
            // (мост, фаза Awake), и панель UIDocument (создаётся в OnEnable
            // документа). Здесь — только реактивация уже построенного UI:
            // переприменяем текст, не перестраивая.
            if (_initialized && _root != null && _loc != null)
            {
                _loc.RegisterLocalizable(this);
                ApplyLocalizedText();
            }
        }

        public void InitializeScene()
        {
            if (_initialized)
            {
                return;
            }

            if (_clientConfig == null || _loc == null)
            {
                // К Start инжекция гарантирована (мост, фаза Awake); отсутствие
                // зависимостей здесь — дефект, а не гонка.
                throw new InvalidOperationException(
                    "[Gateway] DI-инжекция не произошла до scene entry — вьюха строила бы UI без зависимостей.");
            }

            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                // К Start панель гарантирована: UIDocument создаёт её в своём
                // OnEnable, а Start выполняется после всех OnEnable сцены.
                throw new InvalidOperationException(
                    "[Gateway] UIDocument panel is not available at Start (панель создаётся в OnEnable документа и к Start обязана существовать).");
            }

            var asset = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.GatewayUxml);
            if (asset == null)
            {
                Debug.LogWarning($"[Gateway] UI resource '{ProjectRuntimeContracts.ResourcePaths.GatewayUxml}' is missing; returning to main menu.");
                GoToMainMenu();
                return;
            }

            _root = _document.rootVisualElement;
            _initialized = true;
            _root.Clear();

            VisualElement tree = asset.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            // Статические ключи UXML резолвятся сразу при сборке, а не только
            // по событию смены языка — иначе ворота показали бы сырые ключи.
            UILocalizer.Apply(tree, _loc);

            // Тир раскладки вместо @media — как и в остальных экранах.
            UILayoutTier.Attach(tree);
            _root = tree;

            // Состояние ставится на тот же элемент, на котором оно задано в
            // разметке. Иначе начальный gateway--auth из UXML снять было бы
            // некому и форма входа осталась бы видимой поверх онбординга.
            _gatewayRoot = _root.Q<VisualElement>("GatewayRoot") ?? _root;

            _authGate = AuthGate.TryCreate(_root, _clientConfig, _authentication, _loc);
            if (_authGate == null)
            {
                Debug.LogWarning("[Gateway] Ворота входа не собрались — сразу уходим в меню.");
                GoToMainMenu();
                return;
            }

            _authGate.Passed += OnAuthPassed;

            ApplySavedUIScale();
            BindOnboarding();

            SetState(StateAuthClass);
            _authGate.Show();

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            _loc.RegisterLocalizable(this);
            Debug.Log("[Gateway] Gateway UI initialized and displayed.");
        }

        /// <summary>
        /// Переприменяет локализованный текст после смены языка: статические ключи
        /// через UILocalizer, онбординг (заголовок шага, кнопка «Далее») и списки
        /// выпадающих списков — напрямую.
        /// </summary>
        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(GatewayController));
            if (_root == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_root, _loc);
            ApplyStep(_step);

            var uiScale = _root.Q<DropdownField>("OnbUIScale");
            if (uiScale != null)
            {
                uiScale.choices = new System.Collections.Generic.List<string>();
                foreach ((string label, float _) in UIScales)
                {
                    uiScale.choices.Add(_loc.Get(label));
                }
            }

            var frameRate = _root.Q<DropdownField>("OnbFrameRate");
            if (frameRate != null)
            {
                frameRate.choices = new System.Collections.Generic.List<string>();
                foreach ((string label, int _) in FrameRates)
                {
                    frameRate.choices.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
                }
            }

            var colorblind = _root.Q<DropdownField>("OnbColorblind");
            if (colorblind != null)
            {
                colorblind.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.colorblind.none"),
                    _loc.Get("gateway.onb.colorblind.deuteranopia"),
                    _loc.Get("gateway.onb.colorblind.protanopia"),
                    _loc.Get("gateway.onb.colorblind.tritanopia"),
                    _loc.Get("gateway.onb.colorblind.high_contrast"),
                };
            }

            var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
            if (photoSens != null)
            {
                photoSens.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.photosens.off"),
                    _loc.Get("gateway.onb.photosens.on"),
                };
            }

            var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
            if (controlScheme != null)
            {
                controlScheme.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.controls.keyboard"),
                    _loc.Get("gateway.onb.controls.mouse"),
                };
            }

            UILocalizer.AssertLocalized(_root, _loc);
        }

        private void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }
        }

        private void OnAuthPassed()
        {
            bool alreadyDone = !GatewayDevFlags.ForceGates
                && PlayerPrefs.GetInt(OnboardingDonePrefsKey, 0) == 1;

            if (alreadyDone || _onboardingOverlay == null)
            {
                GoToMainMenu();
                return;
            }

            SetState(StateOnboardingClass);
            ApplyStep(0);
        }

        /// <summary>Включает ровно одно состояние ворот и гасит остальные.</summary>
        private void SetState(string state)
        {
            if (_gatewayRoot == null)
            {
                return;
            }

            _gatewayRoot.EnableInClassList(StateAuthClass, state == StateAuthClass);
            _gatewayRoot.EnableInClassList(StateOnboardingClass, state == StateOnboardingClass);
        }

        // ─────────────────────────────────────────────────────────────
        // Онбординг
        // ─────────────────────────────────────────────────────────────

        private void BindOnboarding()
        {
            if (_clientConfig == null || _loc == null)
            {
                return;
            }

            _onboardingOverlay = _root.Q<VisualElement>("OnboardingOverlay");
            if (_onboardingOverlay == null)
            {
                return;
            }

            ClientConfig config = _clientConfig.Config;

            var uiScale = _root.Q<DropdownField>("OnbUIScale");
            if (uiScale != null)
            {
                var labels = new System.Collections.Generic.List<string>();
                foreach ((string label, float _) in UIScales)
                {
                    labels.Add(_loc.Get(label));
                }

                uiScale.choices = labels;
                uiScale.index = IndexOfUIScale(config.Interface.UIScale);

                // Применяем сразу при выборе, а не по кнопке «Далее»: смысл
                // этой настройки в том, чтобы увидеть результат на себе.
                uiScale.RegisterValueChangedCallback(_ => ApplyUIScale(ValueOfUIScale(uiScale.index)));
            }

            var colorblind = _root.Q<DropdownField>("OnbColorblind");
            if (colorblind != null)
            {
                colorblind.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.colorblind.none"),
                    _loc.Get("gateway.onb.colorblind.deuteranopia"),
                    _loc.Get("gateway.onb.colorblind.protanopia"),
                    _loc.Get("gateway.onb.colorblind.tritanopia"),
                    _loc.Get("gateway.onb.colorblind.high_contrast"),
                };
                colorblind.index = Mathf.Clamp(config.Accessibility.ColorblindMode, 0, 4);
            }

            var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
            if (photoSens != null)
            {
                photoSens.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.photosens.off"),
                    _loc.Get("gateway.onb.photosens.on"),
                };
                photoSens.index = config.Accessibility.ReducePhotosensitivity ? 1 : 0;
            }

            var frameRate = _root.Q<DropdownField>("OnbFrameRate");
            if (frameRate != null)
            {
                var labels = new System.Collections.Generic.List<string>();
                foreach ((string label, int _) in FrameRates)
                {
                    labels.Add(label.StartsWith("gateway.") ? _loc.Get(label) : label);
                }

                frameRate.choices = labels;
                frameRate.index = IndexOfFrameRate(config.Display.TargetFrameRate);
            }

            var preset = _root.Q<DropdownField>("OnbGraphicsPreset");
            if (preset != null)
            {
                preset.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.preset.ultra"),
                    _loc.Get("gateway.onb.preset.high"),
                    _loc.Get("gateway.onb.preset.medium"),
                    _loc.Get("gateway.onb.preset.fast"),
                };
                preset.index = 0;
            }

            var vsync = _root.Q<Toggle>("OnbVSync");
            if (vsync != null)
            {
                vsync.SetValueWithoutNotify(config.Display.VSync);
            }

            var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
            if (controlScheme != null)
            {
                controlScheme.choices = new System.Collections.Generic.List<string>
                {
                    _loc.Get("gateway.onb.controls.keyboard"),
                    _loc.Get("gateway.onb.controls.mouse"),
                };
                controlScheme.index = Mathf.Clamp(config.Interface.ControlScheme, 0, 1);
            }

            var masterVol = _root.Q<Slider>("OnbMasterVolume");
            var masterVolLbl = _root.Q<Label>("OnbMasterVolumeLabel");
            if (masterVol != null)
            {
                masterVol.value = Mathf.RoundToInt(config.Audio.MasterVolume * 100f);
                if (masterVolLbl != null)
                {
                    masterVolLbl.text = $"{Mathf.RoundToInt(masterVol.value)}%";
                }

                masterVol.RegisterValueChangedCallback(evt =>
                {
                    if (masterVolLbl != null)
                    {
                        masterVolLbl.text = $"{Mathf.RoundToInt(evt.newValue)}%";
                    }
                });
            }

            var mute = _root.Q<Toggle>("OnbMuteInBackground");
            if (mute != null)
            {
                mute.SetValueWithoutNotify(config.Audio.MuteInBackground);
            }

            var prev = _root.Q<Button>("OnbPrevButton");
            if (prev != null)
            {
                prev.clicked += () => ApplyStep(_step - 1);
            }

            var next = _root.Q<Button>("OnbNextButton");
            if (next != null)
            {
                next.clicked += OnNext;
            }

            var skip = _root.Q<Button>("OnbSkipButton");
            if (skip != null)
            {
                skip.clicked += FinishOnboarding;
            }
        }

        private void OnNext()
        {
            if (_step >= StepTitles.Length - 1)
            {
                FinishOnboarding();
                return;
            }

            ApplyStep(_step + 1);
        }

        private void ApplyStep(int step)
        {
            if (_loc == null)
            {
                // Защитный гард: ApplyStep вызывается из ApplyLocalizedText (после
                // проверки _loc) и из колбэков UI, построенных с гарантированным
                // _loc — пропуск здесь означает дефект проводки, а не гонку.
                return;
            }

            _step = Mathf.Clamp(step, 0, StepTitles.Length - 1);

            for (int i = 0; i < StepTitles.Length; i++)
            {
                var content = _root.Q<VisualElement>($"OnbStep{i + 1}");
                content?.EnableInClassList(StepActiveClass, i == _step);

                var pill = _root.Q<Label>($"OnbPill{i + 1}");
                if (pill == null)
                {
                    continue;
                }

                pill.EnableInClassList(PillActiveClass, i == _step);
                pill.EnableInClassList(PillDoneClass, i < _step);
            }

            var title = _root.Q<Label>("OnboardingTitle");
            if (title != null)
            {
                title.text = _loc.Get(StepTitles[_step]);
            }

            // На первом шаге назад некуда — кнопка прячется, но место сохраняет,
            // иначе футер дёргается при переходе между шагами.
            _root.Q<Button>("OnbPrevButton")?.EnableInClassList(ButtonHiddenClass, _step == 0);

            var next = _root.Q<Button>("OnbNextButton");
            if (next != null)
            {
                next.text = _step >= StepTitles.Length - 1
                    ? _loc.Get("gateway.onb.start")
                    : _loc.Get("gateway.onb.next");
            }
        }

        private void FinishOnboarding()
        {
            SaveSettings();
            PlayerPrefs.SetInt(OnboardingDonePrefsKey, 1);
            PlayerPrefs.Save();
            GoToMainMenu();
        }

        private void SaveSettings()
        {
            _clientConfig.UpdateAndSave(config =>
            {
                var uiScale = _root.Q<DropdownField>("OnbUIScale");
                if (uiScale != null)
                {
                    config.Interface.UIScale = ValueOfUIScale(uiScale.index);
                }

                var colorblind = _root.Q<DropdownField>("OnbColorblind");
                if (colorblind != null && colorblind.index >= 0)
                {
                    config.Accessibility.ColorblindMode = colorblind.index;
                }

                var photoSens = _root.Q<DropdownField>("OnbPhotosensitivity");
                if (photoSens != null && photoSens.index >= 0)
                {
                    config.Accessibility.ReducePhotosensitivity = photoSens.index == 1;
                }

                var frameRate = _root.Q<DropdownField>("OnbFrameRate");
                if (frameRate != null && frameRate.index >= 0 && frameRate.index < FrameRates.Length)
                {
                    config.Display.TargetFrameRate = FrameRates[frameRate.index].Value;
                }

                var vsync = _root.Q<Toggle>("OnbVSync");
                if (vsync != null)
                {
                    config.Display.VSync = vsync.value;
                }

                var controlScheme = _root.Q<DropdownField>("OnbControlScheme");
                if (controlScheme != null && controlScheme.index >= 0)
                {
                    config.Interface.ControlScheme = controlScheme.index;
                }

                var masterVol = _root.Q<Slider>("OnbMasterVolume");
                if (masterVol != null)
                {
                    config.Audio.MasterVolume = masterVol.value / 100f;
                }

                var mute = _root.Q<Toggle>("OnbMuteInBackground");
                if (mute != null)
                {
                    config.Audio.MuteInBackground = mute.value;
                }
            });
        }

        /// <summary>
        /// Кладёт сохранённый зум в PanelSettings. Раньше это делал только
        /// PauseMenu при своей инициализации — то есть настройка вступала в
        /// силу лишь после того, как игрок хоть раз открыл паузу уже в игре,
        /// а ворота и меню всегда рисовались со стопроцентным масштабом.
        /// </summary>
        private void ApplySavedUIScale()
        {
            if (_clientConfig == null)
            {
                return;
            }

            float saved = _clientConfig.Config.Interface.UIScale;

            // Ноль означает «в конфиге ничего нет» — множитель ноль погасил бы
            // весь интерфейс, поэтому такое значение трактуем как штатное.
            ApplyUIScale(saved <= 0f ? 1f : saved);
        }

        private void ApplyUIScale(float scale)
        {
            PanelSettings? panel = _document.panelSettings;
            if (panel == null)
            {
                return;
            }

            // Диапазон тот же, что проверяет ClientConfigManager.
            panel.scale = Mathf.Clamp(scale, 0.5f, 2f);
        }

        private static float ValueOfUIScale(int index)
        {
            return index >= 0 && index < UIScales.Length ? UIScales[index].Value : 1f;
        }

        private static int IndexOfUIScale(float value)
        {
            for (int i = 0; i < UIScales.Length; i++)
            {
                if (Mathf.Abs(UIScales[i].Value - value) < 0.001f)
                {
                    return i;
                }
            }

            return 0;
        }

        private static int IndexOfFrameRate(int value)
        {
            for (int i = 0; i < FrameRates.Length; i++)
            {
                if (FrameRates[i].Value == value)
                {
                    return i;
                }
            }

            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        // Переход в меню
        // ─────────────────────────────────────────────────────────────

        private void GoToMainMenu()
        {
            if (_leaving)
            {
                return;
            }

            _leaving = true;
            _operations.Run("gateway_to_main_menu", LoadMainMenuAsync);
        }

        private async UniTask LoadMainMenuAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            await _sceneNavigator.TransitionAsync(
                MainMenuSceneName,
                linkedCancellation.Token);
        }
    }
}
