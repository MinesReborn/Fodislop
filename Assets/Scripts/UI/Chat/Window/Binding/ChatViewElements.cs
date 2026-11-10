#nullable enable

using System;
using Kern.Core.Localization;
using UnityEngine.UIElements;

using Kern.UI.Localization;

namespace Kern.UI.Chat;

internal sealed class ChatViewElements
{
    public VisualElement Tree { get; }

    // Свёрнутый чат: три последние строки (режим 0)
    public Button? Peek { get; }

    public Label? PeekTag { get; }

    public VisualElement? PeekLines { get; }

    // Окно чата (режим 1)
    public VisualElement? Panel { get; }

    public Label? ChatHeader { get; }

    public Button? ListButton { get; }

    public Label? ListBadge { get; }

    public Button? CloseButton { get; }

    public ScrollView? ScrollView { get; }

    public Label? MuteStatus { get; }

    public TextField? InputField { get; }

    public ScrollView? ChatListScroll { get; }

    public ChatInputBlinker? Blinker { get; }

    public ChatViewElements(VisualElement tree, ILocalizationService? loc)
    {
        Tree = tree;
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;

        if (loc != null)
        {
            UILocalizer.Apply(tree, loc);
        }

        Peek = tree.Q<Button>("ChatPeek");
        PeekTag = tree.Q<Label>("ChatPeekTag");
        PeekLines = tree.Q<VisualElement>("ChatPeekLines");
        Panel = tree.Q<VisualElement>("ChatPanel");
        if (Panel != null)
        {
            Panel.style.display = DisplayStyle.None;
        }

        ChatHeader = tree.Q<Label>("ChatHeader");
        ListButton = tree.Q<Button>("ChatListButton");
        ListBadge = tree.Q<Label>("ChatListBadge");
        CloseButton = tree.Q<Button>("ChatCloseButton");
        MuteStatus = tree.Q<Label>("ChatMuteStatus");
        ScrollView = tree.Q<ScrollView>("ChatScroll");
        InputField = tree.Q<TextField>("ChatInput");
        ChatListScroll = tree.Q<ScrollView>("ChatListScroll");

        var internalInput = InputField != null
            ? InputField.Q<VisualElement>(className: "unity-text-field__input")
            : null;
        if (InputField != null && internalInput != null)
        {
            Blinker = new ChatInputBlinker(InputField, internalInput);
        }
    }

    public void BindActions(Action toggleChatList, Action collapse, Action open)
    {
        if (ListButton != null)
        {
            ListButton.clicked += toggleChatList;
        }

        if (CloseButton != null)
        {
            CloseButton.clicked += collapse;
        }

        if (Peek != null)
        {
            Peek.clicked += open;
        }
    }

    public void SetMuteStatus(string message)
    {
        if (MuteStatus == null)
        {
            return;
        }

        MuteStatus.text = message;
        MuteStatus.style.display = string.IsNullOrEmpty(message)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }
}
