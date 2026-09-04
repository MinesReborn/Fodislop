# Что изменилось на вид при приведении к макету

Отчёт разовый, для проверки глазами: сравнение с коммитом, на котором заход
начинался. Указание было «приводи всё к макету», поэтому часть значений
менялась НЕ значение-в-значение — игра приводилась к палитре макета.

Замены, где значение сохранилось дословно (370 подстановок отступов, кеглей,
радиусов и точных цветов), сюда не входят: там смотреть нечего.

**Δ** — сдвиг видимой светлоты с учётом прозрачности, на типичном фоне игры.
Ни одной замены со сдвигом больше 40 не осталось: всё, что уезжало сильнее,
переподобрано по светлоте, а не по одному лишь оттенку.

Не тронуто намеренно: `rgb(0,119,255)` в `.auth-vk` — фирменный синий
ВКонтакте. Приведение чужого бренда к своей палитре сломало бы узнаваемость.

## общий слой — 30 правил

| Δ | селектор | было | стало | файл |
|---|---|---|---|---|
| 39 | `.mm-timeline-section--purple` | `rgb(192, 132, 252)` | `--state-anomaly` | Theme.uss |
| 39 | `.mm-tb--purple` | `rgb(192, 132, 252)` | `--state-anomaly` | Theme.uss |
| 39 | `.mm-tag--purple` | `rgb(192, 132, 252)` | `--state-anomaly` | Theme.uss |
| 26 | `.mm-side-btn--danger:hover` | `rgb(230, 57, 70)` | `--state-danger` | Theme.uss |
| 26 | `.mm-side-badge` | `rgb(230, 57, 70)` | `--state-danger` | Theme.uss |
| 26 | `.mm-sc-ping` | `rgb(82, 183, 136)` | `--state-ok` | Theme.uss |
| 26 | `.mm-log-line--success` | `rgb(82, 183, 136)` | `--state-ok` | Theme.uss |
| 14 | `Button#ServerSelectButton` | `rgba(86, 221, 212, 0.38)` | `--accent-cyan-glow` | Theme.uss |
| 14 | `.mm-settings-nav` | `rgba(245, 197, 66, 0.2)` | `--accent-gold-wash` | Theme.uss |
| 14 | `.mm-modal-footer` | `rgba(245, 197, 66, 0.2)` | `--accent-gold-wash` | Theme.uss |
| 14 | `.mm-modal-close:hover` | `rgba(245, 197, 66, 0.2)` | `--accent-gold-wash` | Theme.uss |
| 14 | `.mm-kbd-box` | `rgba(255, 255, 255, 0.18)` | `--light-sheen` | Theme.uss |
| 14 | `.mm-card-tag` | `rgba(245, 197, 66, 0.2)` | `--accent-gold-wash` | Theme.uss |
| 10 | `.sci-fi-btn-dark:hover` | `rgba(26, 40, 58, 0.95)` | `--surface-shelf-dense` | SciFi.uss |
| 10 | `.sci-fi-slot:hover` | `rgba(26, 40, 58, 0.95)` | `--surface-shelf-dense` | Animations.uss |
| 9 | `.mm-timeline-card` | `rgba(86, 221, 212, 0.25)` | `--accent-cyan-glow` | Theme.uss |
| 9 | `.mm-server-card` | `rgba(86, 221, 212, 0.25)` | `--accent-cyan-glow` | Theme.uss |
| 9 | `.mm-repair-log` | `rgba(86, 221, 212, 0.25)` | `--accent-cyan-glow` | Theme.uss |
| 9 | `.mm-card-box` | `rgba(86, 221, 212, 0.25)` | `--accent-cyan-glow` | Theme.uss |
| 7 | `Button.mm-btn-gold:hover` | `rgb(255, 215, 95)` | `--accent-gold-hover` | Theme.uss |
| 7 | `Button#PlayButton:hover` | `rgb(255, 215, 95)` | `--accent-gold-hover` | Theme.uss |
| 7 | `.mm-user-avatar Label` | `rgb(18, 12, 4)` | `--surface-crisis-solid` | Theme.uss |
| 7 | `.mm-profile-avatar Label` | `rgb(18, 12, 4)` | `--surface-crisis-solid` | Theme.uss |
| 5 | `.mm-server-badge` | `rgba(86, 221, 212, 0.15)` | `--accent-cyan-wash` | Theme.uss |
| 5 | `.mm-nav-tab--active` | `rgba(245, 197, 66, 0.15)` | `--accent-gold-wash` | Theme.uss |
| 5 | `.mm-kbd-box` | `rgba(255, 255, 255, 0.08)` | `--light-film` | Theme.uss |
| 4 | `.ui-overlay--blocking` | `rgba(0, 0, 0, 0.9)` | `--surface-scrim` | Theme.uss |
| 3 | `.mm-target-badge` | `rgba(10, 16, 28, 0.95)` | `--surface-slate-dense` | Theme.uss |
| 3 | `.mm-modal-header` | `rgba(14, 22, 38, 0.95)` | `--surface-slate-dense` | Theme.uss |
| 3 | `.sci-fi-input` | `rgba(5, 8, 11, 0.85)` | `--surface-scrim` | SciFi.uss |

