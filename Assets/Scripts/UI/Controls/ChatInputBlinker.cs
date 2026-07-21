using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Controls
{
    public class ChatInputBlinker
    {
        private IVisualElementScheduledItem _blinkItem;
        private bool _cursorVisible = true;
        private readonly TextField _inputField;
        private readonly VisualElement _internalInput;

        public ChatInputBlinker(TextField inputField, VisualElement internalInput)
        {
            _inputField = inputField;
            _internalInput = internalInput;
        }

        public void StartBlink()
        {
            StopBlink();
            _cursorVisible = true;
            _internalInput?.RemoveFromClassList("cursor-hidden");

            if (_inputField == null)
            {
                return;
            }

            _blinkItem = _inputField.schedule.Execute(() =>
            {
                _cursorVisible = !_cursorVisible;
                if (_internalInput == null)
                {
                    return;
                }

                if (_cursorVisible)
                {
                    _internalInput.RemoveFromClassList("cursor-hidden");
                }
                else
                {
                    _internalInput.AddToClassList("cursor-hidden");
                }
            }).StartingIn(530).Every(530);
        }

        public void StopBlink()
        {
            _blinkItem?.Pause();
            _blinkItem = null;
            _internalInput?.RemoveFromClassList("cursor-hidden");
        }
    }
}
