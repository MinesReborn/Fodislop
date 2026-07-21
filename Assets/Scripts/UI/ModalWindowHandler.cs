using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class ModalWindowHandler
    {
        private readonly UIDocument _doc;
        private VisualElement _overlay;

        public ModalWindowHandler(UIDocument doc)
        {
            _doc = doc;
        }

        public void Show(ModalWindowPacket packet)
        {
            Hide();

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 40;
            panel.style.paddingRight = 40;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 300;
            panel.style.maxWidth = 500;

            if (!string.IsNullOrEmpty(packet.IconURI))
            {
                var icon = new VisualElement();
                icon.style.width = 64;
                icon.style.height = 64;
                icon.style.marginBottom = 10;
                icon.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                panel.Add(icon);
            }

            var titleLabel = new Label(packet.Title);
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.7f, 0.65f, 0.5f, 1f);
            titleLabel.style.marginBottom = 12;
            panel.Add(titleLabel);

            var descLabel = new Label(packet.Description);
            descLabel.style.fontSize = 14;
            descLabel.style.color = Color.white;
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.marginBottom = 20;
            descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(descLabel);

            var okButton = new Button(() => Hide());
            okButton.text = packet.ButtonText;
            okButton.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            okButton.style.borderTopWidth = 2;
            okButton.style.borderBottomWidth = 2;
            okButton.style.borderLeftWidth = 2;
            okButton.style.borderRightWidth = 2;
            okButton.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            okButton.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            okButton.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            okButton.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            okButton.style.paddingTop = 8;
            okButton.style.paddingBottom = 8;
            okButton.style.paddingLeft = 20;
            okButton.style.paddingRight = 20;
            okButton.style.minWidth = 100;
            okButton.style.color = Color.white;
            okButton.style.fontSize = 14;
            okButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            okButton.RegisterCallback<MouseEnterEvent>(_ =>
                okButton.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f));
            okButton.RegisterCallback<MouseLeaveEvent>(_ =>
                okButton.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
            panel.Add(okButton);

            _overlay.Add(panel);
            _doc.rootVisualElement.Add(_overlay);
        }

        public bool IsShowing => _overlay != null && _overlay.parent != null;

        public void Hide()
        {
            if (_overlay != null && _overlay.parent != null)
            {
                _overlay.parent.Remove(_overlay);
            }

            _overlay = null;
        }
    }
}
