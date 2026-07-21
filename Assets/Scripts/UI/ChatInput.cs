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

            if (PlayerMovementController.LocalPlayer != null)
            {
                PlayerMovementController.LocalPlayer.enabled = false;
            }

            var worldMap = Object.FindAnyObjectByType<WorldMapController>();
            if (worldMap != null)
            {
                worldMap.enabled = false;
            }

            var pauseMenu = Object.FindAnyObjectByType<PauseMenu>();
            if (pauseMenu != null)
            {
                pauseMenu.enabled = false;
            }
        }

        public static void OnBlur()
        {
            IsFocused = false;

            if (PlayerMovementController.LocalPlayer != null)
            {
                PlayerMovementController.LocalPlayer.enabled = true;
            }

            var worldMap = Object.FindAnyObjectByType<WorldMapController>();
            if (worldMap != null)
            {
                worldMap.enabled = true;
            }

            var pauseMenu = Object.FindAnyObjectByType<PauseMenu>();
            if (pauseMenu != null)
            {
                pauseMenu.enabled = true;
            }
        }
    }
}
