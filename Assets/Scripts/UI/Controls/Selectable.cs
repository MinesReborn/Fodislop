using System.Linq;
using UnityEngine.UIElements;

namespace Fodinae.UI.Controls
{
    public class Selectable : BindableElement, INotifyValueChanged<bool>
    {
        private bool _value;
        private VisualElement _checkedElement;
        private VisualElement _uncheckedElement;

        public string Group { get; set; }

        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                using var evt = ChangeEvent<bool>.GetPooled(_value, value);
                evt.target = this;
                _value = value;
                SetValueWithoutNotify(value);
                SendEvent(evt);
            }
        }

        public Selectable()
        {
            RegisterCallback<ClickEvent>(OnClick);
        }

        public void SetVisuals(VisualElement checkedElement, VisualElement uncheckedElement)
        {
            _checkedElement = checkedElement;
            _uncheckedElement = uncheckedElement;

            Add(_checkedElement);
            Add(_uncheckedElement);

            UpdateVisuals();
        }

        private void OnClick(ClickEvent evt)
        {
            if (string.IsNullOrEmpty(Group))
            {
                // Case 1: No group name, act as a checkbox.
                value = !value;
                return;
            }

            // Case 2 & 3: Has a group name.
            var root = panel.visualTree;
            var groupPeers = root.Query<Selectable>().Where(s => s.Group == Group && s != this).ToList();

            if (groupPeers.Count == 0)
            {
                // Case 2: Only item in group, act as a checkbox.
                value = !value;
            }
            else
            {
                // Case 3: Part of a radio button group.
                if (value)
                {
                    // It's already selected. Standard radio button groups don't allow
                    // deselection by clicking the active button. So, do nothing.
                    return;
                }

                // Select this button and deselect all others in the group.
                value = true;
                foreach (var peer in groupPeers)
                {
                    // Use SetValueWithoutNotify to avoid firing multiple change events.
                    peer.SetValueWithoutNotify(false);
                }
            }
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (_checkedElement == null || _uncheckedElement == null)
            {
                return;
            }

            _checkedElement.style.display = _value ? DisplayStyle.Flex : DisplayStyle.None;
            _uncheckedElement.style.display = !_value ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
