using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Player;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class PlayerHUD : MonoBehaviour
    {
        private const int PANEL_WIDTH = 240;
        private const int PADDING = 12;
        private const int LABEL_FONT_SIZE = 14;
        private const int TITLE_FONT_SIZE = 14;
        private const int HP_BAR_HEIGHT = 14;
        private const int BTN_SIZE = 50;
        private const int PROGRAMMATOR_WIDTH = 584;
        private const int PROGRAMMATOR_HEIGHT = 520;
        private const int BONUS_PANEL_WIDTH = 260;
        private const int GAP = 6;
        private const int SKILL_GRID_COLS = 4;

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
        private Label _bonusStatusLabel;
        private Button _bonusClaimButton;
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
        private VisualElement _skillContainer;
        private readonly Dictionary<SkillType, (Label arrow, VisualElement barFill)> _skillIcons = new();
        private readonly Dictionary<SkillType, IVisualElementScheduledItem> _bounceSchedules = new();
        private readonly Dictionary<SkillType, IVisualElementScheduledItem> _pulseSchedules = new();
        private Button _autoDigButton;
        private Label _autoDigLabel;
        private Button _aggressionButton;
        private Label _aggressionLabel;
        private VisualElement _currentSkillRow;
        private int _skillCountInRow = 0;
        private Button _chatButton;
        private VisualElement _statusPanel;
        private readonly Dictionary<string, VisualElement> _statusLineElements = new();

        private VisualElement _respawnPopup;
        private VisualElement _buildingsPopup;
        private VisualElement _faqPopup;
        private VisualElement _programmatorPopup;

        private Button _missionButton;
        private VisualElement _missionPanel;
        private Label _missionTitleLabel;
        private Label _missionDescLabel;
        private VisualElement _missionProgressFill;
        private Label _missionProgressLabel;

        async void Start()
        {
            await LoadCrystalTextures();
            InitializeHUD();
        }

        void OnDestroy()
        {
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnStatsChanged -= RefreshAll;
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnSkillProgress -= OnSkillProgress;
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnDailyBonusChanged -= UpdateDailyBonusPanel;
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnStatusLinesChanged -= RebuildStatusPanel;
            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnMissionChanged -= UpdateMissionPanel;
            if (GlobalChatUI.Instance != null)
                GlobalChatUI.Instance.Hide();
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
            CreateAggressionToggle(_doc.rootVisualElement);
            CreateAutoDigToggle(_doc.rootVisualElement);
            CreateChatButton(_doc.rootVisualElement);
            CreateButtonsAndPopups(_doc.rootVisualElement);
            CreateStatusPanel(_doc.rootVisualElement);
            CreateSkillContainer(_doc.rootVisualElement);
            CreateMissionPanel(_doc.rootVisualElement);
            if (PlayerStatsModel.Instance != null)
            {
                PlayerStatsModel.Instance.OnSkillProgress += OnSkillProgress;
                PlayerStatsModel.Instance.OnStatusLinesChanged += RebuildStatusPanel;
                PlayerStatsModel.Instance.OnMissionChanged += UpdateMissionPanel;
            }
            var player = FindObjectOfType<PlayerMovementController>();
            if (player != null)
                player.OnAutoDigChanged += UpdateAutoDigButton;

            if (player != null)
                player.OnAggressionChanged += UpdateAggressionButton;

            if (PlayerStatsModel.Instance != null)
                PlayerStatsModel.Instance.OnDailyBonusChanged += UpdateDailyBonusPanel;

            RebuildCrystalRows();
            PlayerStatsModel.Instance.OnStatsChanged += RefreshAll;
            RefreshAll();

            var root = _doc.rootVisualElement;

            // Блокируем навигацию стрелками/Tab
            root.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            // Блокируем ENTER/Space на кнопках (кроме чата)
            root.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (!ChatInput.IsFocused)
                    evt.StopPropagation();
            }, TrickleDown.TrickleDown);
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

            var basketSep = new VisualElement();
            basketSep.style.height = 1;
            basketSep.style.backgroundColor = _separatorColor;
            basketSep.style.marginTop = 4;
            basketSep.style.marginBottom = 4;
            _panel.Add(basketSep);

            _basketPercentLabel = new Label("Груз: 0%");
            _basketPercentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _basketPercentLabel.style.fontSize = LABEL_FONT_SIZE;
            _basketPercentLabel.style.color = _accentColor;
            _basketPercentLabel.style.marginBottom = 2;
            _panel.Add(_basketPercentLabel);

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

            _bonusStatusLabel = new Label("Ежедневный бонус: ...");
            _bonusStatusLabel.style.fontSize = 12;
            _bonusStatusLabel.style.color = Color.gray;
            _bonusStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            _bonusStatusLabel.style.marginBottom = 5;
            _bonusPanel.Add(_bonusStatusLabel);

            _bonusClaimButton = new Button(ClaimDailyBonus);
            _bonusClaimButton.text = "Забрать";
            _bonusClaimButton.style.display = DisplayStyle.None;
            _bonusClaimButton.style.width = 80;
            _bonusClaimButton.style.height = 28;
            _bonusClaimButton.style.alignSelf = Align.Center;
            _bonusClaimButton.style.backgroundColor = new Color(0.1f, 0.4f, 0.1f, 1f);
            _bonusClaimButton.style.color = Color.white;
            _bonusClaimButton.style.fontSize = 12;
            _bonusClaimButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bonusClaimButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bonusClaimButton.style.borderTopWidth = 1;
            _bonusClaimButton.style.borderBottomWidth = 1;
            _bonusClaimButton.style.borderLeftWidth = 1;
            _bonusClaimButton.style.borderRightWidth = 1;
            _bonusClaimButton.style.borderTopColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            _bonusClaimButton.style.borderBottomColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            _bonusClaimButton.style.borderLeftColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            _bonusClaimButton.style.borderRightColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            _bonusClaimButton.style.paddingTop = 0;
            _bonusClaimButton.style.paddingBottom = 0;
            _bonusClaimButton.style.paddingLeft = 0;
            _bonusClaimButton.style.paddingRight = 0;
            _bonusClaimButton.RegisterCallback<MouseEnterEvent>(_ =>
                _bonusClaimButton.style.backgroundColor = new Color(0.2f, 0.5f, 0.2f, 1f));
            _bonusClaimButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _bonusClaimButton.style.backgroundColor = new Color(0.1f, 0.4f, 0.1f, 1f));
            _bonusPanel.Add(_bonusClaimButton);

            _bonusPanel.style.display = DisplayStyle.None;
            root.Add(_bonusPanel);
        }

        private void ToggleBonusPanel()
        {
            _isBonusOpen = !_isBonusOpen;
            _bonusPanel.style.display = _isBonusOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _bonusButton.style.backgroundColor = _isBonusOpen ? _accentHoverColor : _accentColor;
            if (_isBonusOpen)
                UpdateDailyBonusPanel();
            UpdateStatusPanelPosition();
        }

        private void UpdateStatusPanelPosition()
        {
            if (_statusPanel == null) return;
            if (_isBonusOpen && _bonusPanel != null)
            {
                _bonusPanel.schedule.Execute(() =>
                {
                    if (!_isBonusOpen) return;
                    _statusPanel.style.top = 10 + GAP + _bonusPanel.resolvedStyle.height;
                }).StartingIn(16);
            }
            else
            {
                _statusPanel.style.top = 10 + BTN_SIZE + GAP;
            }
        }

        private void UpdateDailyBonusPanel()
        {
            if (_bonusStatusLabel == null) return;
            var stats = PlayerStatsModel.Instance;
            if (stats == null) return;

            if (stats.DailyBonusAvailable)
            {
                _bonusStatusLabel.text = "Ежедневный бонус: <color=lime>Доступен!</color>";
                _bonusStatusLabel.style.color = Color.green;
                _bonusClaimButton.style.display = DisplayStyle.Flex;
            }
            else
            {
                _bonusStatusLabel.text = "Ежедневный бонус: Нет активных бонусов";
                _bonusStatusLabel.style.color = Color.gray;
                _bonusClaimButton.style.display = DisplayStyle.None;
            }

            UpdateStatusPanelPosition();
        }

        private void CreateStatusPanel(VisualElement root)
        {
            _statusPanel = new VisualElement();
            _statusPanel.name = "StatusPanel";
            _statusPanel.style.position = Position.Absolute;
            _statusPanel.style.left = 10 + PANEL_WIDTH + GAP;
            _statusPanel.style.top = 10 + BTN_SIZE + GAP;
            _statusPanel.style.width = 220;
            _statusPanel.style.paddingTop = PADDING;
            _statusPanel.style.paddingBottom = PADDING;
            _statusPanel.style.paddingLeft = PADDING;
            _statusPanel.style.paddingRight = PADDING;
            _statusPanel.style.backgroundColor = _panelBgColor;
            _statusPanel.style.borderTopWidth = 2;
            _statusPanel.style.borderBottomWidth = 2;
            _statusPanel.style.borderLeftWidth = 2;
            _statusPanel.style.borderRightWidth = 2;
            _statusPanel.style.borderTopColor = _panelBorderColor;
            _statusPanel.style.borderBottomColor = _panelBorderColor;
            _statusPanel.style.borderLeftColor = _panelBorderColor;
            _statusPanel.style.borderRightColor = _panelBorderColor;
            _statusPanel.style.flexDirection = FlexDirection.Column;
            _statusPanel.style.display = DisplayStyle.None;
            root.Add(_statusPanel);
        }

        private void RebuildStatusPanel()
        {
            if (_statusPanel == null) return;
            var stats = PlayerStatsModel.Instance;
            if (stats == null) return;

            var currentLines = stats.StatusLines;
            if (currentLines.Count == 0)
            {
                _statusPanel.style.display = DisplayStyle.None;
                _statusLineElements.Clear();
                _statusPanel.Clear();
                return;
            }
            _statusPanel.style.display = DisplayStyle.Flex;
            var toRemove = new List<string>();
            foreach (var kvp in _statusLineElements)
            {
                if (!currentLines.ContainsKey(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
            {
                _statusPanel.Remove(_statusLineElements[key]);
                _statusLineElements.Remove(key);
            }

            foreach (var kvp in currentLines)
            {
                if (_statusLineElements.TryGetValue(kvp.Key, out var existing))
                {
                    var label = existing as Label;
                    if (label != null)
                        UpdateStatusLabel(label, kvp.Value);
                    label.style.color = kvp.Value.Color;
                }
                else
                {
                    var row = new Label();
                    row.style.fontSize = LABEL_FONT_SIZE;
                    row.style.color = kvp.Value.Color;
                    row.style.marginBottom = 2;
                    row.style.whiteSpace = WhiteSpace.Normal;
                    UpdateStatusLabel(row, kvp.Value);
                    _statusPanel.Add(row);

                    if (kvp.Value.Expiry > 0)
                    {
                        row.schedule.Execute(() =>
                        {
                            if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                                return;
                            var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                            if (entry.Text == null)
                                return;
                            UpdateStatusLabel(row, entry);
                        }).Every(1000);
                    }

                    _statusLineElements[kvp.Key] = row;
                }
            }
        }

        private static void UpdateStatusLabel(Label label, StatusLineEntry entry)
        {
            if (entry.Text == null || entry.Text.Length == 0)
            {
                label.text = "";
                return;
            }

            var name = entry.Text[0];
            if (entry.Expiry > 0)
            {
                var remaining = Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                label.text = $"{name}: {FormatTime(remaining)}";
            }
            else if (entry.Text.Length > 1)
            {
                label.text = $"{name}: {entry.Text[1]}";
            }
            else
            {
                label.text = name;
            }
        }

        private static string FormatTime(long seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void ClaimDailyBonus()
        {
            Debug.Log("[PlayerHUD] ClaimDailyBonus: sending claim request");
            NetworkService.Instance.Send(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
        }

        private void CreateAutoDigToggle(VisualElement root)
        {
            _autoDigButton = new Button(ToggleAutoDig);
            _autoDigButton.text = "";
            _autoDigButton.style.position = Position.Absolute;
            _autoDigButton.style.left = 10;
            _autoDigButton.style.bottom = 281;
            _autoDigButton.style.width = 100;
            _autoDigButton.style.height = 28;
            _autoDigButton.style.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.85f);
            _autoDigButton.style.borderTopWidth = 2;
            _autoDigButton.style.borderBottomWidth = 2;
            _autoDigButton.style.borderLeftWidth = 2;
            _autoDigButton.style.borderRightWidth = 2;
            _autoDigButton.style.borderTopColor = _panelBorderColor;
            _autoDigButton.style.borderBottomColor = _panelBorderColor;
            _autoDigButton.style.borderLeftColor = _panelBorderColor;
            _autoDigButton.style.borderRightColor = _panelBorderColor;
            _autoDigButton.style.paddingTop = 0;
            _autoDigButton.style.paddingBottom = 0;
            _autoDigButton.style.paddingLeft = 0;
            _autoDigButton.style.paddingRight = 0;

            _autoDigLabel = new Label("Копать ✗");
            _autoDigLabel.style.fontSize = 12;
            _autoDigLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _autoDigLabel.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _autoDigLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _autoDigLabel.style.flexGrow = 1;
            _autoDigButton.Add(_autoDigLabel);

            root.Add(_autoDigButton);
        }

        private void CreateAggressionToggle(VisualElement root)
        {
            _aggressionButton = new Button(ToggleAggression);
            _aggressionButton.text = "";
            _aggressionButton.style.position = Position.Absolute;
            _aggressionButton.style.left = 10;
            _aggressionButton.style.bottom = 314;
            _aggressionButton.style.width = 100;
            _aggressionButton.style.height = 28;
            _aggressionButton.style.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.85f);
            _aggressionButton.style.borderTopWidth = 2;
            _aggressionButton.style.borderBottomWidth = 2;
            _aggressionButton.style.borderLeftWidth = 2;
            _aggressionButton.style.borderRightWidth = 2;
            _aggressionButton.style.borderTopColor = _panelBorderColor;
            _aggressionButton.style.borderBottomColor = _panelBorderColor;
            _aggressionButton.style.borderLeftColor = _panelBorderColor;
            _aggressionButton.style.borderRightColor = _panelBorderColor;
            _aggressionButton.style.paddingTop = 0;
            _aggressionButton.style.paddingBottom = 0;
            _aggressionButton.style.paddingLeft = 0;
            _aggressionButton.style.paddingRight = 0;

            _aggressionLabel = new Label("Агрессия ✗");
            _aggressionLabel.style.fontSize = 12;
            _aggressionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _aggressionLabel.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _aggressionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _aggressionLabel.style.flexGrow = 1;
            _aggressionButton.Add(_aggressionLabel);

            root.Add(_aggressionButton);
        }

        private void CreateSkillContainer(VisualElement root)
        {
            _skillContainer = new VisualElement();
            _skillContainer.name = "MiniSkills";
            _skillContainer.style.position = Position.Absolute;
            _skillContainer.style.right = 10;
            _skillContainer.style.bottom = 10;
            _skillContainer.style.flexDirection = FlexDirection.ColumnReverse;
            _skillContainer.style.alignItems = Align.FlexEnd;
            root.Add(_skillContainer);
        }

        private void EnsureSkillRow()
        {
            if (_currentSkillRow != null && _skillCountInRow < SKILL_GRID_COLS)
                return;

            _currentSkillRow = new VisualElement();
            _currentSkillRow.style.flexDirection = FlexDirection.Row;
            _currentSkillRow.style.alignItems = Align.FlexStart;
            _currentSkillRow.style.marginBottom = 2;
            _skillContainer.Add(_currentSkillRow);
            _skillCountInRow = 0;
        }

        private (Label arrow, VisualElement barFill) CreateSkillIcon(SkillType skill)
        {
            EnsureSkillRow();

            var cell = new VisualElement();
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.alignItems = Align.FlexEnd;
            cell.style.marginRight = 4;
            cell.style.marginBottom = 2;

            var iconColumn = new VisualElement();
            iconColumn.style.flexDirection = FlexDirection.Column;
            iconColumn.style.alignItems = Align.Center;
            iconColumn.style.width = 24;

            var arrow = new Label("up");
            arrow.style.fontSize = 11;
            arrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            arrow.style.color = _accentColor;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.height = 10;
            arrow.style.width = 24;
            arrow.style.marginLeft = 0;
            arrow.style.marginRight = 0;
            arrow.style.paddingTop = 0;
            arrow.style.paddingBottom = 0;
            arrow.style.paddingLeft = 0;
            arrow.style.paddingRight = 0;
            iconColumn.Add(arrow);

            var iconImage = new Image();
            iconImage.style.width = 24;
            iconImage.style.height = 24;

            var tex = Resources.Load<Texture2D>($"skills/{skill}");
            if (tex != null)
                iconImage.image = tex;
            else
                iconImage.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

            iconColumn.Add(iconImage);
            cell.Add(iconColumn);

            var barContainer = new VisualElement();
            barContainer.style.width = 6;
            barContainer.style.height = 24;
            barContainer.style.marginLeft = 2;
            barContainer.style.flexDirection = FlexDirection.Column;
            barContainer.style.justifyContent = Justify.FlexEnd;

            var barFill = new VisualElement();
            barFill.style.width = 6;
            barFill.style.height = 2;
            barFill.style.backgroundColor = Color.green;
            barContainer.Add(barFill);
            cell.Add(barContainer);

            _currentSkillRow.Add(cell);
            _skillCountInRow++;

            _skillIcons[skill] = (arrow, barFill);
            return (arrow, barFill);
        }

        private void ToggleAutoDig()
        {
            var player = FindObjectOfType<PlayerMovementController>();
            if (player != null)
                player.AutoDig = !player.AutoDig;
        }

        private void UpdateAutoDigButton(bool enabled)
        {
            if (_autoDigLabel == null) return;
            _autoDigLabel.text = enabled ? "Копать ✓" : "Копать ✗";
            _autoDigLabel.style.color = enabled
                ? new Color(0.3f, 0.9f, 0.3f, 1f)
                : new Color(0.9f, 0.3f, 0.3f, 1f);
            _autoDigButton.style.backgroundColor = enabled
                ? new Color(0.05f, 0.15f, 0.05f, 0.85f)
                : new Color(0.15f, 0.05f, 0.05f, 0.85f);
        }

        private void ToggleAggression()
        {
            var player = FindObjectOfType<PlayerMovementController>();
            if (player != null)
                player.ToggleAggression();
        }

        private void UpdateAggressionButton(bool enabled)
        {
            if (_aggressionLabel == null) return;
            _aggressionLabel.text = enabled ? "Агрессия ✓" : "Агрессия ✗";
            _aggressionLabel.style.color = enabled
                ? new Color(0.3f, 0.9f, 0.3f, 1f)
                : new Color(0.9f, 0.3f, 0.3f, 1f);
            _aggressionButton.style.backgroundColor = enabled
                ? new Color(0.05f, 0.15f, 0.05f, 0.85f)
                : new Color(0.15f, 0.05f, 0.05f, 0.85f);
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

        private void OnSkillProgress(SkillType skill, long current, long max)
        {
            if (!_skillIcons.TryGetValue(skill, out var icon))
            {
                var created = CreateSkillIcon(skill);
                icon.arrow = created.arrow;
                icon.barFill = created.barFill;
            }

            float progress = max > 0 ? (float)current / max : 0f;

            icon.barFill.style.backgroundColor = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(progress));

            icon.arrow.text = progress >= 1f ? "up" : "";

            if (progress >= 1f)
            {
                StopBarPulse(skill);
                StartBounce(skill, icon.arrow);
            }
            else
            {
                StopBounce(skill, icon.arrow);
                StartBarPulse(skill, icon.barFill, progress);
            }
        }

        private void StartBounce(SkillType skill, Label arrow)
        {
            StopBounce(skill, arrow);

            float t = 0f;
            var item = arrow.schedule.Execute(() =>
            {
                t += Time.unscaledDeltaTime;
                float offsetY = Mathf.Sin(t * 2f * Mathf.PI) * 3f;
                arrow.style.translate = new Translate(0, offsetY);
            });
            item.Every(0);

            _bounceSchedules[skill] = item;
        }

        private void StopBounce(SkillType skill, Label arrow)
        {
            if (_bounceSchedules.TryGetValue(skill, out var existing))
            {
                existing.Pause();
                _bounceSchedules.Remove(skill);
            }
            arrow.style.translate = new Translate(0, 0);
        }

        private void StartBarPulse(SkillType skill, VisualElement barFill, float progress)
        {
            StopBarPulse(skill);

            float baseSeg = Mathf.Floor(progress * 20f);
            float baseH = baseSeg * (24f / 20f);
            barFill.style.height = new Length(baseH, LengthUnit.Pixel);

            float t = 0f;
            var item = barFill.schedule.Execute(() =>
            {
                t += Time.unscaledDeltaTime;
                float pulse = (Mathf.Sin(t * 2f * Mathf.PI * 0.5f) + 1f) * (24f / 20f);
                barFill.style.height = new Length(Mathf.Min(baseH + pulse, 24f), LengthUnit.Pixel);
            });
            item.Every(0);
            _pulseSchedules[skill] = item;
        }

        private void StopBarPulse(SkillType skill)
        {
            if (_pulseSchedules.TryGetValue(skill, out var existing))
            {
                existing.Pause();
                _pulseSchedules.Remove(skill);
            }
        }

        private void CreateChatButton(VisualElement root)
        {
            _chatButton = new Button(() => GlobalChatUI.Instance.Toggle());
            _chatButton.text = "Чат";
            _chatButton.style.position = Position.Absolute;
            _chatButton.style.left = 10;
            _chatButton.style.bottom = 248;
            _chatButton.style.width = 100;
            _chatButton.style.height = 28;
            _chatButton.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            _chatButton.style.borderTopWidth = 2;
            _chatButton.style.borderBottomWidth = 2;
            _chatButton.style.borderLeftWidth = 2;
            _chatButton.style.borderRightWidth = 2;
            _chatButton.style.borderTopColor = _panelBorderColor;
            _chatButton.style.borderBottomColor = _panelBorderColor;
            _chatButton.style.borderLeftColor = _panelBorderColor;
            _chatButton.style.borderRightColor = _panelBorderColor;
            _chatButton.style.paddingTop = 0;
            _chatButton.style.paddingBottom = 0;
            _chatButton.style.paddingLeft = 0;
            _chatButton.style.paddingRight = 0;
            _chatButton.style.fontSize = 12;
            _chatButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _chatButton.style.color = _textColor;
            _chatButton.style.unityTextAlign = TextAnchor.MiddleCenter;

            _chatButton.RegisterCallback<MouseEnterEvent>(_ =>
                _chatButton.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            _chatButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _chatButton.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(_chatButton);
        }

        private void CreateButtonsAndPopups(VisualElement root)
        {
            _respawnPopup = CreateRespawnPopup();
            _buildingsPopup = CreatePopup("Мои здания");
            _faqPopup = CreatePopup("FAQ");
            _programmatorPopup = CreateProgrammatorPopup();
            root.Add(_respawnPopup);
            root.Add(_buildingsPopup);
            root.Add(_faqPopup);
            root.Add(_programmatorPopup);

            CreateRespawnButton(root, () => _respawnPopup.style.display = DisplayStyle.Flex);
            CreateMyBuildingsButton(root, () => _buildingsPopup.style.display = DisplayStyle.Flex);
            CreateFaqButton(root, () => _faqPopup.style.display = DisplayStyle.Flex);
            CreateProgrammatorButton(root, () => _programmatorPopup.style.display = DisplayStyle.Flex);
            CreateModalTestButton(root);
            CreateClanButtons(root);
            CreateMissionButton(root);
        }

        private VisualElement CreatePopup(string title)
        {
            var popup = new VisualElement();
            popup.style.position = Position.Absolute;
            popup.style.left = 0;
            popup.style.top = 0;
            popup.style.right = 0;
            popup.style.bottom = 0;
            popup.style.justifyContent = Justify.Center;
            popup.style.alignItems = Align.Center;
            popup.style.display = DisplayStyle.None;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            dimmer.pickingMode = PickingMode.Ignore;
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = _panelBorderColor;
            panel.style.borderBottomColor = _panelBorderColor;
            panel.style.borderLeftColor = _panelBorderColor;
            panel.style.borderRightColor = _panelBorderColor;
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 40;
            panel.style.paddingRight = 40;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 200;

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = _accentColor;
            titleLabel.style.marginBottom = 20;
            panel.Add(titleLabel);

            var closeBtn = new Button(() => popup.style.display = DisplayStyle.None);
            closeBtn.text = "Закрыть";
            closeBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            closeBtn.style.borderTopWidth = 2;
            closeBtn.style.borderBottomWidth = 2;
            closeBtn.style.borderLeftWidth = 2;
            closeBtn.style.borderRightWidth = 2;
            closeBtn.style.borderTopColor = _panelBorderColor;
            closeBtn.style.borderBottomColor = _panelBorderColor;
            closeBtn.style.borderLeftColor = _panelBorderColor;
            closeBtn.style.borderRightColor = _panelBorderColor;
            closeBtn.style.paddingTop = 8;
            closeBtn.style.paddingBottom = 8;
            closeBtn.style.paddingLeft = 20;
            closeBtn.style.paddingRight = 20;
            closeBtn.style.color = Color.white;
            closeBtn.style.fontSize = 14;
            closeBtn.style.unityTextAlign = TextAnchor.MiddleCenter;

            closeBtn.RegisterCallback<MouseEnterEvent>(_ =>
                closeBtn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f));
            closeBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                closeBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));

            panel.Add(closeBtn);
            popup.Add(panel);
            return popup;
        }

        private VisualElement CreateRespawnPopup()
        {
            var popup = new VisualElement();
            popup.style.position = Position.Absolute;
            popup.style.left = 0;
            popup.style.top = 0;
            popup.style.right = 0;
            popup.style.bottom = 0;
            popup.style.justifyContent = Justify.Center;
            popup.style.alignItems = Align.Center;
            popup.style.display = DisplayStyle.None;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            dimmer.pickingMode = PickingMode.Ignore;
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = _panelBorderColor;
            panel.style.borderBottomColor = _panelBorderColor;
            panel.style.borderLeftColor = _panelBorderColor;
            panel.style.borderRightColor = _panelBorderColor;
            panel.style.paddingTop = 20;
            panel.style.paddingBottom = 20;
            panel.style.paddingLeft = 40;
            panel.style.paddingRight = 40;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.alignItems = Align.Center;
            panel.style.minWidth = 200;

            var titleLabel = new Label("Респавн");
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = _accentColor;
            titleLabel.style.marginBottom = 20;
            panel.Add(titleLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.Center;

            void StyleButton(Button btn)
            {
                btn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
                btn.style.borderTopWidth = 2;
                btn.style.borderBottomWidth = 2;
                btn.style.borderLeftWidth = 2;
                btn.style.borderRightWidth = 2;
                btn.style.borderTopColor = _panelBorderColor;
                btn.style.borderBottomColor = _panelBorderColor;
                btn.style.borderLeftColor = _panelBorderColor;
                btn.style.borderRightColor = _panelBorderColor;
                btn.style.paddingTop = 8;
                btn.style.paddingBottom = 8;
                btn.style.paddingLeft = 20;
                btn.style.paddingRight = 20;
                btn.style.marginLeft = 8;
                btn.style.marginRight = 8;
                btn.style.minWidth = 100;
                btn.style.color = Color.white;
                btn.style.fontSize = 14;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;
                btn.RegisterCallback<MouseEnterEvent>(_ =>
                    btn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f));
                btn.RegisterCallback<MouseLeaveEvent>(_ =>
                    btn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
            }

            var okBtn = new Button(() =>
            {
                NetworkService.Instance.SendAction(new SuicidePacket());
                popup.style.display = DisplayStyle.None;
            });
            okBtn.text = "ОК";
            StyleButton(okBtn);
            btnRow.Add(okBtn);

            var backBtn = new Button(() => popup.style.display = DisplayStyle.None);
            backBtn.text = "Назад";
            StyleButton(backBtn);
            btnRow.Add(backBtn);

            panel.Add(btnRow);
            popup.Add(panel);
            return popup;
        }

        private void CreateRespawnButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Респавн";
            btn.style.position = Position.Absolute;
            btn.style.top = 10;
            btn.style.right = 10 + (100 + 6) * 2;
            btn.style.width = 100;
            btn.style.height = 28;
            btn.style.fontSize = 12;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = _textColor;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _panelBorderColor;
            btn.style.borderBottomColor = _panelBorderColor;
            btn.style.borderLeftColor = _panelBorderColor;
            btn.style.borderRightColor = _panelBorderColor;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(btn);
        }

        private void CreateMyBuildingsButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Мои здания";
            btn.style.position = Position.Absolute;
            btn.style.top = 10;
            btn.style.right = 10 + (100 + 6);
            btn.style.width = 100;
            btn.style.height = 28;
            btn.style.fontSize = 12;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = _textColor;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _panelBorderColor;
            btn.style.borderBottomColor = _panelBorderColor;
            btn.style.borderLeftColor = _panelBorderColor;
            btn.style.borderRightColor = _panelBorderColor;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(btn);
        }

        private void CreateFaqButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "FAQ";
            btn.style.position = Position.Absolute;
            btn.style.top = 10;
            btn.style.right = 10;
            btn.style.width = 100;
            btn.style.height = 28;
            btn.style.fontSize = 12;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = _textColor;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _panelBorderColor;
            btn.style.borderBottomColor = _panelBorderColor;
            btn.style.borderLeftColor = _panelBorderColor;
            btn.style.borderRightColor = _panelBorderColor;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(btn);
        }

        private void CreateModalTestButton(VisualElement root)
        {
            var btn = new Button(() =>
            {
                NetworkService.Instance.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
            });
            btn.text = "Тест модального окна";
            btn.style.position = Position.Absolute;
            btn.style.top = 10;
            btn.style.right = 10 + (100 + 6) * 3;
            btn.style.width = 160;
            btn.style.height = 28;
            btn.style.fontSize = 12;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = _textColor;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _panelBorderColor;
            btn.style.borderBottomColor = _panelBorderColor;
            btn.style.borderLeftColor = _panelBorderColor;
            btn.style.borderRightColor = _panelBorderColor;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(btn);
        }

        private void CreateClanButtons(VisualElement root)
        {
            var joinBtn = new Button(() =>
            {
                NetworkService.Instance.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
            });
            joinBtn.text = "Вступить в клан";
            joinBtn.style.position = Position.Absolute;
            joinBtn.style.top = 10;
            joinBtn.style.right = 10 + (100 + 6) * 3 + 160 + 6;
            joinBtn.style.width = 140;
            joinBtn.style.height = 28;
            joinBtn.style.fontSize = 12;
            joinBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            joinBtn.style.color = _textColor;
            joinBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            joinBtn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            joinBtn.style.borderTopWidth = 2;
            joinBtn.style.borderBottomWidth = 2;
            joinBtn.style.borderLeftWidth = 2;
            joinBtn.style.borderRightWidth = 2;
            joinBtn.style.borderTopColor = _panelBorderColor;
            joinBtn.style.borderBottomColor = _panelBorderColor;
            joinBtn.style.borderLeftColor = _panelBorderColor;
            joinBtn.style.borderRightColor = _panelBorderColor;
            joinBtn.style.paddingTop = 0;
            joinBtn.style.paddingBottom = 0;
            joinBtn.style.paddingLeft = 0;
            joinBtn.style.paddingRight = 0;

            joinBtn.RegisterCallback<MouseEnterEvent>(_ =>
                joinBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            joinBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                joinBtn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(joinBtn);

            var leaveBtn = new Button(() =>
            {
                NetworkService.Instance.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
            });
            leaveBtn.text = "Выйти из клана";
            leaveBtn.style.position = Position.Absolute;
            leaveBtn.style.top = 10 + 28 + 6;
            leaveBtn.style.right = 10 + (100 + 6) * 3 + 160 + 6;
            leaveBtn.style.width = 140;
            leaveBtn.style.height = 28;
            leaveBtn.style.fontSize = 12;
            leaveBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            leaveBtn.style.color = _textColor;
            leaveBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            leaveBtn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            leaveBtn.style.borderTopWidth = 2;
            leaveBtn.style.borderBottomWidth = 2;
            leaveBtn.style.borderLeftWidth = 2;
            leaveBtn.style.borderRightWidth = 2;
            leaveBtn.style.borderTopColor = _panelBorderColor;
            leaveBtn.style.borderBottomColor = _panelBorderColor;
            leaveBtn.style.borderLeftColor = _panelBorderColor;
            leaveBtn.style.borderRightColor = _panelBorderColor;
            leaveBtn.style.paddingTop = 0;
            leaveBtn.style.paddingBottom = 0;
            leaveBtn.style.paddingLeft = 0;
            leaveBtn.style.paddingRight = 0;

            leaveBtn.RegisterCallback<MouseEnterEvent>(_ =>
                leaveBtn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            leaveBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                leaveBtn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(leaveBtn);
        }

        private void CreateMissionButton(VisualElement root)
        {
            _missionButton = new Button(() =>
            {
                NetworkService.Instance.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
            });
            _missionButton.text = "Миссии";
            _missionButton.style.position = Position.Absolute;
            _missionButton.style.top = 10 + 28 + 6 + 28 + 6;
            _missionButton.style.right = 10 + (100 + 6) * 3 + 160 + 6;
            _missionButton.style.width = 140;
            _missionButton.style.height = 28;
            _missionButton.style.fontSize = 12;
            _missionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _missionButton.style.color = _textColor;
            _missionButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _missionButton.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            _missionButton.style.borderTopWidth = 2;
            _missionButton.style.borderBottomWidth = 2;
            _missionButton.style.borderLeftWidth = 2;
            _missionButton.style.borderRightWidth = 2;
            _missionButton.style.borderTopColor = _panelBorderColor;
            _missionButton.style.borderBottomColor = _panelBorderColor;
            _missionButton.style.borderLeftColor = _panelBorderColor;
            _missionButton.style.borderRightColor = _panelBorderColor;
            _missionButton.style.paddingTop = 0;
            _missionButton.style.paddingBottom = 0;
            _missionButton.style.paddingLeft = 0;
            _missionButton.style.paddingRight = 0;

            _missionButton.RegisterCallback<MouseEnterEvent>(_ =>
                _missionButton.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            _missionButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _missionButton.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(_missionButton);
        }

        private void CreateMissionPanel(VisualElement root)
        {
            _missionPanel = new VisualElement();
            _missionPanel.name = "MissionPanel";
            _missionPanel.style.position = Position.Absolute;
            _missionPanel.style.top = 50;
            _missionPanel.style.left = new Length(50, LengthUnit.Percent);
            _missionPanel.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _missionPanel.style.minWidth = 300;
            _missionPanel.style.backgroundColor = _panelBgColor;
            _missionPanel.style.borderTopWidth = 2;
            _missionPanel.style.borderBottomWidth = 2;
            _missionPanel.style.borderLeftWidth = 2;
            _missionPanel.style.borderRightWidth = 2;
            _missionPanel.style.borderTopColor = _panelBorderColor;
            _missionPanel.style.borderBottomColor = _panelBorderColor;
            _missionPanel.style.borderLeftColor = _panelBorderColor;
            _missionPanel.style.borderRightColor = _panelBorderColor;
            _missionPanel.style.paddingTop = PADDING;
            _missionPanel.style.paddingBottom = PADDING;
            _missionPanel.style.paddingLeft = PADDING;
            _missionPanel.style.paddingRight = PADDING;
            _missionPanel.style.flexDirection = FlexDirection.Column;
            _missionPanel.style.display = DisplayStyle.None;

            _missionTitleLabel = new Label("---");
            _missionTitleLabel.style.fontSize = TITLE_FONT_SIZE;
            _missionTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _missionTitleLabel.style.color = _accentColor;
            _missionTitleLabel.style.marginBottom = 4;
            _missionPanel.Add(_missionTitleLabel);

            _missionDescLabel = new Label("");
            _missionDescLabel.style.fontSize = LABEL_FONT_SIZE;
            _missionDescLabel.style.color = _textColor;
            _missionDescLabel.style.whiteSpace = WhiteSpace.Normal;
            _missionDescLabel.style.marginBottom = 8;
            _missionPanel.Add(_missionDescLabel);

            var progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;
            progressRow.style.marginBottom = 4;

            _missionProgressLabel = new Label("0/0");
            _missionProgressLabel.style.fontSize = LABEL_FONT_SIZE;
            _missionProgressLabel.style.color = _textColor;
            _missionProgressLabel.style.marginRight = 8;
            _missionProgressLabel.style.minWidth = 50;
            progressRow.Add(_missionProgressLabel);

            var barBg = new VisualElement();
            barBg.style.flexGrow = 1;
            barBg.style.height = 16;
            barBg.style.backgroundColor = _hpBarBgColor;
            barBg.style.borderTopLeftRadius = 3;
            barBg.style.borderTopRightRadius = 3;
            barBg.style.borderBottomLeftRadius = 3;
            barBg.style.borderBottomRightRadius = 3;

            _missionProgressFill = new VisualElement();
            _missionProgressFill.style.height = 16;
            _missionProgressFill.style.width = 0;
            _missionProgressFill.style.borderTopLeftRadius = 3;
            _missionProgressFill.style.borderTopRightRadius = 3;
            _missionProgressFill.style.borderBottomLeftRadius = 3;
            _missionProgressFill.style.borderBottomRightRadius = 3;
            _missionProgressFill.style.backgroundColor = new Color(0.7f, 0.7f, 0.2f, 1f);
            barBg.Add(_missionProgressFill);

            progressRow.Add(barBg);
            _missionPanel.Add(progressRow);

            root.Add(_missionPanel);
        }

        private void UpdateMissionPanel()
        {
            var stats = PlayerStatsModel.Instance;
            if (stats == null) return;

            if (!stats.IsMissionActive)
            {
                _missionPanel.style.display = DisplayStyle.None;
                return;
            }

            _missionPanel.style.display = DisplayStyle.Flex;
            _missionTitleLabel.text = stats.MissionTitle ?? "Миссия";
            _missionDescLabel.text = stats.MissionDescription ?? "";

            float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
            _missionProgressFill.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
            _missionProgressLabel.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }

        private VisualElement CreateProgrammatorPopup()
        {
            var popup = new VisualElement();
            popup.style.position = Position.Absolute;
            popup.style.left = 0;
            popup.style.top = 0;
            popup.style.right = 0;
            popup.style.bottom = 0;
            popup.style.justifyContent = Justify.Center;
            popup.style.alignItems = Align.Center;
            popup.style.display = DisplayStyle.None;

            var dimmer = new VisualElement();
            dimmer.style.position = Position.Absolute;
            dimmer.style.left = 0;
            dimmer.style.top = 0;
            dimmer.style.right = 0;
            dimmer.style.bottom = 0;
            dimmer.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            dimmer.pickingMode = PickingMode.Ignore;
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            panel.style.borderTopWidth = 2;
            panel.style.borderBottomWidth = 2;
            panel.style.borderLeftWidth = 2;
            panel.style.borderRightWidth = 2;
            panel.style.borderTopColor = _panelBorderColor;
            panel.style.borderBottomColor = _panelBorderColor;
            panel.style.borderLeftColor = _panelBorderColor;
            panel.style.borderRightColor = _panelBorderColor;
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 20;
            panel.style.paddingRight = 20;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.minWidth = PROGRAMMATOR_WIDTH;
            panel.style.minHeight = PROGRAMMATOR_HEIGHT;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.marginBottom = 10;

            var title = new Label("Программатор");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = _accentColor;
            title.style.flexGrow = 1;
            topRow.Add(title);

            var closeBtn = new Button(() => popup.style.display = DisplayStyle.None);
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
            closeBtn.RegisterCallback<MouseEnterEvent>(_ =>
                closeBtn.style.color = Color.white);
            closeBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                closeBtn.style.color = new Color(0.7f, 0.7f, 0.7f, 1f));
            topRow.Add(closeBtn);

            panel.Add(topRow);

            var gridScroll = new ScrollView();
            gridScroll.style.flexGrow = 1;
            gridScroll.style.maxHeight = 12 * 34;

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 16 * 34;

            var cellTex = Resources.Load<Texture2D>("Programmator/programmator_84");

            for (int i = 0; i < 16 * 12; i++)
            {
                var btn = new Button(() => { });
                btn.style.width = 30;
                btn.style.height = 30;
                btn.style.backgroundColor = Color.clear;
                btn.style.borderTopWidth = 0;
                btn.style.borderBottomWidth = 0;
                btn.style.borderLeftWidth = 0;
                btn.style.borderRightWidth = 0;
                btn.style.paddingTop = 0;
                btn.style.paddingBottom = 0;
                btn.style.paddingLeft = 0;
                btn.style.paddingRight = 0;
                btn.style.marginLeft = 2;
                btn.style.marginRight = 2;
                btn.style.marginTop = 2;
                btn.style.marginBottom = 2;
                if (cellTex != null)
                {
                    btn.style.backgroundImage = new StyleBackground(cellTex);
                    btn.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                }
                grid.Add(btn);
            }

            gridScroll.Add(grid);
            panel.Add(gridScroll);

            var runBtn = new Button(() =>
            {
                Debug.Log("Запуск программы");
            });
            runBtn.text = "Запуск";
            runBtn.style.marginTop = 10;
            runBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            runBtn.style.borderTopWidth = 2;
            runBtn.style.borderBottomWidth = 2;
            runBtn.style.borderLeftWidth = 2;
            runBtn.style.borderRightWidth = 2;
            runBtn.style.borderTopColor = _panelBorderColor;
            runBtn.style.borderBottomColor = _panelBorderColor;
            runBtn.style.borderLeftColor = _panelBorderColor;
            runBtn.style.borderRightColor = _panelBorderColor;
            runBtn.style.paddingTop = 8;
            runBtn.style.paddingBottom = 8;
            runBtn.style.paddingLeft = 20;
            runBtn.style.paddingRight = 20;
            runBtn.style.alignSelf = Align.Center;
            runBtn.style.color = Color.white;
            runBtn.style.fontSize = 14;
            runBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            runBtn.RegisterCallback<MouseEnterEvent>(_ =>
                runBtn.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f));
            runBtn.RegisterCallback<MouseLeaveEvent>(_ =>
                runBtn.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
            panel.Add(runBtn);

            popup.Add(panel);
            return popup;
        }

        private void CreateProgrammatorButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Программатор";
            btn.style.position = Position.Absolute;
            btn.style.left = 10;
            btn.style.bottom = 215;
            btn.style.width = 100;
            btn.style.height = 28;
            btn.style.fontSize = 12;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.color = _textColor;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _panelBorderColor;
            btn.style.borderBottomColor = _panelBorderColor;
            btn.style.borderLeftColor = _panelBorderColor;
            btn.style.borderRightColor = _panelBorderColor;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
                btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.85f));
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
                btn.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f));

            root.Add(btn);
        }
    }
}
