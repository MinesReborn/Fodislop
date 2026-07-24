using UnityEngine;

namespace Fodinae.Scripts.Player.Interfaces
{
    public interface IPlayerInput
    {
        Vector2 MoveInput { get; }
        bool WantsToToggleAutoDig { get; }
        bool WantsToToggleAggression { get; }
        bool WantsToDig { get; }
        bool IsShiftPressed { get; }
        void SetMovementInput(Vector2 input);
    }
}
