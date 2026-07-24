using System;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        [SerializeField]
        private Texture2D _loaderTexture;
        private UIDocument _doc;
        private VisualElement _mainMenuContainer;
        private VisualElement _loaderContainer;
        private bool _hasShownLoader = false;
        private Button _playButton;

        protected void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            if (_doc == null || _doc.rootVisualElement == null)
            {
                Debug.LogError("[MainMenu] UIDocument component or rootVisualElement not ready on MainMenu GameObject");
                return;
            }

            var root = _doc.rootVisualElement;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            ShowLoader();

            var mainMenuUXML = Resources.Load<VisualTreeAsset>("UI/MainMenu");
            if (mainMenuUXML == null)
            {
                Debug.LogError("[MainMenu] MainMenu.uxml not found in Resources/UI/");
                return;
            }

            var mainMenu = mainMenuUXML.CloneTree();
            if (mainMenu == null)
            {
                Debug.LogError("[MainMenu] Failed to clone MainMenu.uxml tree");
                return;
            }

            _mainMenuContainer = mainMenu.Q<VisualElement>("MainMenuContainer");
            _playButton = mainMenu.Q<Button>("PlayButton");
            if (_playButton != null)
            {
                _playButton.clicked += OnPlayButtonClicked;
            }

            root.Add(mainMenu);

            mainMenu.style.position = Position.Absolute;
            mainMenu.style.left = 0;
            mainMenu.style.top = 0;
            mainMenu.style.right = 0;
            mainMenu.style.bottom = 0;
            mainMenu.BringToFront();
            if (_loaderContainer != null)
            {
                _loaderContainer.pickingMode = PickingMode.Ignore;
            }

            // UI Toolkit иногда не регистрирует ивенты при старте — форсируем пересоздание панэли
            if (_doc != null && _doc.panelSettings != null)
            {
                var ps = _doc.panelSettings;
                _doc.panelSettings = null;
                _doc.panelSettings = ps;
            }
        }

        protected void OnDisable()
        {
            if (_playButton != null)
            {
                _playButton.clicked -= OnPlayButtonClicked;
            }
        }

        private void ShowLoader()
        {
            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            var root = _doc.rootVisualElement;
            root.style.width = new Length(100, LengthUnit.Percent);
            root.style.height = new Length(100, LengthUnit.Percent);

            _loaderContainer = new VisualElement();
            _loaderContainer.name = "LoaderContainer";
            _loaderContainer.style.position = Position.Absolute;
            _loaderContainer.style.top = 0;
            _loaderContainer.style.left = 0;
            _loaderContainer.style.right = 0;
            _loaderContainer.style.bottom = 0;
            _loaderContainer.style.alignItems = Align.Stretch;
            _loaderContainer.style.justifyContent = Justify.Center;

            var image = new UnityEngine.UIElements.Image();
            Texture2D loaderTexture = _loaderTexture;
            if (loaderTexture == null)
            {
                loaderTexture = CreateSimpleLoaderTexture();
                Debug.LogWarning("[MainMenu] Loader texture not assigned, using placeholder");
            }

            image.image = loaderTexture;
            image.style.width = new Length(100, LengthUnit.Percent);
            image.style.height = new Length(100, LengthUnit.Percent);
            image.scaleMode = ScaleMode.ScaleAndCrop; // покрывает весь элемент, сохраняя пропорции

            _loaderContainer.Add(image);
            root.Add(_loaderContainer);
            _hasShownLoader = true;

            Debug.Log("[MainMenu] Loader shown");
        }

        private static Texture2D CreateSimpleLoaderTexture()
        {
            const int width = 192;
            const int height = 108;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            Color32 black = Color.black;
            Color32 white = Color.white;
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = black;
            }

            const int CENTER_X = width / 2;
            const int CENTER_Y = height / 2;
            const int radius = 15;

            for (int y = -radius; y < radius; y++)
            {
                for (int x = -radius; x < radius; x++)
                {
                    if ((x * x) + (y * y) < radius * radius)
                    {
                        int px = CENTER_X + x;
                        int py = CENTER_Y + y;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            pixels[(py * width) + px] = white;
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        private void HideLoader()
        {
            if (_hasShownLoader && _loaderContainer != null)
            {
                _loaderContainer.RemoveFromHierarchy();
                _hasShownLoader = false;
                Debug.Log("[MainMenu] Loader hidden");
            }
        }

        private void HideMenu()
        {
            if (_mainMenuContainer != null)
            {
                _mainMenuContainer.style.display = DisplayStyle.None;
                Debug.Log("[MainMenu] Menu hidden");
            }
        }

        private void OnPlayButtonClicked()
        {
            Debug.Log("[MainMenu] Play button clicked");

            // Скрываем лоадер
            HideLoader();

            // Скрываем меню с кнопкой
            HideMenu();

            // Выполняем остальную логику
            if (ConnectionManager.Instance != null && (ConnectionManager.Instance.Connection == null ||
                ConnectionManager.Instance.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Disconnected))
            {
                ConnectionManager.Instance.Connect(oldClient: false);
            }
        }

        private void OnOldClientButtonClicked()
        {
            Debug.Log("[MainMenu] Old client button clicked");
            RobotManager.ShowDebugVisuals = false;
            HideLoader();
            HideMenu();
            ConnectionManager.Instance.Connect(oldClient: true);
        }
    }
}
