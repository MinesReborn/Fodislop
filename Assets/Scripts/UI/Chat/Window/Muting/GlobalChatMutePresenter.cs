#nullable enable

using Kern.Core.Localization;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

internal sealed class GlobalChatMutePresenter
{
    private readonly ChatMuteTracker _muteTracker;
    private readonly ILocalizationService _localization;
    private readonly GlobalChatChannelPresenter _channels;
    private readonly System.Action<string> _addSystemMessage;
    private ChatViewElements? _view;
    private bool _lastMutedState;
    private bool _hasCachedMuteState;

    public GlobalChatMutePresenter(
        ChatMuteTracker muteTracker,
        ILocalizationService localization,
        GlobalChatChannelPresenter channels,
        System.Action<string> addSystemMessage)
    {
        _muteTracker = muteTracker;
        _localization = localization;
        _channels = channels;
        _addSystemMessage = addSystemMessage;
    }

    public void BindView(ChatViewElements view)
    {
        _view = view;
        _hasCachedMuteState = false;
    }

    public void UnbindView()
    {
        _view = null;
        _hasCachedMuteState = false;
    }

    public void ApplyMute(ChatMutePacket packet)
    {
        _muteTracker.ApplyMute(packet, _localization, out string statusMessage, out string notificationMessage);
        _view?.SetMuteStatus(statusMessage);
        Refresh();
        _addSystemMessage(notificationMessage);
    }

    public void Refresh()
    {
        if (_muteTracker.CheckExpiration())
        {
            _view?.SetMuteStatus(string.Empty);
        }

        bool muted = _muteTracker.IsMuted;
        if (_view == null)
        {
            _hasCachedMuteState = false;
            return;
        }

        bool muteChanged = !_hasCachedMuteState || _lastMutedState != muted;
        _lastMutedState = muted;
        _hasCachedMuteState = true;
        if (muteChanged)
        {
            RefreshControls(muted);
        }
    }

    public void RefreshControls(bool muted)
    {
        if (_view == null)
        {
            return;
        }

        // Отправка — Enter из поля: поле доступно, пока нет блокировки и канал известен серверу
        _view.InputField?.SetEnabled(!muted && _channels.CanSendToActiveChannel);
    }
}
