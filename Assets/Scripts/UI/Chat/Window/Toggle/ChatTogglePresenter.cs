#nullable enable

using Kern.AssetPipeline.Loading;
using Kern.Core.Localization;
using Kern.Networking.Gateways;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

internal sealed class ChatTogglePresenter : System.IDisposable
{
    private const string ChatIconResource = "UI/Chat/Global/Icons/ChatIcon";
    private readonly UIDocument _document;
    private readonly ILocalizationService _localization;
    private readonly ChatEventGateway _chatEvents;
    private Button? _button;
    private Label? _unreadBadge;

    public ChatTogglePresenter(
        UIDocument document,
        ILocalizationService localization,
        ChatEventGateway chatEvents)
    {
        _document = document;
        _localization = localization;
        _chatEvents = chatEvents;
        _chatEvents.UnreadChanged += UpdateUnreadBadge;
    }

    public void Create(System.Action onToggle)
    {
        if (_button != null)
        {
            return;
        }

        var button = new Button(onToggle);
        button.AddToClassList("chat-toggle-btn");
        button.tooltip = _localization.Get("chat.toggle_tooltip");
        Texture2D? icon = Resources.Load<Texture2D>(ChatIconResource);
        if (icon != null)
        {
            RuntimeTextureFactory.ApplySampling(icon, FilterMode.Point, TextureWrapMode.Clamp);
            button.style.backgroundImage = new StyleBackground(icon);
        }

        var unreadBadge = new Label { name = "ChatUnreadBadge" };
        unreadBadge.AddToClassList("chat-toggle-badge");
        unreadBadge.pickingMode = PickingMode.Ignore;
        UIState.Hide(unreadBadge);
        button.Add(unreadBadge);
        _unreadBadge = unreadBadge;
        _button = button;
        _document.rootVisualElement.Add(button);
        UpdateUnreadBadge();
    }

    public void SetActive(bool isActive) => _button?.EnableInClassList("chat-toggle-btn--active", isActive);

    public void Dispose()
    {
        _chatEvents.UnreadChanged -= UpdateUnreadBadge;
        _button?.RemoveFromHierarchy();
        _button = null;
    }

    private void UpdateUnreadBadge()
    {
        if (_unreadBadge == null)
        {
            return;
        }

        int total = _chatEvents.GetTotalUnread();
        if (total <= 0)
        {
            UIState.Hide(_unreadBadge);
            return;
        }

        _unreadBadge.text = total > 99
            ? "99+"
            : total.ToString(System.Globalization.CultureInfo.InvariantCulture);
        UIState.Show(_unreadBadge);
    }
}
