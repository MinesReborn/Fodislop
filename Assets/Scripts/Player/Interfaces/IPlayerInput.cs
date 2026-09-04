#nullable enable

using UnityEngine;

namespace Fodinae.Player.Interfaces
{
    public interface IPlayerInput
    {
        Vector2 MoveInput { get; }
        bool WantsToToggleAutoDig { get; }
        bool WantsToToggleAggression { get; }
        bool WantsToDig { get; }
        bool WantsToGeo { get; }
        bool WantsToHeal { get; }

        /// <summary>
        /// Клавиша лечения зажата прямо сейчас.
        /// </summary>
        /// <remarks>
        /// Отдельно от <see cref="WantsToHeal"/>: то — нажатие в этом кадре,
        /// по нему уходит пакет лечения, и оно истинно ровно один кадр. Аура
        /// же горит всё время, пока клавишу держат.
        /// </remarks>
        bool IsHealHeld { get; }
        bool WantsToBuildCyan { get; }
        bool WantsToBuildGray { get; }
        bool WantsToBuildGreen { get; }
        bool WantsToBuildWhite { get; }
        bool IsShiftPressed { get; }
        bool IsCtrlPressed { get; }
        bool IsGamepadActive { get; }
        void SetMovementInput(Vector2 input);
    }
}
