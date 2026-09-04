#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Centralized UI Input and Modal Stack Manager for Fodinae.
    /// Manages open modal windows, chat focus state, and escape key modal stack popping.
    /// </summary>
    public class UIInputManager : MonoBehaviour
    {
        private readonly Stack<VisualElement> _modalStack = new();
        public bool IsChatFocused { get; set; }
        public bool IsPauseMenuOpen { get; set; }
        public bool IsProgrammatorOpen { get; set; }

        public bool IsModalOpen => _modalStack.Count > 0;
        public bool IsInputBlocked =>
            IsModalOpen || IsChatFocused || IsPauseMenuOpen || IsProgrammatorOpen;

        public void PushModal(VisualElement modalElement)
        {
            if (modalElement != null && !_modalStack.Contains(modalElement))
            {
                _modalStack.Push(modalElement);
            }
        }

        public void PopModal(VisualElement modalElement)
        {
            if (_modalStack.Count > 0 && _modalStack.Peek() == modalElement)
            {
                _modalStack.Pop();
            }
        }

        public bool TryPopTopModal()
        {
            if (_modalStack.Count > 0)
            {
                var top = _modalStack.Pop();
                if (top != null && top.parent != null)
                {
                    top.parent.Remove(top);
                    return true;
                }
            }

            return false;
        }
    }
}