## main game — 140 правил

| Δ | селектор | было | стало | файл |
|---|---|---|---|---|
| 40 | `.asset-status-button.asset-status-loadin` | `rgba(70, 70, 70, 0.96)` | `--surface-shelf-solid` | HUD.uss |
| 39 | `.inv-tooltip-bg` | `rgba(80, 140, 200, 0.5)` | `--accent-cyan-fill` | Inventory.uss |
| 39 | `.inv-full-bg` | `rgba(80, 140, 200, 0.5)` | `--accent-cyan-fill` | Inventory.uss |
| 39 | `.inv-context-menu` | `rgba(80, 140, 200, 0.5)` | `--accent-cyan-fill` | Inventory.uss |
| 39 | `.hud-minimap-panel` | `rgba(80, 140, 200, 0.5)` | `--accent-cyan-fill` | HUD.uss |
| 34 | `.prog-dialog-input` | `rgb(77, 77, 77)` | `--border-line` | Programmator.uss |
| 34 | `.prog-dialog-cancel` | `rgb(77, 77, 77)` | `--border-line` | Programmator.uss |
| 34 | `.gchat-send-button` | `rgb(77, 77, 77)` | `--border-line` | Chat.uss |
| 34 | `.gchat-input` | `rgb(77, 77, 77)` | `--border-line` | Chat.uss |
| 34 | `.gchat-color-grid` | `rgb(77, 77, 77)` | `--border-line` | Chat.uss |
| 34 | `.gchat-color-button` | `rgb(77, 77, 77)` | `--border-line` | Chat.uss |
| 34 | `.gchat-channel-button` | `rgb(77, 77, 77)` | `--border-line` | Chat.uss |
| 32 | `.prog-title` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-pager-label` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-page-input .unity-text-input` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-page-input` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-list-title` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-dialog-title` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.prog-create-btn` | `rgb(178, 166, 128)` | `--accent-gold` | Programmator.uss |
| 32 | `.hud-skill-bar-bg` | `rgba(60, 60, 60, 1)` | `--surface-shelf-solid` | HUD.uss |
| 32 | `.lchat-prompt` | `rgb(178, 166, 128)` | `--accent-gold` | Chat.uss |
| 32 | `.gchat-header` | `rgb(178, 166, 128)` | `--accent-gold` | Chat.uss |
| 32 | `.gchat-channel-button--active` | `rgb(178, 166, 128)` | `--accent-gold` | Chat.uss |
| 31 | `.inv-tooltip-desc` | `rgb(210, 225, 240)` | `--accent-cyan-solid` | Inventory.uss |
| 31 | `.hud-button-close` | `rgba(180, 180, 180, 1)` | `--text-secondary` | HUD.uss |
| 29 | `.prog-save-btn` | `rgb(178, 178, 178)` | `--text-secondary` | Programmator.uss |
| 29 | `.prog-dialog-cancel` | `rgb(178, 178, 178)` | `--text-secondary` | Programmator.uss |
| 29 | `.prog-ctrl-btn` | `rgb(178, 178, 178)` | `--text-secondary` | Programmator.uss |
| 29 | `.prog-close-btn` | `rgb(178, 178, 178)` | `--text-secondary` | Programmator.uss |
| 28 | `.hud-mission-progress-fill` | `rgb(179, 179, 51)` | `--accent-gold` | HUD.uss |
| 24 | `.inv-close-btn:hover` | `rgba(255, 80, 80, 0.8)` | `--state-danger` | Inventory.uss |
| 23 | `.prog-panel--running` | `rgb(51, 204, 51)` | `--state-ok` | Programmator.uss |
| 23 | `.prog-create-btn:hover` | `rgb(51, 51, 51)` | `--surface-shelf-solid` | Programmator.uss |
| 23 | `.settings-section--debug` | `rgba(90, 90, 90, 0.65)` | `--border-line` | PauseMenu.uss |
| 23 | `.settings-navigation` | `rgba(90, 90, 90, 0.65)` | `--border-line` | PauseMenu.uss |
| 23 | `.modal-icon` | `rgb(51, 51, 51)` | `--surface-shelf-solid` | Modal.uss |
| 23 | `.gchat-send-button` | `rgb(51, 51, 51)` | `--surface-shelf-solid` | Chat.uss |
| 22 | `.inv-button` | `rgba(80, 180, 255, 0.5)` | `--accent-cyan-fill` | Inventory.uss |
| 21 | `.prog-radial-item` | `rgba(51, 51, 51, 0.95)` | `--surface-shelf-solid` | Programmator.uss |
| 21 | `.prog-joy-item` | `rgba(51, 51, 51, 0.95)` | `--surface-shelf-solid` | Programmator.uss |
| 21 | `.prog-cell` | `rgb(64, 64, 64)` | `--border-line` | Programmator.uss |
| 21 | `.hud-button-claim` | `rgba(40, 130, 40, 1)` | `--light-edge` | HUD.uss |
| 21 | `.gchat-channel-button` | `rgb(170, 170, 170)` | `--text-secondary` | Chat.uss |
| 19 | `.prog-dialog-confirm` | `rgb(51, 128, 51)` | `--light-edge` | Programmator.uss |
| 18 | `.settings-section__title` | `rgba(178, 166, 128, 0.55)` | `--accent-gold-fill` | PauseMenu.uss |
| 18 | `.inv-separator` | `rgba(80, 140, 200, 0.3)` | `--accent-cyan-glow` | Inventory.uss |
| 18 | `.inv-close-btn` | `rgba(80, 140, 200, 0.3)` | `--accent-cyan-glow` | Inventory.uss |
| 18 | `.hud-button-claim:hover` | `rgba(50, 130, 50, 1)` | `--light-edge` | HUD.uss |
| 17 | `.hud-minimap-container` | `rgba(60, 100, 150, 0.6)` | `--light-sheen` | HUD.uss |
| 16 | `.inv-cell:hover` | `rgba(80, 180, 255, 0.9)` | `--accent-cyan-dense` | Inventory.uss |
| 16 | `.gchat-channel-button--active` | `rgb(255, 239, 190)` | `--light-solid` | Chat.uss |
| 14 | `.settings-tab--advanced` | `rgba(190, 120, 70, 0.75)` | `--accent-gold-fill` | PauseMenu.uss |
| 14 | `.settings-section--debug` | `rgba(190, 120, 70, 0.75)` | `--accent-gold-fill` | PauseMenu.uss |
| 12 | `.prog-stop-btn` | `rgba(89, 0, 0, 0.3)` | `--surface-crisis-dense` | Programmator.uss |
| 12 | `.prog-stop-btn` | `rgb(229, 77, 77)` | `--state-danger` | Programmator.uss |
| 12 | `.prog-radial-ring--outer` | `rgba(20, 20, 20, 0.45)` | `--border-hairline` | Programmator.uss |
| 12 | `.prog-del-btn` | `rgb(229, 77, 77)` | `--state-danger` | Programmator.uss |
| 12 | `.settings-section--custom` | `rgba(35, 31, 24, 0.98)` | `--surface-ember-solid` | PauseMenu.uss |
| 12 | `.inv-title` | `rgb(220, 235, 255)` | `--text-primary` | Inventory.uss |
| 12 | `.inv-context-btn` | `rgb(220, 235, 255)` | `--text-primary` | Inventory.uss |
| 12 | `.hud-toggle-btn-label` | `rgba(230, 77, 77, 1)` | `--state-danger` | HUD.uss |
| 12 | `.hud-panel .hud-creds` | `rgb(200, 200, 0)` | `--accent-gold` | HUD.uss |
| 12 | `.hud-hp-bar` | `rgba(40, 40, 40, 1)` | `--surface-shelf-solid` | HUD.uss |
| 12 | `.hud-bar` | `rgba(40, 40, 40, 1)` | `--surface-shelf-solid` | HUD.uss |
| 11 | `.prog-del-btn` | `rgba(77, 0, 0, 0.3)` | `--surface-crisis-dense` | Programmator.uss |
| 11 | `.inv-cell:hover` | `rgba(30, 42, 62, 0.9)` | `--surface-shelf-dense` | Inventory.uss |
| 11 | `.inv-cell--highlight` | `rgba(50, 70, 100, 0.8)` | `--light-sheen` | Inventory.uss |
| 11 | `.hud-toggle-btn` | `rgba(38, 13, 13, 0.85)` | `--surface-crisis-dense` | HUD.uss |
| 10 | `.programmator-cell.has-operator` | `rgba(178, 166, 128, 0.3)` | `--accent-gold-glow` | Programmator.uss |
| 10 | `.programmator-cell` | `rgba(38, 38, 38, 1)` | `--surface-shelf-solid` | Programmator.uss |
| 10 | `.prog-radial-back:hover` | `rgb(255, 214, 0)` | `--accent-gold` | Programmator.uss |
| 10 | `.prog-radial-back` | `rgba(38, 38, 38, 0.95)` | `--surface-shelf-dense` | Programmator.uss |
| 10 | `.prog-list-row:hover` | `rgb(38, 38, 38)` | `--surface-shelf-solid` | Programmator.uss |
| 10 | `.prog-joy-item:hover` | `rgb(255, 214, 0)` | `--accent-gold` | Programmator.uss |
| 10 | `.prog-dialog-cancel` | `rgb(38, 38, 38)` | `--surface-shelf-solid` | Programmator.uss |
| 10 | `.prog-create-btn` | `rgb(38, 38, 38)` | `--surface-shelf-solid` | Programmator.uss |
| 10 | `.prog-cell--hover` | `rgb(255, 214, 0)` | `--accent-gold` | Programmator.uss |
| 10 | `.prog-cell` | `rgb(38, 38, 38)` | `--surface-shelf-solid` | Programmator.uss |
| 10 | `.inv-tooltip-name` | `rgb(255, 214, 0)` | `--accent-gold` | Inventory.uss |
| 10 | `.inv-cell--selected` | `rgb(255, 214, 0)` | `--accent-gold` | Inventory.uss |
| 10 | `.hud-toggle-btn:hover` | `rgba(64, 13, 13, 0.85)` | `--light-film` | HUD.uss |
| 10 | `.hud-toggle-btn.enabled` | `rgba(13, 38, 13, 0.85)` | `--surface-slate-dense` | HUD.uss |
| 10 | `.hud-mission-progress-bar` | `rgba(38, 38, 38, 1)` | `--surface-shelf-solid` | HUD.uss |
| 10 | `.hud-button-accent` | `rgba(38, 38, 38, 1)` | `--surface-shelf-solid` | HUD.uss |
| 9 | `.prog-run-btn` | `rgb(102, 229, 102)` | `--state-ok` | Programmator.uss |
| 9 | `.prog-joy-item:hover` | `rgba(89, 89, 89, 0.95)` | `--accent-cyan-deep` | Programmator.uss |
| 9 | `.prog-dialog-confirm` | `rgb(102, 229, 102)` | `--state-ok` | Programmator.uss |
| 9 | `.settings-section--effects` | `rgba(105, 145, 190, 0.75)` | `--text-tertiary` | PauseMenu.uss |
| 8 | `.prog-list-row` | `rgb(51, 51, 51)` | `--border-line` | Programmator.uss |
| 8 | `.settings-section--custom` | `rgba(205, 170, 90, 0.8)` | `--text-secondary` | PauseMenu.uss |
| 8 | `.hud-button-claim` | `rgba(25, 100, 25, 1)` | `--accent-cyan-glow` | HUD.uss |
| 8 | `.gchat-scroll` | `rgb(51, 51, 51)` | `--border-line` | Chat.uss |
| 7 | `.tooltip-panel` | `rgba(26, 26, 26, 0.95)` | `--surface-slate-dense` | Modal.uss |
| 7 | `.tooltip-panel` | `rgb(102, 102, 102)` | `--text-disabled` | Modal.uss |
| 7 | `.inv-cell` | `rgba(80, 140, 200, 0.4)` | `--accent-cyan-glow` | Inventory.uss |
| 7 | `.inv-button:hover` | `rgba(50, 85, 130, 0.95)` | `--accent-cyan-glow` | Inventory.uss |
| 7 | `.inv-button:hover` | `rgba(100, 210, 255, 0.9)` | `--accent-cyan-dense` | Inventory.uss |
| 7 | `.hud-clan-button` | `rgb(102, 102, 102)` | `--text-disabled` | HUD.uss |
| 7 | `.gchat-swatch` | `rgb(102, 102, 102)` | `--text-disabled` | Chat.uss |
| 6 | `.prog-radial-ring--inner` | `rgba(31, 31, 31, 0.5)` | `--surface-raised` | Programmator.uss |
| 6 | `.hud-panel .hud-money` | `rgb(0, 200, 0)` | `--text-secondary` | HUD.uss |
| 6 | `.asset-status-button` | `rgba(25, 25, 25, 0.92)` | `--surface-slate-dense` | HUD.uss |
| 5 | `.prog-panel` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Programmator.uss |
| 5 | `.prog-list-panel` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Programmator.uss |
| 5 | `.prog-dialog-panel` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Programmator.uss |
| 5 | `.prog-create-btn` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Programmator.uss |
| 5 | `.settings-tab--active` | `rgba(20, 20, 20, 1)` | `--surface-solid` | PauseMenu.uss |
| 5 | `.settings-section--effects` | `rgba(29, 34, 44, 0.96)` | `--surface-shelf-solid` | PauseMenu.uss |
| 5 | `.settings-section` | `rgba(30, 34, 41, 0.92)` | `--surface-shelf-dense` | PauseMenu.uss |
| 5 | `.hud-clan-button` | `rgb(77, 77, 77)` | `--accent-gold-glow` | HUD.uss |
| 5 | `.asset-status-panel` | `rgba(20, 20, 20, 0.96)` | `--surface-solid` | HUD.uss |
| 5 | `.lchat-overlay` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Chat.uss |
| 5 | `.gchat-panel` | `rgb(89, 89, 89)` | `--accent-cyan-deep` | Chat.uss |
| 4 | `.prog-list-name` | `rgb(204, 204, 204)` | `--accent-gold-hover` | Programmator.uss |
| 4 | `.prog-dialog-input .unity-text-input` | `rgb(204, 204, 204)` | `--accent-gold-hover` | Programmator.uss |
| 4 | `.prog-dialog-input` | `rgb(204, 204, 204)` | `--accent-gold-hover` | Programmator.uss |
| 4 | `.prog-cell--selected` | `rgb(51, 128, 255)` | `--light-edge` | Programmator.uss |
| 4 | `.inv-button` | `rgba(30, 50, 75, 0.85)` | `--light-sheen` | Inventory.uss |
| 4 | `.hud-skill-bar-fill` | `rgb(0, 180, 0)` | `--light-edge` | HUD.uss |
| 4 | `.hud-btn-action` | `rgba(90, 90, 90, 1)` | `--accent-cyan-deep` | HUD.uss |
| 4 | `.asset-status-panel` | `rgba(120, 120, 120, 0.9)` | `--accent-cyan-fill` | HUD.uss |
| 3 | `.prog-radial-item` | `rgb(128, 128, 128)` | `--light-edge` | Programmator.uss |
| 3 | `.prog-radial-back` | `rgb(128, 128, 128)` | `--light-edge` | Programmator.uss |
| 3 | `.prog-page-input .unity-text-input` | `rgb(31, 31, 31)` | `--surface-shelf-solid` | Programmator.uss |
| 3 | `.prog-page-input` | `rgb(31, 31, 31)` | `--surface-shelf-solid` | Programmator.uss |
| 3 | `.prog-joy-item` | `rgb(128, 128, 128)` | `--light-edge` | Programmator.uss |
| 3 | `.prog-dialog-input .unity-text-input` | `rgb(31, 31, 31)` | `--surface-shelf-solid` | Programmator.uss |
| 3 | `.prog-dialog-input` | `rgb(31, 31, 31)` | `--surface-shelf-solid` | Programmator.uss |
| 3 | `.pause-panel` | `rgba(16, 18, 22, 0.97)` | `--surface-solid` | PauseMenu.uss |
| 3 | `.pause-btn-confirm` | `rgba(153, 51, 51, 1)` | `--state-magma-glow` | PauseMenu.uss |
| 3 | `.pause-btn-confirm` | `rgba(128, 38, 38, 1)` | `--state-danger-glow` | PauseMenu.uss |
| 3 | `.inv-full-panel` | `rgba(0, 0, 0, 0.6)` | `--surface-sunken` | Inventory.uss |
| 3 | `.inv-close-btn` | `rgba(0, 0, 0, 0.3)` | `--surface-void-haze` | Inventory.uss |
| 3 | `.inv-cell` | `rgba(20, 26, 38, 0.85)` | `--surface-shelf-dense` | Inventory.uss |
| 3 | `.world-map-overlay` | `rgba(0, 0, 0, 0.82)` | `--surface-scrim` | HUD.uss |
| 3 | `.hud-toggle-btn-label.enabled` | `rgba(77, 230, 77, 1)` | `--state-ok` | HUD.uss |
| 3 | `.hud-btn-action` | `rgba(25, 25, 38, 0.85)` | `--surface-shelf-dense` | HUD.uss |
| 3 | `.gchat-scroll` | `rgba(0, 0, 0, 0.3)` | `--surface-void-haze` | Chat.uss |
| 3 | `.gchat-color-grid` | `rgba(31, 31, 31, 0.95)` | `--surface-shelf-dense` | Chat.uss |
| 3 | `.gchat-channel-button--active` | `rgba(89, 74, 42, 0.9)` | `--accent-gold-glow` | Chat.uss |


