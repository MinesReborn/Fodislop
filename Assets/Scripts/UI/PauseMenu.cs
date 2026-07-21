using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class PauseMenu : MonoBehaviour
    {
        public static bool IsMenuOpen { get; private set; }

        private Color _panelBg = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        private Color _borderColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private Color _accentColor = new Color(0.7f, 0.65f, 0.5f, 1f);
        private Color _btnBg = new Color(0.15f, 0.15f, 0.15f, 1f);
        private Color _btnHover = new Color(0.35f, 0.35f, 0.35f, 1f);
        private Color _btnBorder = new Color(0.4f, 0.4f, 0.4f, 1f);

        private UIDocument _doc;
        private VisualElement _menuPanel;
        private VisualElement _mainPage;
        private VisualElement _settingsPage;
        private bool _isOpen;
        private InputAction _escapeAction;
        private float _originalScale;
        private Button _fullscreenButton;
        private Button _simpleGraphicsButton;
        private Button _headlightButton;

        protected void Start()
        {
            _escapeAction = new InputAction("Escape", binding: "<Keyboard>/escape");
            _escapeAction.performed += _ => ToggleMenu();
            _escapeAction.Enable();

            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[PauseMenu] UIDocument не найден");
                return;
            }

            _originalScale = _doc.panelSettings.scale;

            CreateMenu(_doc.rootVisualElement);
            CloseMenu();

            var savedScale = PlayerPrefs.GetFloat("UIScale", 1f);
            _doc.panelSettings.scale = savedScale;
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                canvas.scaleFactor = savedScale;
            }
        }

        protected void OnDestroy()
        {
            IsMenuOpen = false;

            if (_doc != null && _doc.panelSettings != null)
            {
                _doc.panelSettings.scale = _originalScale;
            }

            _escapeAction?.Dispose();
        }

        private static VisualElement CreateSlider(string labelText, float initialValue, System.Action<float> onChange, float min, float max)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;
            container.style.marginBottom = 12;
            container.style.minWidth = 240;

            var label = new Label(labelText);
            label.style.fontSize = 14;
            label.style.color = Color.white;
            label.style.marginBottom = 4;
            container.Add(label);

            var slider = new Slider(min, max);
            slider.value = initialValue;
            slider.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            container.Add(slider);

            return container;
        }

        private static bool IsSimpleGraphics()
        {
            return PlayerPrefs.GetInt("SimpleGraphics", 0) == 1;
        }

        private static bool IsHeadlightOn()
        {
            return PlayerPrefs.GetInt("UseLight2D", 0) == 1;
        }

        private VisualElement CreateStyledPanel()
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = _panelBg;
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = _borderColor;
            panel.style.borderBottomColor = _borderColor;
            panel.style.borderLeftColor = _borderColor;
            panel.style.borderRightColor = _borderColor;
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 30;
            panel.style.paddingRight = 30;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 260;
            panel.style.maxHeight = new Length(85, LengthUnit.Percent);
            return panel;
        }

        private Label CreateTitle(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 18;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = _accentColor;
            label.style.marginBottom = 16;
            return label;
        }

        private void CreateMenu(VisualElement root)
        {
            _menuPanel = new VisualElement();
            _menuPanel.style.position = Position.Absolute;
            _menuPanel.style.left = 0;
            _menuPanel.style.top = 0;
            _menuPanel.style.right = 0;
            _menuPanel.style.bottom = 0;
            _menuPanel.style.justifyContent = Justify.Center;
            _menuPanel.style.alignItems = Align.Center;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            dimmer.pickingMode = PickingMode.Ignore;
            _menuPanel.Add(dimmer);

            _mainPage = CreateStyledPanel();
            _mainPage.Add(CreateTitle("Меню"));
            _mainPage.Add(CreateButton("Продолжить", CloseMenu));
            _mainPage.Add(CreateButton("Настройки", OpenSettings));
            _mainPage.Add(CreateButton("Выйти", QuitGame));
            _menuPanel.Add(_mainPage);

            _settingsPage = CreateStyledPanel();
            _settingsPage.style.maxWidth = 360;
            _settingsPage.Add(CreateTitle("Настройки"));

            var scrollContainer = new ScrollView(ScrollViewMode.Vertical);
            scrollContainer.style.width = new Length(100, LengthUnit.Percent);
            scrollContainer.style.maxHeight = 400;

            // Настройка громкости всех 6 шин FMOD
            scrollContainer.Add(CreateAudioSlider("Общая громкость", AudioBusType.Master, "Audio_Master", 1f));
            scrollContainer.Add(CreateAudioSlider("Звуковые эффекты", AudioBusType.SFX, "Audio_SFX", 1f));
            scrollContainer.Add(CreateAudioSlider("Музыка", AudioBusType.Music, "Audio_Music", 0.5f));
            scrollContainer.Add(CreateAudioSlider("Эмбиент", AudioBusType.Ambience, "Audio_Ambience", 0.7f));
            scrollContainer.Add(CreateAudioSlider("Голос / Диалоги", AudioBusType.Voice, "Audio_Voice", 1f));
            scrollContainer.Add(CreateAudioSlider("Интерфейс", AudioBusType.UI, "Audio_UI", 1f));

            scrollContainer.Add(CreateSlider(
                "Масштаб UI",
                PlayerPrefs.GetFloat("UIScale", 1f),
                v =>
                {
                    PlayerPrefs.SetFloat("UIScale", v);
                    PlayerPrefs.Save();
                    _doc.panelSettings.scale = v;
                    foreach (var canvas in FindObjectsByType<Canvas>())
                    {
                        canvas.scaleFactor = v;
                    }
                },
                0.65f,
                2f));

            var fsLabel = new Label("Экран");
            fsLabel.style.fontSize = 14;
            fsLabel.style.color = Color.white;
            fsLabel.style.marginBottom = 4;
            scrollContainer.Add(fsLabel);

            _fullscreenButton = new Button(ToggleFullscreen);
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
            _fullscreenButton.style.backgroundColor = _btnBg;
            _fullscreenButton.style.borderTopWidth = 2;
            _fullscreenButton.style.borderBottomWidth = 2;
            _fullscreenButton.style.borderLeftWidth = 2;
            _fullscreenButton.style.borderRightWidth = 2;
            _fullscreenButton.style.borderTopColor = _btnBorder;
            _fullscreenButton.style.borderBottomColor = _btnBorder;
            _fullscreenButton.style.borderLeftColor = _btnBorder;
            _fullscreenButton.style.borderRightColor = _btnBorder;
            _fullscreenButton.style.paddingTop = 8;
            _fullscreenButton.style.paddingBottom = 8;
            _fullscreenButton.style.paddingLeft = 20;
            _fullscreenButton.style.paddingRight = 20;
            _fullscreenButton.style.marginBottom = 16;
            _fullscreenButton.style.minWidth = 180;
            _fullscreenButton.style.color = Color.white;
            _fullscreenButton.style.fontSize = 14;
            _fullscreenButton.style.unityTextAlign = TextAnchor.MiddleCenter;

            _fullscreenButton.RegisterCallback<MouseEnterEvent>(_ =>
            {
                _fullscreenButton.style.backgroundColor = _btnHover;
            });
            _fullscreenButton.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                _fullscreenButton.style.backgroundColor = _btnBg;
            });

            scrollContainer.Add(_fullscreenButton);

            var sgLabel = new Label("Графика");
            sgLabel.style.fontSize = 14;
            sgLabel.style.color = Color.white;
            sgLabel.style.marginBottom = 4;
            scrollContainer.Add(sgLabel);

            _simpleGraphicsButton = new Button(ToggleSimpleGraphics);
            _simpleGraphicsButton.text = IsSimpleGraphics() ? "Простая" : "Обычная";
            scrollContainer.Add(_simpleGraphicsButton);

            var hlLabel = new Label("Фары");
            hlLabel.style.fontSize = 14;
            hlLabel.style.color = Color.white;
            hlLabel.style.marginBottom = 4;
            scrollContainer.Add(hlLabel);

            _headlightButton = new Button(ToggleHeadlight);
            _headlightButton.text = IsHeadlightOn() ? "Вкл" : "Выкл";
            scrollContainer.Add(_headlightButton);

            _settingsPage.Add(scrollContainer);
            _settingsPage.Add(CreateButton("Назад", CloseSettings));
            _settingsPage.style.display = DisplayStyle.None;
            _menuPanel.Add(_settingsPage);

            root.Add(_menuPanel);
        }

        private static VisualElement CreateAudioSlider(string title, AudioBusType busType, string prefKey, float defaultValue)
        {
            float currentVol = AudioSystem.Instance != null ? AudioSystem.Instance.GetBusVolume(busType) : PlayerPrefs.GetFloat(prefKey, defaultValue);
            return CreateSlider(
                title,
                currentVol,
                v =>
                {
                    AudioSystem.Instance?.SetBusVolume(busType, v);
                    PlayerPrefs.SetFloat(prefKey, v);
                    PlayerPrefs.Save();
                },
                0f,
                1f);
        }

        private Button CreateButton(string text, System.Action action)
        {
            var btn = new Button(action);
            btn.text = text;
            btn.style.backgroundColor = _btnBg;
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _btnBorder;
            btn.style.borderBottomColor = _btnBorder;
            btn.style.borderLeftColor = _btnBorder;
            btn.style.borderRightColor = _btnBorder;
            btn.style.paddingTop = 8;
            btn.style.paddingBottom = 8;
            btn.style.paddingLeft = 20;
            btn.style.paddingRight = 20;
            btn.style.marginBottom = 10;
            btn.style.minWidth = 180;
            btn.style.color = Color.white;
            btn.style.fontSize = 14;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                btn.style.backgroundColor = _btnHover;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                btn.style.backgroundColor = _btnBg;
            });
            return btn;
        }

        private void ToggleMenu()
        {
            if (!enabled)
            {
                return;
            }

            if (PacketHandler.IsInputBlocked && !_isOpen)
            {
                var topTag = PacketHandler.TopWindowTag;
                if (topTag != null)
                {
                    NetworkService.Send(new ElementClickPacket(topTag, 0, System.Array.Empty<StringPairPacket>()));
                }

                return;
            }

            if (_settingsPage.style.display == DisplayStyle.Flex)
            {
                CloseSettings();
                return;
            }

            if (_isOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            Debug.Log($"[PauseMenu] Fullscreen: {Screen.fullScreen}");
            _fullscreenButton.text = Screen.fullScreen ? "Полный экран" : "Оконный";
        }

        private void ToggleSimpleGraphics()
        {
            var terrain = FindAnyObjectByType<SingleMeshTerrainRenderer>();
            if (terrain == null)
            {
                return;
            }

            bool current = PlayerPrefs.GetInt("SimpleGraphics", 0) == 1;
            terrain.SetSimpleGraphics(!current);
            _simpleGraphicsButton.text = !current ? "Простая" : "Обычная";
        }

        private void ToggleHeadlight()
        {
            bool current = PlayerPrefs.GetInt("UseLight2D", 0) == 1;
            bool newValue = !current;

            var terrain = FindAnyObjectByType<SingleMeshTerrainRenderer>();
            if (terrain != null)
            {
                terrain.SetUseLight2D(newValue);
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var headlight = player.GetComponent<RobotHeadlight>();
                if (headlight == null && newValue)
                {
                    headlight = player.AddComponent<RobotHeadlight>();
                }

                if (headlight != null)
                {
                    headlight.SetEnabled(newValue);
                }
            }

            _headlightButton.text = newValue ? "Вкл" : "Выкл";
        }

        private void OpenMenu()
        {
            _isOpen = true;
            IsMenuOpen = true;
            _menuPanel.style.display = DisplayStyle.Flex;
            _mainPage.style.display = DisplayStyle.Flex;
            _settingsPage.style.display = DisplayStyle.None;
        }

        private void CloseMenu()
        {
            _isOpen = false;
            IsMenuOpen = false;
            _menuPanel.style.display = DisplayStyle.None;
        }

        private void OpenSettings()
        {
            _mainPage.style.display = DisplayStyle.None;
            _settingsPage.style.display = DisplayStyle.Flex;
        }

        private void CloseSettings()
        {
            _settingsPage.style.display = DisplayStyle.None;
            _mainPage.style.display = DisplayStyle.Flex;
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("[PauseMenu] Выход из игры");
#else
            Application.Quit();
#endif
        }
    }
}
