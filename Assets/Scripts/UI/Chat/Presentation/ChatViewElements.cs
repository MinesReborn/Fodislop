#nullable enable

using System;
using Kern.Core.Localization;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class ChatViewElements
{
    public VisualElement Tree { get; }

    public VisualElement? Panel { get; }

    public ScrollView? ScrollView { get; }

    public Label? MuteStatus { get; }

    public TextField? InputField { get; }

    public Label? ChatHeader { get; }

    public Button? GlobalChannelButton { get; }

    public Button? SendButton { get; }

    public Button? ColorButton { get; }

    public VisualElement? ColorGrid { get; }

    public Controls.ChatInputBlinker? Blinker { get; }

    public ChatViewElements(VisualElement tree, ILocalizationService? loc)
    {
        Tree = tree;
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;

        if (loc != null)
        {
            UILocalizer.Apply(tree, loc);
        }

        Panel = tree.Q<VisualElement>("ChatPanel");
        if (Panel != null)
        {
            Panel.style.display = DisplayStyle.None;
        }

        MuteStatus = tree.Q<Label>("ChatMuteStatus");
        ScrollView = tree.Q<ScrollView>("ChatScroll");
        InputField = tree.Q<TextField>("ChatInput");
        ChatHeader = tree.Q<Label>("ChatHeader");
        GlobalChannelButton = tree.Q<Button>("GlobalChannelButton");
        SendButton = tree.Q<Button>("SendButton");
        ColorButton = tree.Q<Button>("ColorButton");
        ColorGrid = tree.Q<VisualElement>("ColorGrid");

        var internalInput = InputField != null
            ? InputField.Q<VisualElement>(className: "unity-text-field__input")
            : null;
        if (internalInput != null)
        {
            internalInput.AddToClassList("gchat-internal-input");
        }

        if (InputField != null && internalInput != null)
        {
            Blinker = new Controls.ChatInputBlinker(InputField, internalInput);
        }
    }

    public void BindActions(Action onSend, Action selectGlobal)
    {
        if (SendButton != null)
        {
            SendButton.clicked += onSend;
        }

        if (GlobalChannelButton != null)
        {
            GlobalChannelButton.clicked += selectGlobal;
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
