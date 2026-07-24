using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class UIStack
    {
        private readonly UIDocument _doc;
        private readonly Stack<VisualElement> _stack = new();

        public event Action OnStackEmpty;
        public int Count => _stack.Count;

        public UIStack(UIDocument doc)
        {
            _doc = doc;
        }

        public void Push(VisualElement window)
        {
            if (window == null)
            {
                return;
            }

            _stack.Push(window);
            _doc.rootVisualElement.Add(window);
        }

        public bool Pop()
        {
            if (_stack.Count == 0)
            {
                return false;
            }

            var top = _stack.Pop();
            if (top != null && top.parent != null)
            {
                top.parent.Remove(top);
            }

            return true;
        }

        public void PopAll()
        {
            while (_stack.Count > 0)
            {
                Pop();
            }
        }

        public bool HandleEscape()
        {
            if (_stack.Count == 0)
            {
                OnStackEmpty?.Invoke();
                return false;
            }

            Pop();
            return true;
        }

        public bool Contains(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            foreach (var el in _stack)
            {
                if (el == element)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
