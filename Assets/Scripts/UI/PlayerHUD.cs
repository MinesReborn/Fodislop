using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Assets.Scripts.UI
{
    public class PlayerHUD : MonoBehaviour
    {
        private const int PANEL_WIDTH = 240;
        private const int PADDING = 12;
        private const int LABEL_FONT_SIZE = 14;
        private const int TITLE_FONT_SIZE = 14;
        private const int HP_BAR_HEIGHT = 14;

        private Color _panelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        private Color _panelBorderColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private Color _separatorColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private Color _hpBarBgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private Color _hpBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        private Color _hpBarLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private Color _textColor = Color.white;
        private Color _accentColor = new Color(0.7f, 0.65f, 0.5f, 1f);

        private UIDocument _doc;
        private VisualElement _panel;

        private Label _nicknameLabel;
        private Label _levelLabel;
        private Label _hpLabel;
        private VisualElement _hpBarFill;
        private Label _moneyLabel;
        private Label _credsLabel;
        private Label _geologyLabel;

        void Start()
        {
            InitializeHUD();
        }

        void OnDestroy()
        {
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnStatsChanged -= RefreshAll;
        }

        private void InitializeHUD()
        {
            _doc = FindObjectOfType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[PlayerHUD] UIDocument не найден на сцене");
                return;
            }

            CreatePanel(_doc.rootVisualElement);

            PlayerStatsModel.Instance.OnStatsChanged += RefreshAll;
            RefreshAll();
        }

        private void CreatePanel(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.name = "PlayerHUD";
            _panel.style.position = Position.Absolute;
            _panel.style.left = 10;
            _panel.style.top = 10;
            _panel.style.width = PANEL_WIDTH;
            _panel.style.paddingTop = PADDING;
            _panel.style.paddingBottom = PADDING;
            _panel.style.paddingLeft = PADDING;
            _panel.style.paddingRight = PADDING;
            _panel.style.backgroundColor = _panelBgColor;
            _panel.style.borderTopWidth = 2;
            _panel.style.borderBottomWidth = 2;
            _panel.style.borderLeftWidth = 2;
            _panel.style.borderRightWidth = 2;
            _panel.style.borderTopColor = _panelBorderColor;
            _panel.style.borderBottomColor = _panelBorderColor;
            _panel.style.borderLeftColor = _panelBorderColor;
            _panel.style.borderRightColor = _panelBorderColor;
            _panel.style.flexDirection = FlexDirection.Column;

            // Nickname + Level row
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.marginBottom = 4;

            _nicknameLabel = new Label("---");
            _nicknameLabel.style.fontSize = TITLE_FONT_SIZE;
            _nicknameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nicknameLabel.style.color = _accentColor;
            _nicknameLabel.style.flexGrow = 1;
            topRow.Add(_nicknameLabel);

            _levelLabel = new Label("Ур: 0");
            _levelLabel.style.fontSize = TITLE_FONT_SIZE;
            _levelLabel.style.color = _textColor;
            _levelLabel.style.marginRight = 2;
            topRow.Add(_levelLabel);

            _panel.Add(topRow);

            // Separator
            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = _separatorColor;
            separator.style.marginBottom = 4;
            _panel.Add(separator);

            // HP label
            _hpLabel = new Label("Прочность: 0/0");
            _hpLabel.style.fontSize = LABEL_FONT_SIZE;
            _hpLabel.style.color = _textColor;
            _hpLabel.style.marginBottom = 2;
            _panel.Add(_hpLabel);

            // HP bar
            var hpContainer = new VisualElement();
            hpContainer.style.height = HP_BAR_HEIGHT;
            hpContainer.style.backgroundColor = _hpBarBgColor;
            hpContainer.style.borderTopLeftRadius = 3;
            hpContainer.style.borderTopRightRadius = 3;
            hpContainer.style.borderBottomLeftRadius = 3;
            hpContainer.style.borderBottomRightRadius = 3;
            hpContainer.style.flexDirection = FlexDirection.Row;
            hpContainer.style.marginBottom = 4;

            _hpBarFill = new VisualElement();
            _hpBarFill.style.height = HP_BAR_HEIGHT;
            _hpBarFill.style.borderTopLeftRadius = 3;
            _hpBarFill.style.borderTopRightRadius = 3;
            _hpBarFill.style.borderBottomLeftRadius = 3;
            _hpBarFill.style.borderBottomRightRadius = 3;
            _hpBarFill.style.backgroundColor = _hpBarFillColor;
            hpContainer.Add(_hpBarFill);

            _panel.Add(hpContainer);

            // Money
            _moneyLabel = new Label("$ 0");
            _moneyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _moneyLabel.style.fontSize = LABEL_FONT_SIZE;
            _moneyLabel.style.color = Color.green;
            _moneyLabel.style.marginTop = 0;
            _moneyLabel.style.marginBottom = 0;
            _panel.Add(_moneyLabel);

            // Creds
            _credsLabel = new Label("C 0");
            _credsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _credsLabel.style.fontSize = LABEL_FONT_SIZE;
            _credsLabel.style.color = Color.yellow;
            _credsLabel.style.marginTop = 0;
            _credsLabel.style.marginBottom = 0;
            _panel.Add(_credsLabel);

            // Geology
            _geologyLabel = new Label("Геология: 0/0");
            _geologyLabel.style.fontSize = LABEL_FONT_SIZE;
            _geologyLabel.style.color = _textColor;
            _geologyLabel.style.marginTop = 0;
            _geologyLabel.style.marginBottom = 0;
            _panel.Add(_geologyLabel);

            root.Add(_panel);
        }

        private void RefreshAll()
        {
            var stats = PlayerStatsModel.Instance;
            if (stats == null) return;

            _nicknameLabel.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
            _levelLabel.text = $"Ур: {stats.Level:N0}";

            _hpLabel.text = $"Прочность: {stats.Health:N0}/{stats.MaxHealth:N0}";

            float pct = stats.HealthPercent;
            _hpBarFill.style.width = new Length(pct * 100, LengthUnit.Percent);
            _hpBarFill.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;

            _moneyLabel.text = $"$ {stats.Money:N0}";
            _credsLabel.text = $"C {stats.Creds:N0}";

            _geologyLabel.text = string.IsNullOrEmpty(stats.GeologyText)
                ? "Геология: 0/0"
                : $"Геология: {stats.GeologyCurrent}/{stats.GeologyMax} ({stats.GeologyText})";
        }
    }
}
