using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Networking;
using Fodinae.Assets.Scripts.Networking.Connection;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.GUI;
using System;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MainMenu : MonoBehaviour
{
    [SerializeField] private Texture2D _loaderTexture;
    private UIDocument _doc;
    private VisualElement _mainMenuContainer;
    private VisualElement _loaderContainer;
    private bool _hasShownLoader = false;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;
        root.style.justifyContent = Justify.Center;
        root.style.alignItems = Align.Center;
        ShowLoader();
        var mainMenuUXML = Resources.Load<VisualTreeAsset>("UI/MainMenu");
        if (mainMenuUXML == null)
        {
            Debug.LogError("MainMenu.uxml не найден в Resources/UI/");
            return;
        }
        var mainMenu = mainMenuUXML.CloneTree();
        _mainMenuContainer = mainMenu.Q<VisualElement>("MainMenuContainer");
        var playButton = mainMenu.Q<Button>("PlayButton");
        if (playButton != null)
            playButton.clicked += OnPlayButtonClicked;
        root.Add(mainMenu);

        // The loader is a full-screen, absolutely-positioned background overlay
        // that stays visible until the player presses PLAY (see HideLoader).
        // The menu must therefore sit *above* the loader and the loader must
        // not intercept pointer events, otherwise the PLAY button is occluded
        // and unclickable.
        mainMenu.style.position = Position.Absolute;
        mainMenu.style.left = 0;
        mainMenu.style.top = 0;
        mainMenu.style.right = 0;
        mainMenu.style.bottom = 0;
        mainMenu.BringToFront();
        if (_loaderContainer != null)
            _loaderContainer.pickingMode = PickingMode.Ignore;
    }

    private void ShowLoader()
    {
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
            Debug.LogWarning("Картинка не найдена, используется заглушка");
        }

        image.image = loaderTexture;
        image.style.width = new Length(100, LengthUnit.Percent);
        image.style.height = new Length(100, LengthUnit.Percent);
        image.scaleMode = ScaleMode.ScaleAndCrop; // покрывает весь элемент, сохраняя пропорции

        _loaderContainer.Add(image);
        root.Add(_loaderContainer);
        _hasShownLoader = true;

        Debug.Log("✅ Лоадер показан");
    }

    private Texture2D CreateSimpleLoaderTexture()
    {
        int width = 1920;
        int height = 1080;

        Texture2D texture = new Texture2D(width, height);

        // Заливаем чёрным
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.black;
        }

        // Рисуем белый круг в центре
        int centerX = width / 2;
        int centerY = height / 2;
        int radius = 150;

        for (int y = -radius; y < radius; y++)
        {
            for (int x = -radius; x < radius; x++)
            {
                if (x * x + y * y < radius * radius)
                {
                    int px = centerX + x;
                    int py = centerY + y;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        pixels[py * width + px] = Color.white;
                    }
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    private void HideLoader()
    {
        if (_hasShownLoader && _loaderContainer != null)
        {
            _loaderContainer.RemoveFromHierarchy();
            _hasShownLoader = false;
            Debug.Log("✅ Лоадер скрыт");
        }
    }

    private void HideMenu()
    {
        if (_mainMenuContainer != null)
        {
            _mainMenuContainer.style.display = DisplayStyle.None;
            Debug.Log("✅ Меню скрыто");
        }
    }

    private void OnPlayButtonClicked()
    {
        Debug.Log("🔘 Нажата кнопка Play");

        RobotManager.ShowDebugVisuals = true;

        // Скрываем лоадер
        HideLoader();

        // Скрываем меню с кнопкой
        HideMenu();

        // Выполняем остальную логику
        if (ConnectionManager.Instance.Connection == null ||
            ConnectionManager.Instance.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Disconnected)
        {
            ConnectionManager.Instance.Connect();
        }
        //NetworkService.Instance.Send(new OpenHelpClickPacket());
    }
}