## добавка: 19 значений «вне палитры» (закрыт долг `design-debt-uss.md`)

Эти значения раньше были вынесены в отчёт о долге, потому что точного токена
им не нашлось. По указанию «приводи всё к макету» они подобраны **по роли**,
а не по числу: hover-фон панели берёт следующую ступень той же поверхности,
рамка — токен рамки того же цвета, заливка состояния — wash того же состояния.

| селектор | было | стало | файл |
|---|---|---|---|
| `.mm-target-badge` | `rgba(245,197,66,0.4)` | `--border-gold` | Theme.uss |
| `.mm-side-btn` | `rgba(86,221,212,0.4)` | `--border-cyan` | Theme.uss |
| `.mm-server-badge` | `rgba(86,221,212,0.4)` | `--border-cyan` | Theme.uss |
| `.mm-user-pill:hover` | `rgba(26,40,68,0.95)` | `--surface-shelf-dense` | Theme.uss |
| `.mm-side-btn:hover` | `rgba(28,44,72,1)` | `--surface-shelf-dense` | Theme.uss |
| `.mm-side-btn--danger` | `rgba(230,57,70,0.7)` | `--border-danger` | Theme.uss |
| `.mm-side-btn--danger:hover` | `rgba(60,20,25,0.95)` | `--surface-crisis-dense` | Theme.uss |
| `Button#ServerSelectButton:hover` | `rgba(26,40,68,0.95)` | `--surface-shelf-dense` | Theme.uss |
| `.mm-loader-progress-track` | `rgba(146,167,176,0.2)` | `--border-line` | Theme.uss |
| `.mm-news-ticker:hover` | `rgba(26,40,68,0.95)` | `--surface-shelf-dense` | Theme.uss |
| `Button.mm-btn-outline:hover` | `rgba(26,40,68,0.85)` | `--light-film` | Theme.uss |
| `.mm-server-card:hover` | `rgba(26,40,68,0.95)` | `--surface-shelf-dense` | Theme.uss |
| `.mm-server-card--active` | `rgba(30,48,80,0.95)` | `--surface-shelf-solid` | Theme.uss |
| `.mm-tc--purple` | `rgba(192,132,252,0.4)` | `--state-anomaly` | Theme.uss |
| `.sci-fi-btn-cyan:hover` | `rgba(38,198,218,0.35)` | `--accent-cyan-wash` | SciFi.uss |
| `.sci-fi-btn-red` | `rgba(231,76,60,0.5)` | `--border-danger` | SciFi.uss |
| `.sci-fi-btn-red:hover` | `rgba(231,76,60,0.45)` | `--state-danger-wash` | SciFi.uss |
| `.sci-fi-window-overlay` | `rgba(5,8,11,0.75)` | `--surface-scrim` | SciFi.uss |
| `.sci-fi-slot--selected` | `rgba(242,169,0,0.15)` | `--accent-gold-wash` | Animations.uss |

