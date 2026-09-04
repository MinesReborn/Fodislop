#nullable enable

using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI;

internal static class ChatChannelPresenter
{
    public static void UpdatePresentation(
        ChatChannel activeChannel,
        Label? header,
        Button? globalButton,
        Button? localButton,
        Button? colorButton,
        ILocalizationService? loc)
    {
        bool local = activeChannel == ChatChannel.Local;
        if (header != null && loc != null)
        {
            header.text = loc.Get(local ? "chat.channel.local" : "chat.channel.global");
        }

        globalButton?.EnableInClassList("gchat-channel-button--active", !local);
        localButton?.EnableInClassList("gchat-channel-button--active", local);
        if (colorButton != null)
        {
            colorButton.style.display = local ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
