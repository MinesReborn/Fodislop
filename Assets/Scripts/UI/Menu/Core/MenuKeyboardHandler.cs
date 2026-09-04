#nullable enable

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.UI;

/// <summary>
/// Handles keyboard shortcuts (Escape to close modal or cancel, Enter to start game) for MainMenu.
/// </summary>
public static class MenuKeyboardHandler
{
    public static void HandleInput(
        MenuModalManager modalManager,
        bool loadingActive,
        Action onPlay,
        Action onCancelDescent)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (modalManager.HasActiveModal)
            {
                modalManager.CloseCurrentModal();
            }
            else if (loadingActive)
            {
                onCancelDescent();
            }
        }
        else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            if (!modalManager.HasActiveModal && !loadingActive)
            {
                onPlay();
            }
        }
    }
}