Два места смотреть в первую очередь, там приём сменился, а не оттенок:
`Button.mm-btn-outline:hover` — вместо синей заливки белая плёнка поверх
прозрачного фона, ровно как `components.css:415` в макете; и красные кнопки
`.sci-fi-btn-red:hover` — заливка стала заметно бледнее (0.45 → 0.15).

## добавка: места, где вида не было вовсе

Это не замены значений, а починка. В каждом случае класс существовал, а
правил под ним не было — экран показывал не «старый вид», а никакой.

| место | что было | что стало | файл |
|---|---|---|---|
| ползунок громкости в онбординге (`onb-slider*`) | контрол рисовался стандартной темой Unity: серая дорожка и ручка посреди оформленного экрана | дорожка и ручка по образцу макета (`css/screens/modals.css`, `.slider-container`), ширина группы равна соседнему `.onb-select` | Auth.uss |
| поле ввода, не принявшее ввод (`.invalid`) | отказ происходил молча: значение откатывалось без единого признака | рамка и текст цветом опасности, приём из `components.css:422` | Input.uss |
| экран загрузки бутстрапа | четыре сырых цвета мимо палитры, своя система видимости через модификатор `--visible` | токены общей палитры, видимость через `is-hidden` | BootstrapLoadingScreen.uss |

