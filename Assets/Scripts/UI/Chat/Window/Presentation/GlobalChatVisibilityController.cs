#nullable enable

using System;
using Kern.Networking.Gateways;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

// Режимы глобального чата (макет visual/kern-ui js/hud.js setChat): 0 — три последние строки,
// 1 — окно на их месте, 2 — скрыт. Tab листает по кругу; три строки открывают окно нажатием, «свернуть» — обратно.
internal sealed class GlobalChatVisibilityController
{
    public enum Mode
    {
        Peek = 0,
        Open = 1,
        Hidden = 2,
    }

    private const string ChatOpenClass = "ui--chat-open";

    private readonly ChatEventGateway _chatEvents;
    private readonly UIDocument _document;
    private readonly GlobalChatMessagePresenter _messages;
    private readonly Func<ChatViewElements?> _viewProvider;
    private readonly Func<string> _activeTag;
    private readonly Action _closeChatList;

    public GlobalChatVisibilityController(
        ChatEventGateway chatEvents,
        UIDocument document,
        GlobalChatMessagePresenter messages,
        Func<ChatViewElements?> viewProvider,
        Func<string> activeTag,
        Action closeChatList)
    {
        _chatEvents = chatEvents;
        _document = document;
        _messages = messages;
        _viewProvider = viewProvider;
        _activeTag = activeTag;
        _closeChatList = closeChatList;
    }

    public Mode Current { get; private set; } = Mode.Peek;

    public bool IsOpen => Current == Mode.Open;

    // Tab: по кругу 0 → 1 → 2 → 0
    public void Cycle() => Set((Mode)(((int)Current + 1) % 3));

    // Кнопка «Чат» на пальце: окно или три строки
    public void ToggleOpen() => Set(IsOpen ? Mode.Peek : Mode.Open);

    public void Show() => Set(Mode.Open);

    public void Hide() => Set(Mode.Peek);

    public void Set(Mode mode)
    {
        bool wasOpen = IsOpen;
        Current = mode;
        ChatViewElements? view = _viewProvider();
        if (view?.Peek != null)
        {
            view.Peek.style.display = mode == Mode.Peek ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (view?.Panel != null)
        {
            view.Panel.style.display = mode == Mode.Open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        SetChatOpenClass(mode == Mode.Open);
        if (mode == Mode.Open)
        {
            view?.Tree.BringToFront();
            _chatEvents.SetFocusedTag(_activeTag());
            _messages.ScrollToBottom();
            view?.InputField?.Focus();
            return;
        }

        if (!wasOpen)
        {
            return;
        }

        _closeChatList();
        _chatEvents.SetFocusedTag(null);
        if (view?.InputField != null)
        {
            view.InputField.value = string.Empty;
            view.InputField.Blur();
        }
    }

    private void SetChatOpenClass(bool isOpen) =>
        _document.rootVisualElement?.panel?.visualTree.EnableInClassList(ChatOpenClass, isOpen);
}
