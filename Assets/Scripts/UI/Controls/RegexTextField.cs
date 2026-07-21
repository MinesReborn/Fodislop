using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Fodinae.UI.Controls
{
    public class RegexTextField : TextField
    {
        private int _lastCursorIndex;
        private string _lastValidValue;
        private string _regex;
        public string Regex
        {
            get => _regex;
            set
            {
                _regex = value;
                Validate();
            }
        }

        public RegexTextField()
            : base()
        {
            _lastValidValue = this.value;
            RegisterCallback<FocusOutEvent>(evt => Validate());
            RegisterCallback<ChangeEvent<string>>(evt => Validate(evt.newValue));
            RegisterCallback<KeyDownEvent>(evt => _lastCursorIndex = cursorIndex);
        }

        private string _defaultValue;
        public string DefaultValue
        {
            get => _defaultValue;
            set
            {
                _defaultValue = value;
                this.value = _defaultValue;
            }
        }

        public RegexTextField(string label)
            : base(label)
        {
            _lastValidValue = this.value;
            RegisterCallback<FocusOutEvent>(evt => Validate());
            RegisterCallback<ChangeEvent<string>>(evt => Validate(evt.newValue));
            RegisterCallback<KeyDownEvent>(evt => _lastCursorIndex = cursorIndex);
        }

        public RegexTextField(string label, string defaultValue)
            : base(label)
        {
            _defaultValue = defaultValue;
            this.value = defaultValue;
            _lastValidValue = this.value;
            RegisterCallback<FocusOutEvent>(evt => Validate());
            RegisterCallback<ChangeEvent<string>>(evt => Validate(evt.newValue));
            RegisterCallback<KeyDownEvent>(evt => _lastCursorIndex = cursorIndex);
        }

        private void Validate(string text)
        {
            if (string.IsNullOrEmpty(Regex))
            {
                RemoveFromClassList("invalid");
                _lastValidValue = text;
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(text, Regex))
            {
                this.SetValueWithoutNotify(_lastValidValue);
                this.cursorIndex = _lastCursorIndex;
                AddToClassList("invalid");
            }
            else
            {
                _lastValidValue = text;
                RemoveFromClassList("invalid");
            }
        }

        private void Validate()
        {
            Validate(this.value);
        }
    }
}