Отдельно: стрелка миссии (`.mission-arrow`) не рисуется вообще — правил под
классом нет, инлайном задаются только позиция и поворот, то есть элемент
нулевого размера. Файл в `UI/Overlays`, это main game, поэтому не тронут и
заведён в `TODO.md`.

Экрана загрузки бутстрапа **в макете не существует** — его вид выведен по
аналогии с оверлеями, а не из источника истины. Тоже в `TODO.md`.

## добавка: иконки рейла

Растровые иконки игры печатались из SVG макета вручную и отстали: набор в
макете нормализовали по массе, а PNG остались прежними. Теперь их печатает
`visual/fodinae-ui-lab/tools/emit-icon-textures.py`, а расхождение ловит
`checkIconsMatchMirror` в сборке — как у токенов.

Три глифа сдвинулись (настройки, восстановление, выход) — разница в доли
пикселя по центровке, на глаз почти не видна. Discord, Telegram и VK совпали
побайтово: они и были напечатаны этим же путём.

Сама кнопка приведена к макету (`css/screens/chrome.css`, `.side-icon-btn`):

| свойство | было | стало |
|---|---|---|
| размер кнопки | 44px | 48px |
| толщина рамки | 1.5px | 1px |
| глиф при наведении | `--accent-cyan-hover` | `--light-solid` (белый) |
| фон при наведении | `--surface-shelf-dense` | `--surface-shelf-solid` |
| рамка при наведении | `--accent-gold` | `--accent-cyan` |

