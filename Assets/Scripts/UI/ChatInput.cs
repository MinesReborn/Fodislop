using Fodinae.Scripts.Player;
using UnityEngine;

namespace Fodinae.Scripts.UI
{
    public static class ChatInput
    {
        public static bool IsFocused { get; private set; }

        public static void OnFocus()
        {
            IsFocused = true;
        }

        public static void OnBlur()
        {
            IsFocused = false;
        }
    }
}
