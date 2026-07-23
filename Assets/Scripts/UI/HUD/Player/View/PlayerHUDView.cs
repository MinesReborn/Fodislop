using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.UI.HUD.Player.Model;
using Fodinae.Scripts.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.HUD.Player.View
{
    public class PlayerHUDView : MonoBehaviour
    {
        private const int PANEL_WIDTH = 240;
        private const int PADDING = 12;
        private const int LABEL_FONT_SIZE = 14;
        private const int TITLE_FONT_SIZE = 14;
        private const int HP_BAR_HEIGHT = 14;
        private const int BTN_SIZE = 50;
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
        private Tooltip _tooltip;
        private bool _isLoaded;
        private IVisualElementScheduledItem _skeletonPulse;
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
        private Button _collisionButton;
        private Label _collisionLabel;
        private VisualElement _currentSkillRow;
        private int _skillCountInRow = 0;
        private Button _chatButton;
        private VisualElement _statusPanel;
        private readonly Dictionary<string, VisualElement> _statusLineElements = new();

        private VisualElement _respawnPopup;
        private VisualElement _buildingsPopup;
        private VisualElement _faqPopup;
        private ProgrammatorGrid _programmatorGrid;

        private Button _missionButton;
        private VisualElement _missionPanel;
        private Label _missionTitleLabel;
        private Label _missionDescLabel;
        private VisualElement _missionProgressFill;
        private Label _missionProgressLabel;

        protected void Start()
        {
            StartAsync(this.destroyCancellationToken).Forget();
        }

        private async UniTaskVoid StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            await LoadCrystalTextures(cancellationToken);
            if (cancellationToken.IsCancellationRequested || this == null)
            {
                return;
            }

            InitializeHUD();
        }

        protected void OnDestroy()
        {
            if (PlayerStatsModel.Instance != null)
            {
                PlayerStatsModel.Instance.OnStatsChanged -= RefreshAll;
                PlayerStatsModel.Instance.OnSkillProgress -= OnSkillProgress;
                PlayerStatsModel.Instance.OnDailyBonusChanged -= UpdateDailyBonusPanel;
                PlayerStatsModel.Instance.OnStatusLinesChanged -= RebuildStatusPanel;
                PlayerStatsModel.Instance.OnMissionChanged -= UpdateMissionPanel;
            }

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.OnAutoDigChanged -= UpdateAutoDigButton;
                player.OnAggressionChanged -= UpdateAggressionButton;
            }

            if (GlobalChatUI.Instance != null)
            {
                GlobalChatUI.Instance.Hide();
            }
        }

        private async UniTask LoadCrystalTextures(System.Threading.CancellationToken cancellationToken)
        {
            _crystalTextures.Clear();
            foreach (CrystalType ct in Enum.GetValues(typeof(CrystalType)))
            {
                if (ct == CrystalType.Unknown)
                {
                    continue;
                }

                string name = ct.ToString().ToLowerInvariant();
                var tex = await ClientAssetLoader.Instance.GetTextureAsync("Crystals/" + name, cancellationToken);
                if (cancellationToken.IsCancellationRequested || this == null)
                {
                    return;
                }

                _crystalTextures.Add(tex);
            }
        }

        private void InitializeHUD()
        {
            _doc = FindAnyObjectByType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[PlayerHUD] UIDocument не найден на сцене");
                return;
            }

            _tooltip = new Tooltip();
            _tooltip.Initialize(_doc);

            CreatePanel(_doc.rootVisualElement);
            CreateBonusButton(_doc.rootVisualElement);
            CreateBonusPanel(_doc.rootVisualElement);
            CreateAggressionToggle(_doc.rootVisualElement);
            CreateAutoDigToggle(_doc.rootVisualElement);
            CreateCollisionToggle(_doc.rootVisualElement);
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

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.OnAutoDigChanged += UpdateAutoDigButton;
            }

            if (player != null)
            {
                player.OnAggressionChanged += UpdateAggressionButton;
            }

            if (PlayerStatsModel.Instance != null)
            {
                PlayerStatsModel.Instance.OnDailyBonusChanged += UpdateDailyBonusPanel;
            }

            RebuildCrystalRows();
            PlayerStatsModel.Instance.OnStatsChanged += RefreshAll;
            _isLoaded = PlayerStatsModel.Instance.Health > 0 || PlayerStatsModel.Instance.Level > 0;
            if (!_isLoaded)
            {
                StartSkeletonPulse();
            }

            RefreshAll();

            var root = _doc.rootVisualElement;

            // Условная блокировка навигации: когда открыто окно — Tab/стрелки работают (IsInputBlocked),
            // когда окна нет — блокируем, чтобы стрелки управляли движением.
            root.RegisterCallback<NavigationMoveEvent>(
                evt =>
            {
                if (!PacketHandler.IsInputBlocked)
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<NavigationSubmitEvent>(
                evt =>
            {
                if (!PacketHandler.IsInputBlocked && !ChatInput.IsFocused)
                {
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            // Escape — закрывает верхнее модальное окно через UIInputManager
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape && PacketHandler.IsInputBlocked)
                {
                    UIInputManager.Instance.TryPopTopModal();
                    evt.StopPropagation();
                }
            });
        }

        private void CreatePanel(VisualElement root)
        {
            _panel = new VisualElement();
            _panel.name = "PlayerHUD";
            _panel.AddToClassList("hud-panel");

            var topRow = new VisualElement();
            topRow.AddToClassList("hud-title-row");

            _nicknameLabel = new Label("---");
            _nicknameLabel.AddToClassList("hud-nickname");
            topRow.Add(_nicknameLabel);

            _levelLabel = new Label("Ур: 0");
            _levelLabel.AddToClassList("hud-level");
            topRow.Add(_levelLabel);

            _panel.Add(topRow);

            var separator = new VisualElement();
            separator.AddToClassList("hud-separator");
            _panel.Add(separator);

            _hpLabel = new Label("Прочность: 0/0");
            _hpLabel.AddToClassList("hud-stat");
            _panel.Add(_hpLabel);

            var hpContainer = new VisualElement();
            hpContainer.AddToClassList("hud-hp-bar");

            _hpBarFill = new VisualElement();
            _hpBarFill.AddToClassList("hud-hp-fill");
            hpContainer.Add(_hpBarFill);

            _panel.Add(hpContainer);

            _moneyLabel = new Label("$ 0");
            _moneyLabel.AddToClassList("hud-money");
            _panel.Add(_moneyLabel);

            _credsLabel = new Label("C 0");
            _credsLabel.AddToClassList("hud-creds");
            _panel.Add(_credsLabel);

            _geologyLabel = new Label("Геология: 0/0");
            _geologyLabel.AddToClassList("hud-stat");
            _panel.Add(_geologyLabel);

            var basketSep = new VisualElement();
            basketSep.AddToClassList("hud-separator");
            _panel.Add(basketSep);

            _basketPercentLabel = new Label("Груз: 0%");
            _basketPercentLabel.AddToClassList("hud-basket");
            _basketPercentLabel.style.color = _accentColor;
            _panel.Add(_basketPercentLabel);

            _basketContainer = new VisualElement();
            _basketContainer.name = "BasketCrystals";
            _basketContainer.AddToClassList("hud-basket-container");
            _panel.Add(_basketContainer);

            root.Add(_panel);
        }

        private void CreateBonusButton(VisualElement root)
        {
            _bonusButton = new Button(ToggleBonusPanel);
            _bonusButton.text = "Бонусы";
            _bonusButton.AddToClassList("hud-button-accent");
            _bonusButton.style.position = Position.Absolute;
            _bonusButton.style.left = 10 + PANEL_WIDTH + GAP;
            _bonusButton.style.top = 10;
            _bonusButton.style.width = 90;
            _bonusButton.style.height = BTN_SIZE;
            Tooltip.AttachTo(_bonusButton, "Открыть панель бонусов", _tooltip);

            root.Add(_bonusButton);
        }

        private void CreateBonusPanel(VisualElement root)
        {
            _bonusPanel = new VisualElement();
            _bonusPanel.AddToClassList("hud-bonus-panel");
            _bonusPanel.style.position = Position.Absolute;
            _bonusPanel.style.left = 10 + PANEL_WIDTH + GAP + 90 + GAP;
            _bonusPanel.style.top = 10;

            var titleRow = new VisualElement();
            titleRow.AddToClassList("hud-title-row");

            var title = new Label("Бонусы");
            title.AddToClassList("hud-stat");
            titleRow.Add(title);

            var closeBtn = new Button(ToggleBonusPanel);
            closeBtn.text = "×";
            closeBtn.AddToClassList("hud-button-close");
            titleRow.Add(closeBtn);

            _bonusPanel.Add(titleRow);

            _bonusStatusLabel = new Label("Ежедневный бонус: ...");
            _bonusStatusLabel.AddToClassList("hud-stat");
            _bonusStatusLabel.AddToClassList("hud-stat-wrap");
            _bonusPanel.Add(_bonusStatusLabel);

            _bonusClaimButton = new Button(ClaimDailyBonus);
            _bonusClaimButton.text = "Забрать";
            _bonusClaimButton.AddToClassList("hud-button-claim");
            _bonusClaimButton.style.display = DisplayStyle.None;
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
            {
                UpdateDailyBonusPanel();
            }

            UpdateStatusPanelPosition();
        }

        private void UpdateStatusPanelPosition()
        {
            if (_statusPanel == null)
            {
                return;
            }

            if (_isBonusOpen && _bonusPanel != null)
            {
                _bonusPanel.schedule.Execute(() =>
                {
                    if (!_isBonusOpen)
                    {
                        return;
                    }

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
            if (_bonusStatusLabel == null)
            {
                return;
            }

            var stats = PlayerStatsModel.Instance;
            if (stats == null)
            {
                return;
            }

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
            _statusPanel.AddToClassList("hud-status-panel");
            _statusPanel.style.position = Position.Absolute;
            _statusPanel.style.left = 10 + PANEL_WIDTH + GAP;
            _statusPanel.style.top = 10 + BTN_SIZE + GAP;
            _statusPanel.style.display = DisplayStyle.None;
            root.Add(_statusPanel);
        }

        private void RebuildStatusPanel()
        {
            if (_statusPanel == null)
            {
                return;
            }

            var stats = PlayerStatsModel.Instance;
            if (stats == null)
            {
                return;
            }

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
                {
                    toRemove.Add(kvp.Key);
                }
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
                    {
                        UpdateStatusLabel(label, kvp.Value);
                    }

                    label.style.color = kvp.Value.Color;
                }
                else
                {
                    var row = new Label();
                    row.AddToClassList("hud-status-line");
                    row.style.color = kvp.Value.Color;
                    UpdateStatusLabel(row, kvp.Value);
                    _statusPanel.Add(row);

                    if (kvp.Value.Expiry > 0)
                    {
                        row.schedule.Execute(() =>
                        {
                            if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                            {
                                return;
                            }

                            var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                            if (entry.Text == null)
                            {
                                return;
                            }

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
                label.text = string.Empty;
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
            {
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }

            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void ClaimDailyBonus()
        {
            Debug.Log("[PlayerHUD] ClaimDailyBonus: sending claim request");
            NetworkService.Send(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
        }

        private void CreateAutoDigToggle(VisualElement root)
        {
            _autoDigButton = new Button(ToggleAutoDig);
            _autoDigButton.text = string.Empty;
            _autoDigButton.AddToClassList("hud-button");
            _autoDigButton.style.position = Position.Absolute;
            _autoDigButton.style.left = 10;
            _autoDigButton.style.bottom = 281;

            _autoDigLabel = new Label("Копать ✗");
            _autoDigLabel.AddToClassList("hud-button-label");
            _autoDigLabel.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _autoDigButton.Add(_autoDigLabel);

            _autoDigButton.RegisterCallback<MouseEnterEvent>(_ =>
                _autoDigButton.style.backgroundColor = new Color(0.25f, 0.05f, 0.05f, 0.85f));
            _autoDigButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _autoDigButton.style.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.85f));
            Tooltip.AttachTo(_autoDigButton, "Автоматическое копание блоков", _tooltip);

            root.Add(_autoDigButton);
        }

        private void CreateAggressionToggle(VisualElement root)
        {
            _aggressionButton = new Button(ToggleAggression);
            _aggressionButton.text = string.Empty;
            _aggressionButton.AddToClassList("hud-button");
            _aggressionButton.style.position = Position.Absolute;
            _aggressionButton.style.left = 10;
            _aggressionButton.style.bottom = 314;

            _aggressionLabel = new Label("Агрессия ✗");
            _aggressionLabel.AddToClassList("hud-button-label");
            _aggressionLabel.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _aggressionButton.Add(_aggressionLabel);

            _aggressionButton.RegisterCallback<MouseEnterEvent>(_ =>
                _aggressionButton.style.backgroundColor = new Color(0.25f, 0.05f, 0.05f, 0.85f));
            _aggressionButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _aggressionButton.style.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.85f));
            Tooltip.AttachTo(_aggressionButton, "Робот атакует враждебных существ", _tooltip);

            root.Add(_aggressionButton);
        }

        private void CreateSkillContainer(VisualElement root)
        {
            _skillContainer = new VisualElement();
            _skillContainer.name = "MiniSkills";
            _skillContainer.AddToClassList("hud-skill-container");
            root.Add(_skillContainer);
        }

        private void EnsureSkillRow()
        {
            if (_currentSkillRow != null && _skillCountInRow < SKILL_GRID_COLS)
            {
                return;
            }

            _currentSkillRow = new VisualElement();
            _currentSkillRow.AddToClassList("hud-skill-row");
            _skillContainer.Add(_currentSkillRow);
            _skillCountInRow = 0;
        }

        private (Label arrow, VisualElement barFill) CreateSkillIcon(SkillType skill)
        {
            EnsureSkillRow();

            var cell = new VisualElement();
            cell.AddToClassList("hud-skill-icon");

            var iconColumn = new VisualElement();
            iconColumn.AddToClassList("hud-skill-icon-column");

            var arrow = new Label("up");
            arrow.AddToClassList("hud-skill-arrow");
            iconColumn.Add(arrow);

            var iconImage = new Image();
            iconImage.style.width = 24;
            iconImage.style.height = 24;

            var tex = Resources.Load<Texture2D>($"Skills/{skill}");
            if (tex != null)
            {
                iconImage.image = tex;
            }
            else
            {
                iconImage.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            }

            iconColumn.Add(iconImage);
            cell.Add(iconColumn);

            var barContainer = new VisualElement();
            barContainer.style.width = 6;
            barContainer.style.height = 24;
            barContainer.style.marginLeft = 2;
            barContainer.style.flexDirection = FlexDirection.Column;
            barContainer.style.justifyContent = Justify.FlexEnd;

            var barFill = new VisualElement();
            barFill.AddToClassList("hud-skill-bar-fill");
            barFill.style.width = 6;
            barFill.style.height = 2;
            barContainer.Add(barFill);
            cell.Add(barContainer);

            _currentSkillRow.Add(cell);
            _skillCountInRow++;

            _skillIcons[skill] = (arrow, barFill);
            return (arrow, barFill);
        }

        private void CreateCollisionToggle(VisualElement root)
        {
            _collisionButton = new Button(ToggleCollision);
            _collisionButton.text = string.Empty;
            _collisionButton.AddToClassList("hud-button");
            _collisionButton.style.position = Position.Absolute;
            _collisionButton.style.left = 10;
            _collisionButton.style.bottom = 347;

            _collisionLabel = new Label("Стены ✗");
            _collisionLabel.AddToClassList("hud-button-label");
            _collisionLabel.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            _collisionButton.Add(_collisionLabel);

            _collisionButton.RegisterCallback<MouseEnterEvent>(_ =>
                _collisionButton.style.backgroundColor = new Color(0.25f, 0.05f, 0.05f, 0.85f));
            _collisionButton.RegisterCallback<MouseLeaveEvent>(_ =>
                _collisionButton.style.backgroundColor = new Color(0.15f, 0.05f, 0.05f, 0.85f));
            Tooltip.AttachTo(_collisionButton, "Игнорирование коллизий со стенами", _tooltip);

            root.Add(_collisionButton);
        }

        private void ToggleAutoDig()
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.AutoDig = !player.AutoDig;
            }
        }

        private void UpdateAutoDigButton(bool enabled)
        {
            if (_autoDigLabel == null)
            {
                return;
            }

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
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.ToggleAggression();
            }
        }

        private void UpdateAggressionButton(bool enabled)
        {
            if (_aggressionLabel == null)
            {
                return;
            }

            _aggressionLabel.text = enabled ? "Агрессия ✓" : "Агрессия ✗";
            _aggressionLabel.style.color = enabled
                ? new Color(0.3f, 0.9f, 0.3f, 1f)
                : new Color(0.9f, 0.3f, 0.3f, 1f);
            _aggressionButton.style.backgroundColor = enabled
                ? new Color(0.05f, 0.15f, 0.05f, 0.85f)
                : new Color(0.15f, 0.05f, 0.05f, 0.85f);
        }

        private void ToggleCollision()
        {
            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                player.IgnoreCollision = !player.IgnoreCollision;
            }

            UpdateCollisionButton(player != null && player.IgnoreCollision);
        }

        private void UpdateCollisionButton(bool enabled)
        {
            if (_collisionLabel == null)
            {
                return;
            }

            _collisionLabel.text = enabled ? "Стены ✓" : "Стены ✗";
            _collisionLabel.style.color = enabled
                ? new Color(0.3f, 0.9f, 0.3f, 1f)
                : new Color(0.9f, 0.3f, 0.3f, 1f);
            _collisionButton.style.backgroundColor = enabled
                ? new Color(0.05f, 0.15f, 0.05f, 0.85f)
                : new Color(0.15f, 0.05f, 0.05f, 0.85f);
        }

        private void StartSkeletonPulse()
        {
            const float pulseMin = 0.3f;
            const float pulseMax = 0.7f;
            const float pulseDuration = 0.8f;
            float t = 0f;
            bool rising = true;

            _skeletonPulse = _panel.schedule.Execute(() =>
            {
                if (_panel == null)
                {
                    return;
                }

                float dt = Time.unscaledDeltaTime;
                t += rising ? dt : -dt;
                if (t >= pulseDuration)
                {
                    t = pulseDuration;
                    rising = false;
                }
                else if (t <= 0f)
                {
                    t = 0f;
                    rising = true;
                }

                float alpha = Mathf.Lerp(pulseMin, pulseMax, t / pulseDuration);
                _nicknameLabel.style.opacity = alpha;
                _levelLabel.style.opacity = alpha;
                _hpLabel.style.opacity = alpha;
                _hpBarFill.style.opacity = alpha;
                _moneyLabel.style.opacity = alpha;
                _credsLabel.style.opacity = alpha;
                _geologyLabel.style.opacity = alpha;
                _basketPercentLabel.style.opacity = alpha;
            }).Every(16);
        }

        private void StopSkeletonPulse()
        {
            if (_skeletonPulse != null)
            {
                _skeletonPulse.Pause();
                _skeletonPulse = null;
            }

            _nicknameLabel.style.opacity = 1;
            _levelLabel.style.opacity = 1;
            _hpLabel.style.opacity = 1;
            _hpBarFill.style.opacity = 1;
            _moneyLabel.style.opacity = 1;
            _credsLabel.style.opacity = 1;
            _geologyLabel.style.opacity = 1;
            _basketPercentLabel.style.opacity = 1;
        }

        private void RefreshAll()
        {
            if (this == null)
            {
                return;
            }

            var stats = PlayerStatsModel.Instance;
            if (stats == null)
            {
                return;
            }

            if (!_isLoaded && (stats.Health > 0 || stats.Level > 0 || stats.Money > 0 || !string.IsNullOrEmpty(stats.Nickname)))
            {
                _isLoaded = true;
                StopSkeletonPulse();
            }

            _nicknameLabel.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
            _levelLabel.text = _isLoaded ? $"Ур: {stats.Level:N0}" : "Ур: ---";
            _hpLabel.text = _isLoaded ? $"Прочность: {stats.Health:N0}/{stats.MaxHealth:N0}" : "Прочность: --/--";
            _hpLabel.style.opacity = 1;

            float pct = stats.HealthPercent;
            _hpBarFill.style.width = new Length(pct * 100, LengthUnit.Percent);
            _hpBarFill.style.backgroundColor = pct < 0.25f ? _hpBarLowColor : _hpBarFillColor;

            _moneyLabel.text = _isLoaded ? $"$ {stats.Money:N0}" : "$ ---";
            _credsLabel.text = _isLoaded ? $"C {stats.Creds:N0}" : "C ---";

            _geologyLabel.text = string.IsNullOrEmpty(stats.GeologyText) || !_isLoaded
                ? "Геология: 0/0"
                : $"Геология: {stats.GeologyCurrent}/{stats.GeologyMax} ({stats.GeologyText})";

            _basketPercentLabel.text = _isLoaded ? $"Груз: {stats.BasketMaxPercent}%" : "Груз: --%";
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
                {
                    dot.style.backgroundImage = new StyleBackground(_crystalTextures[i]);
                }

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
            if (val >= 1_000_000)
            {
                return $"{val / 1_000_000f:F1}M";
            }

            if (val >= 10_000)
            {
                return $"{val / 1_000}K";
            }

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

            icon.arrow.text = progress >= 1f ? "up" : string.Empty;

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
            _chatButton.AddToClassList("hud-btn-action");
            _chatButton.style.left = 10;
            _chatButton.style.bottom = 248;
            _chatButton.style.width = 100;
            Tooltip.AttachTo(_chatButton, "Открыть чат", _tooltip);

            root.Add(_chatButton);
        }

        private void CreateButtonsAndPopups(VisualElement root)
        {
            _respawnPopup = CreateRespawnPopup();
            _buildingsPopup = CreatePopup("Мои здания");
            _faqPopup = CreatePopup("FAQ");
            _programmatorGrid = gameObject.AddComponent<ProgrammatorGrid>();
            root.Add(_respawnPopup);
            root.Add(_buildingsPopup);
            root.Add(_faqPopup);

            CreateRespawnButton(root, () => _respawnPopup.style.display = DisplayStyle.Flex);
            CreateMyBuildingsButton(root, () => _buildingsPopup.style.display = DisplayStyle.Flex);
            CreateFaqButton(root, () => _faqPopup.style.display = DisplayStyle.Flex);
            CreateProgrammatorButton(root, () => _programmatorGrid.Show());
            CreateModalTestButton(root);
            CreateClanButtons(root);
            CreateMissionButton(root);
        }

        private VisualElement CreatePopup(string title)
        {
            var popup = new VisualElement();
            popup.AddToClassList("popup-overlay");

            var dimmer = new VisualElement();
            dimmer.AddToClassList("popup-dimmer");
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.AddToClassList("popup-panel");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("popup-title");
            panel.Add(titleLabel);

            var closeBtn = new Button(() => popup.style.display = DisplayStyle.None);
            closeBtn.text = "Закрыть";
            closeBtn.AddToClassList("popup-close-btn");

            panel.Add(closeBtn);
            popup.Add(panel);
            return popup;
        }

        private VisualElement CreateRespawnPopup()
        {
            var popup = new VisualElement();
            popup.AddToClassList("popup-overlay");

            var dimmer = new VisualElement();
            dimmer.AddToClassList("popup-dimmer");
            popup.Add(dimmer);

            var panel = new VisualElement();
            panel.AddToClassList("popup-panel");

            var titleLabel = new Label("Респавн");
            titleLabel.AddToClassList("popup-title");
            panel.Add(titleLabel);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("popup-btn-row");

            var okBtn = new Button(() =>
            {
                NetworkService.Instance.SendAction(new SuicidePacket());
                popup.style.display = DisplayStyle.None;
            });
            okBtn.text = "ОК";
            okBtn.AddToClassList("popup-btn");
            btnRow.Add(okBtn);

            var backBtn = new Button(() => popup.style.display = DisplayStyle.None);
            backBtn.text = "Назад";
            backBtn.AddToClassList("popup-btn");
            btnRow.Add(backBtn);

            panel.Add(btnRow);
            popup.Add(panel);
            return popup;
        }

        private void CreateRespawnButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Респавн";
            btn.AddToClassList("hud-btn-action");
            btn.style.top = 10;
            btn.style.right = 10 + ((100 + 6) * 2);
            btn.style.width = 100;
            root.Add(btn);
        }

        private void CreateMyBuildingsButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Мои здания";
            btn.AddToClassList("hud-btn-action");
            btn.style.top = 10;
            btn.style.right = 10 + (100 + 6);
            btn.style.width = 100;
            root.Add(btn);
        }

        private void CreateFaqButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "FAQ";
            btn.AddToClassList("hud-btn-action");
            btn.style.top = 10;
            btn.style.right = 10;
            btn.style.width = 100;
            root.Add(btn);
        }

        private void CreateModalTestButton(VisualElement root)
        {
            var btn = new Button(() =>
            {
                NetworkService.Send(new ElementClickPacket("test_modal", 0, System.Array.Empty<StringPairPacket>()));
            });
            btn.text = "Тест модального окна";
            btn.AddToClassList("hud-btn-action");
            btn.style.top = 10;
            btn.style.right = 10 + ((100 + 6) * 3);
            btn.style.width = 160;
            root.Add(btn);
        }

        private void CreateClanButtons(VisualElement root)
        {
            var joinBtn = new Button(() =>
            {
                NetworkService.Send(new ElementClickPacket("join_clan", 0, System.Array.Empty<StringPairPacket>()));
            });
            joinBtn.text = "Вступить в клан";
            joinBtn.AddToClassList("hud-btn-action");
            joinBtn.style.top = 10;
            joinBtn.style.right = 10 + ((100 + 6) * 3) + 160 + 6;
            joinBtn.style.width = 140;
            root.Add(joinBtn);

            var leaveBtn = new Button(() =>
            {
                NetworkService.Send(new ElementClickPacket("leave_clan", 0, System.Array.Empty<StringPairPacket>()));
            });
            leaveBtn.text = "Выйти из клана";
            leaveBtn.AddToClassList("hud-btn-action");
            leaveBtn.style.top = 10 + 28 + 6;
            leaveBtn.style.right = 10 + ((100 + 6) * 3) + 160 + 6;
            leaveBtn.style.width = 140;
            root.Add(leaveBtn);
        }

        private void CreateMissionButton(VisualElement root)
        {
            _missionButton = new Button(() =>
            {
                NetworkService.Send(new ElementClickPacket("open_missions", 0, System.Array.Empty<StringPairPacket>()));
            });
            _missionButton.text = "Миссии";
            _missionButton.AddToClassList("hud-btn-action");
            _missionButton.style.top = 10 + 28 + 6 + 28 + 6;
            _missionButton.style.right = 10 + ((100 + 6) * 3) + 160 + 6;
            _missionButton.style.width = 140;
            root.Add(_missionButton);
        }

        private void CreateMissionPanel(VisualElement root)
        {
            _missionPanel = new VisualElement();
            _missionPanel.name = "MissionPanel";
            _missionPanel.AddToClassList("hud-mission-panel");
            _missionPanel.style.position = Position.Absolute;
            _missionPanel.style.top = 50;
            _missionPanel.style.left = new Length(50, LengthUnit.Percent);
            _missionPanel.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _missionPanel.style.minWidth = 300;
            _missionPanel.style.flexDirection = FlexDirection.Column;
            _missionPanel.style.display = DisplayStyle.None;

            _missionTitleLabel = new Label("---");
            _missionTitleLabel.AddToClassList("hud-stat");
            _missionTitleLabel.style.color = _accentColor;
            _missionTitleLabel.style.marginBottom = 4;
            _missionPanel.Add(_missionTitleLabel);

            _missionDescLabel = new Label(string.Empty);
            _missionDescLabel.AddToClassList("hud-stat");
            _missionDescLabel.AddToClassList("hud-stat-wrap");
            _missionDescLabel.style.marginBottom = 8;
            _missionPanel.Add(_missionDescLabel);

            var progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;
            progressRow.style.marginBottom = 4;

            _missionProgressLabel = new Label("0/0");
            _missionProgressLabel.AddToClassList("hud-stat");
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
            if (stats == null)
            {
                return;
            }

            if (!stats.IsMissionActive)
            {
                _missionPanel.style.display = DisplayStyle.None;
                return;
            }

            _missionPanel.style.display = DisplayStyle.Flex;
            _missionTitleLabel.text = stats.MissionTitle ?? "Миссия";
            _missionDescLabel.text = stats.MissionDescription ?? string.Empty;

            float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
            _missionProgressFill.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
            _missionProgressLabel.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }

        private void CreateProgrammatorButton(VisualElement root, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = "Программатор";
            btn.AddToClassList("hud-btn-action");
            btn.style.left = 10;
            btn.style.bottom = 215;
            btn.style.width = 100;
            root.Add(btn);
        }
    }
}