Наведение — самое заметное здесь: раньше бирюзовый глиф подсвечивался чуть
более светлым бирюзовым и отклик почти не читался; в макете он белеет.

Хроника и обновление: кнопок с такой ролью в рейле макета не было вовсе, и
сначала я оставил их старые растры как невыводимые. Это неправильная граница —
она значит, что две иконки навсегда живут вне системы. Поэтому глифы
`i-chronicle` (журнал с корешком) и `i-update` (загрузка в приёмник)
дорисованы в спрайте макета в его же манере: обводка 1.8, скруглённые концы,
центр в (12,12). Масса выровнена по набору — краска 24.9% и 15.4% при разбросе
набора 13.6–25.1%, то есть оба внутри, а форма у каждого своя (правило из
[[project-icon-system]]: приравнивать массу, а не габарит). Кнопки добавлены в
рейл макета, и порядок там теперь тот же, что в игре: хроника, настройки,
восстановление, обновление | соцсети | выход.

Правился и сам макет: спрайт `fdn-sprite` был невалидным XML — у `i-pick` и
`i-gear` по лишнему `</g>`, следы снятой обёртки `transform`. Браузер это
прощает, любой XML-инструмент — нет, и генератор иконок на этом падал.

## добавка: сверка компонентов (крупнейшая часть захода)

