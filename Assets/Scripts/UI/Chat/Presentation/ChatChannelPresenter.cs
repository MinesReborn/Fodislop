#nullable enable

using Kern.Core.Localization;
using UnityEngine.UIElements;

namespace Kern.UI;

internal static class ChatChannelPresenter
{
    public static void UpdatePresentation(
        Label? header,
        Button? globalButton,
        ILocalizationService? loc)
    {
        if (header != null && loc != null)
        {
            header.text = loc.Get("chat.channel.global");
        }

        globalButton?.EnableInClassList("gchat-channel-button--active", true);
    }
}
