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
    }

    private void ShowLoader()
    {
        var root = _doc.rootVisualElement;

        _loaderContainer = new VisualElement();
        _loaderContainer.name = "LoaderContainer";
        _loaderContainer.style.position = Position.Absolute;
        _loaderContainer.style.top = 0;
        _loaderContainer.style.left = 0;
        _loaderContainer.style.right = 0;
        _loaderContainer.style.bottom = 0;
        _loaderContainer.style.backgroundColor = Color.black;
        _loaderContainer.style.alignItems = Align.Center;
        _loaderContainer.style.justifyContent = Justify.Center;

        var image = new UnityEngine.UIElements.Image();
        Texture2D loaderTexture = null;
        loaderTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/loader_new.png");
        if (loaderTexture == null)
        {
            loaderTexture = CreateSimpleLoaderTexture();
            Debug.LogWarning("Картинка не найдена по пути Assets/Textures/loader_new.png, используется заглушка");
        }

        image.image = loaderTexture;
        image.style.width = 1920;
        image.style.height = 1080;
        image.style.position = Position.Relative;

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
        SendPacket(new OpenHelpClickPacket());
    }

    private void SendPacket(IRootClientPacket packet)
    {
        if (ConnectionManager.Instance != null && ConnectionManager.Instance.Connection != null)
        {
            ConnectionManager.Instance.Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, packet));
        }
        else
        {
            Debug.LogError("Cannot send packet: ConnectionManager or Connection is null");
        }
    }
}