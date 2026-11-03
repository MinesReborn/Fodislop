#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Networking.Gateways;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

internal sealed class GlobalChatVisibilityController
{
    private readonly ChatEventGateway _chatEvents;
    private readonly UIDocument _document;
    private readonly GlobalChatMessagePresenter _messages;
    private readonly Func<ChatViewElements?> _viewProvider;
    private readonly Func<ChatWindowInteractionController?> _interactionProvider;
    private readonly Func<string> _activeTag;
    private readonly Action _refreshToggleState;

    public GlobalChatVisibilityController(
        ChatEventGateway chatEvents,
        UIDocument document,
        GlobalChatMessagePresenter messages,
        Func<ChatViewElements?> viewProvider,
        Func<ChatWindowInteractionController?> interactionProvider,
        Func<string> activeTag,
        Action refreshToggleState)
    {
        _chatEvents = chatEvents;
        _document = document;
        _messages = messages;
        _viewProvider = viewProvider;
        _interactionProvider = interactionProvider;
        _activeTag = activeTag;
        _refreshToggleState = refreshToggleState;
    }

    public bool IsOpen { get; private set; }

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        IsOpen = true;
        _refreshToggleState();
        ChatViewElements? view = _viewProvider();
        if (view?.Panel != null)
        {
            view.Panel.style.display = DisplayStyle.Flex;
        }

        view?.Tree.BringToFront();
        SetChatOpenClass(true);
        _chatEvents.SetFocusedTag(_activeTag());
        _messages.ScrollToBottom();
        view?.InputField?.Focus();
    }

    public void Hide()
    {
        IsOpen = false;
        SetChatOpenClass(false);
        _refreshToggleState();
        ChatViewElements? view = _viewProvider();
        if (view?.Panel != null)
        {
            view.Panel.style.display = DisplayStyle.None;
        }

        _chatEvents.SetFocusedTag(null);
        _interactionProvider()?.OnPanelHidden();
        if (view?.InputField != null)
        {
            view.InputField.value = string.Empty;
            view.InputField.Blur();
        }
    }

    private void SetChatOpenClass(bool isOpen) =>
        _document.rootVisualElement?.panel?.visualTree.EnableInClassList("ui--chat-open", isOpen);
}
