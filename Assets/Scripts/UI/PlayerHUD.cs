using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
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
        private const int BTN_SIZE = 50;
        private const int BONUS_PANEL_WIDTH = 200;
        private const int GAP = 6;

        private Color _panelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        private Color _panelBorderColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private Color _separatorColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private Color _hpBarBgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private Color _hpBarFillColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        private Color _hpBarLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private Color _textColor = Color.white;
        private Color _accentColor = new Color(0.7f, 0.65f, 0.5f, 1f);
        private Color _accentHoverColor = new Color(0.8f, 0.75f, 0.6f, 1f);

        private UIDocument _doc;
        private VisualElement _panel;
        private Button _bonusButton;
        private VisualElement _bonusPanel;
        private bool _isBonusOpen;

        private Label _nicknameLabel;
        private Label _levelLabel;
        private Label _hpLabel;
        private VisualElement _hpBarFill;
        private Label _moneyLabel;
        private Label _credsLabel;
        private Label _geologyLabel;
        private Label _basketPercentLabel;
        private readonly List<Texture2D> _crystalTextures = new();
        private readonly List<Label> _basketCrystalLabels = new();
        private VisualElement _basketContainer;

        async void Start()
        {
            await LoadCrystalTextures();
            InitializeHUD();
        }

        void OnDestroy()
        {
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnStatsChanged -= RefreshAll;
        }

        private async UniTask LoadCrystalTextures()
        {
            _crystalTextures.Clear();
            foreach (CrystalType ct in Enum.GetValues(typeof(CrystalType)))
            {
                if (ct == CrystalType.Unknown) continue;
                string name = ct.ToString().ToLowerInvariant();
                var tex = await ClientAssetLoader.Instance.GetTextureAsync("Crystalls/" + name);
                _crystalTextures.Add(tex);
            }
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
            CreateBonusButton(_doc.rootVisualElement);
            CreateBonusPanel(_doc.rootVisualElement);
            RebuildCrystalRows();
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

            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = _separatorColor;
            separator.style.marginBottom = 4;
            _panel.Add(separator);

            _hpLabel = new Label("Прочность: 0/0");
            _hpLabel.style.fontSize = LABEL_FONT_SIZE;
            _hpLabel.style.color = _textColor;
            _hpLabel.style.marginBottom = 2;
            _panel.Add(_hpLabel);

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

            _moneyLabel = new Label("$ 0");
            _moneyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _moneyLabel.style.fontSize = LABEL_FONT_SIZE;
            _moneyLabel.style.color = Color.green;
            _moneyLabel.style.marginTop = 0;
            _moneyLabel.style.marginBottom = 0;
            _panel.Add(_moneyLabel);

            _credsLabel = new Label("C 0");
            _credsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _credsLabel.style.fontSize = LABEL_FONT_SIZE;
            _credsLabel.style.color = Color.yellow;
            _credsLabel.style.marginTop = 0;
            _credsLabel.style.marginBottom = 0;
            _panel.Add(_credsLabel);

            _geologyLabel = new Label("Геология: 0/0");
            _geologyLabel.style.fontSize = LABEL_FONT_SIZE;
            _geologyLabel.style.color = _textColor;
            _geologyLabel.style.marginTop = 0;
            _geologyLabel.style.marginBottom = 0;
            _panel.Add(_geologyLabel);

            // Basket separator
            var basketSep = new VisualElement();
            basketSep.style.height = 1;
            basketSep.style.backgroundColor = _separatorColor;
            basketSep.style.marginTop = 4;
            basketSep.style.marginBottom = 4;
            _panel.Add(basketSep);

            // Basket percent
            _basketPercentLabel = new Label("Груз: 0%");
            _basketPercentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _basketPercentLabel.style.fontSize = LABEL_FONT_SIZE;
            _basketPercentLabel.style.color = _accentColor;
            _basketPercentLabel.style.marginBottom = 2;
            _panel.Add(_basketPercentLabel);

            //crystals
            _basketContainer = new VisualElement();
            _basketContainer.name = "BasketCrystals";
            _basketContainer.style.flexDirection = FlexDirection.Column;
            _panel.Add(_basketContainer);

            root.Add(_panel);
        }

        private void CreateBonusButton(VisualElement root)
        {
            _bonusButton = new Button(ToggleBonusPanel);
            _bonusButton.text = "Бонусы";
            _bonusButton.style.position = Position.Absolute;
            _bonusButton.style.left = 10 + PANEL_WIDTH + GAP;
            _bonusButton.style.top = 10;
            _bonusButton.style.width = 90;
            _bonusButton.style.height = BTN_SIZE;
            _bonusButton.style.backgroundColor = _accentColor;
            _bonusButton.style.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            _bonusButton.style.fontSize = TITLE_FONT_SIZE;
            _bonusButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bonusButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bonusButton.style.borderTopWidth = 2;
            _bonusButton.style.borderBottomWidth = 2;
            _bonusButton.style.borderLeftWidth = 2;
            _bonusButton.style.borderRightWidth = 2;
            _bonusButton.style.borderTopColor = _panelBorderColor;
            _bonusButton.style.borderBottomColor = _panelBorderColor;
            _bonusButton.style.borderLeftColor = _panelBorderColor;
            _bonusButton.style.borderRightColor = _panelBorderColor;
            _bonusButton.style.paddingTop = 0;
            _bonusButton.style.paddingBottom = 0;
            _bonusButton.style.paddingLeft = 0;
            _bonusButton.style.paddingRight = 0;

            _bonusButton.RegisterCallback<MouseEnterEvent>(_ =>
                _bonusButton.style.backgroundColor = _accentHoverColor);
            _bonusButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _bonusButton.style.backgroundColor = _accentColor);

            root.Add(_bonusButton);
        }

        private void CreateBonusPanel(VisualElement root)
        {
            _bonusPanel = new VisualElement();
            _bonusPanel.style.position = Position.Absolute;
            _bonusPanel.style.left = 10 + PANEL_WIDTH + GAP + 90 + GAP;
            _bonusPanel.style.top = 10;
            _bonusPanel.style.width = BONUS_PANEL_WIDTH;
            _bonusPanel.style.backgroundColor = _panelBgColor;
            _bonusPanel.style.borderTopWidth = 2;
            _bonusPanel.style.borderBottomWidth = 2;
            _bonusPanel.style.borderLeftWidth = 2;
            _bonusPanel.style.borderRightWidth = 2;
            _bonusPanel.style.borderTopColor = _panelBorderColor;
            _bonusPanel.style.borderBottomColor = _panelBorderColor;
            _bonusPanel.style.borderLeftColor = _panelBorderColor;
            _bonusPanel.style.borderRightColor = _panelBorderColor;
            _bonusPanel.style.paddingTop = 10;
            _bonusPanel.style.paddingBottom = 10;
            _bonusPanel.style.paddingLeft = 10;
            _bonusPanel.style.paddingRight = 10;
            _bonusPanel.style.flexDirection = FlexDirection.Column;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.marginBottom = 10;

            var title = new Label("Бонусы");
            title.style.fontSize = 14;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = _accentColor;
            title.style.flexGrow = 1;
            titleRow.Add(title);

            var closeBtn = new Button(ToggleBonusPanel);
            closeBtn.text = "×";
            closeBtn.style.width = 24;
            closeBtn.style.height = 24;
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            closeBtn.style.fontSize = 18;
            closeBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;
            titleRow.Add(closeBtn);

            _bonusPanel.Add(titleRow);

            var emptyLabel = new Label("Нет активных бонусов");
            emptyLabel.style.color = Color.gray;
            emptyLabel.style.fontSize = 12;
            emptyLabel.style.marginTop = 0;
            _bonusPanel.Add(emptyLabel);

            _bonusPanel.style.display = DisplayStyle.None;
            root.Add(_bonusPanel);
        }

        private void ToggleBonusPanel()
        {
            _isBonusOpen = !_isBonusOpen;
            _bonusPanel.style.display = _isBonusOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _bonusButton.style.backgroundColor = _isBonusOpen ? _accentHoverColor : _accentColor;
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

            _basketPercentLabel.text = $"Груз: {stats.BasketMaxPercent}%";
            for (int i = 0; i < _basketCrystalLabels.Count && i < stats.BasketContents.Length; i++)
            {
                _basketCrystalLabels[i].text = $"{FormatCompact(stats.BasketContents[i])}/{FormatCompact(stats.BasketCapacity)}";
            }
        }

        private void RebuildCrystalRows()
        {
            _basketContainer.Clear();
            _basketCrystalLabels.Clear();

            for (int i = 0; i < _crystalTextures.Count; i++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1;

                var dot = new Image();
                dot.style.width = 14;
                dot.style.height = 14;
                dot.style.marginRight = 6;
                dot.style.alignSelf = Align.Center;
                if (_crystalTextures[i] != null)
                    dot.style.backgroundImage = new StyleBackground(_crystalTextures[i]);
                row.Add(dot);

                var label = new Label("0/0");
                label.style.fontSize = 11;
                label.style.color = _textColor;
                row.Add(label);

                _basketCrystalLabels.Add(label);
                _basketContainer.Add(row);
            }
        }

        private static string FormatCompact(long val)
        {
            if (val >= 1_000_000) return $"{(val / 1_000_000f):F1}M";
            if (val >= 10_000) return $"{val / 1_000}K";
            return val.ToString("N0");
        }
    }
}
