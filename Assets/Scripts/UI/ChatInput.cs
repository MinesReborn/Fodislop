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

            var pc = Object.FindObjectOfType<PlayerMovementController>();
            if (pc != null) pc.enabled = false;

            var wm = Object.FindObjectOfType<WorldMapController>();
            if (wm != null) wm.enabled = false;

            var pm = Object.FindObjectOfType<PauseMenu>();
            if (pm != null) pm.enabled = false;
        }

        public static void OnBlur()
        {
            IsFocused = false;

            var pc = Object.FindObjectOfType<PlayerMovementController>();
            if (pc != null) pc.enabled = true;

            var wm = Object.FindObjectOfType<WorldMapController>();
            if (wm != null) wm.enabled = true;

            var pm = Object.FindObjectOfType<PauseMenu>();
            if (pm != null) pm.enabled = true;
        }
    }
}