Токены игра и макет делят с точностью до значения, но **словарь — не текст**:
одними и теми же значениями собираются разные экраны. Пока сверялся только
словарь, кнопка рейла жила 44 пикселя против 48, ширины модалок стояли
наоборот, ретикул был квадратным, а шрифт в шести правилах перебивался вторым
объявлением и не действовал вовсе.

Теперь сверяется сказанное: `visual/fodinae-ui-lab/component-map.json` —
карта из **85 пар** компонентов, построенная по разметке (index.html и
MainMenu.uxml прочитаны рядом, секция за секцией, — по месту и роли элемента,
а не по имени: одноимённых классов всего 14). По ней
`tools/compare-components.py` сравнивает правила свойство за свойством с
разрешением токенов: `var(--space-6)` и `12px` — одно значение, и ловить такую
«разницу» значило бы утопить настоящие находки в шуме.

Найдено и устранено **99 расхождений** на 256 сравнимых свойств. Крупное:

| что | было | стало |
|---|---|---|
| ширины модалок | обычная 800, «широкая» 920 | наоборот, как в макете: 920 и 800 |
| ретикул цели | скругление 4px — квадрат | `--radius-pill` — круг |
| бусина хроники | квадратная, 12px, смещение −24 | круглая, 14px, −30 |
| шрифт в 6 правилах | `var(--face-*)`, перебитый вторым `resource(...)` | одно объявление, токен действует |
| гарнитуры | заголовок хроники Exo, тег Unbounded, бейдж цели Unbounded | как в макете: display, data, data |
| вкладка настроек | 12px Unbounded, отступ 10×16 | 15px Exo, отступ 12×20 |
| колонка меню | 480px | 500px |
| пилюля профиля, тикер, разделители | сплошные заливки | плёнки `--border-hairline` / `--border-subtle` |

Остались **21 расхождение, устранить которые нельзя**, и у каждого записана
причина: градиентов, дробного веса шрифта (600, 800) и относительных единиц
(88vh) в USS нет вовсе, а положение планеты и маркеров задаёт сцена — код
проецирует 3D-точку на кадр, тогда как в макете это числа статичной картинки.

Держит всё `checkComponentsMatchMirror` в сборке: расхождение, которое можно
устранить, роняет проверку. Проверено контрпримером.
