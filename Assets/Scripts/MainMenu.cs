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

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();

        var root = _doc.rootVisualElement;
        root.style.justifyContent = Justify.Center;
        root.style.alignItems = Align.Center;

        var mainMenuUXML = Resources.Load<VisualTreeAsset>("UI/MainMenu");
        
        var mainMenu = mainMenuUXML.CloneTree();
        _mainMenuContainer = mainMenu.Q<VisualElement>("MainMenuContainer");

        var playButton = mainMenu.Q<Button>("PlayButton");
        playButton.clicked += OnPlayButtonClicked;

        var playComplexButton = mainMenu.Q<Button>("PlayComplexButton");
        playComplexButton.clicked += OnPlayComplexButtonClicked;

        root.Add(mainMenu);
    }

    private void OnPlayButtonClicked()
    {
        if (ConnectionManager.Instance.Connection == null || ConnectionManager.Instance.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Disconnected)
        {
            ConnectionManager.Instance.Connect();
        }
        SendPacket(new OpenHelpClickPacket());
        // Optionally, hide the main menu after clicking play
        _mainMenuContainer.style.display = DisplayStyle.None;
    }

    private void OnPlayComplexButtonClicked()
    {
        if (ConnectionManager.Instance.Connection == null || ConnectionManager.Instance.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Disconnected)
        {
            ConnectionManager.Instance.Connect();
        }
        SendPacket(new OpenSettingsClickPacket());
        // Optionally, hide the main menu after clicking play
        _mainMenuContainer.style.display = DisplayStyle.None;
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