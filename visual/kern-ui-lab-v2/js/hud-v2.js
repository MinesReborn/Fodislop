/* HUD v2: данные и поведение игрового экрана по реальным клиентам.
   Подключается после app.js; объявления функций ниже заменяют одноимённые
   из app.js (глобальная привязка общая), остальная логика макета не тронута.
   Источники форматов: Legacy GUIManager.cs / ChatManager.cs / MiniSkill.cs /
   PopupManager.cs, сервер up_building/mod.rs, Kern PlayerHUDView. */

// Корзина: шесть кристаллов G R B V W C и общая вместимость (BasketContents/BasketCapacity)
const CRYSTALS = [
  { key: 'g', file: 'green', color: '#00ff42' },
  { key: 'r', file: 'red', color: '#ff6c6b' },
  { key: 'b', file: 'blue', color: '#5377ff' },
  { key: 'v', file: 'violet', color: '#ff31cf' },
  { key: 'w', file: 'white', color: '#ffffff' },
  { key: 'c', file: 'cyan', color: '#00fdff' },
];
const hud = {
  level: 4,
  basket: { g: 412, r: 96, b: 1204, v: 0, w: 0, c: 57 },
  basketCapacity: 3600,
  busy: false,
  chatMode: 0,            // 0 — три строки, 1 — колонка, 2 — скрыт (TAB/кнопка в Legacy)
  chatPanel: 'chat',
  channel: 'FED',
  updateRequired: true,
  touch: false,
  progRunning: false,
};

// ── Форматы Legacy ─────────────────────────────────────────────
const groupDigits = n => String(Math.trunc(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
// SetBasketCrys: <1000 как есть, <1e6 с пробелами, дальше K и KK
function formatCrys(n) {
  if (n < 1e6) return groupDigits(n);
  if (n < 1e10) return groupDigits(Math.floor(n / 1000)) + ' K';
  return groupDigits(Math.floor(n / 1e6)) + ' KK';
}
// SetBasket: цвет подписи и полос по порогам 50/100/115/200
function cargoStyle(p) {
  if (p < 50) return { text: '#777', bar: 'rgb(191,191,191)', over: null };
  if (p < 100) return { text: '#7f7', bar: 'rgb(64,252,64)', over: null };
  if (p < 115) return { text: '#ff5', bar: 'rgb(242,242,64)', over: 'rgb(179,128,38)' };
  if (p < 200) return { text: '#f75', bar: 'rgb(252,166,64)', over: 'rgb(179,0,38)' };
  return { text: '#f55', bar: 'rgb(230,89,89)', over: 'rgb(179,0,38)' };
}
// SetHP: цвет числа и полосы по доле здоровья
function hpColor(f, a = 1) {
  const r = Math.sqrt(Math.sqrt(1 - f)), g = Math.sqrt(Math.sqrt(f)), b = f * (1 - f);
  return `rgba(${Math.round(r * 255)},${Math.round(g * 255)},${Math.round(b * 255)},${a})`;
}

function updateHpUI() {
  const f = Math.max(0, Math.min(1, playerState.hp / playerState.maxHp));
  const fill = document.getElementById('hpFill');
  const text = document.getElementById('hpText');
  if (fill) { fill.style.width = (f * 100) + '%'; fill.style.background = hpColor(f, 0.7); }
  if (text) { text.textContent = playerState.hp + 'HP'; text.style.color = hpColor(f); }
}

function updateMoneyUI() {
  const money = document.getElementById('hudMoney');
  const creds = document.getElementById('hudCrystals');
  if (money) money.textContent = '$' + groupDigits(playerState.money);
  if (creds) creds.textContent = groupDigits(playerState.crystals);
}

function updateBasketUI() {
  const grid = document.getElementById('basketMiniGrid');
  const panel = document.getElementById('hudCargo');
  if (!grid) return;
  const total = CRYSTALS.reduce((s, c) => s + hud.basket[c.key], 0);
  // Груз в процентах — максимум по кристаллам относительно общей вместимости
  const percent = Math.round(Math.max(...CRYSTALS.map(c => hud.basket[c.key])) * 100 / hud.basketCapacity);
  const st = cargoStyle(percent);
  const badge = document.getElementById('basketCapBadge');
  if (badge) { badge.textContent = percent + '%'; badge.style.color = st.text; }
  const cap = document.getElementById('capFill');
  const over = document.getElementById('capOverFill');
  if (cap) { cap.style.width = Math.min(100, percent) + '%'; cap.style.background = st.bar; }
  if (over) { over.style.width = st.over ? Math.min(100, Math.max(0, percent - 100)) + '%' : '0'; over.style.background = st.over || 'transparent'; }
  grid.innerHTML = CRYSTALS.filter(c => hud.basket[c.key] > 0).map(c =>
    `<div class="g-cry g-mono" style="color:${c.color}"><img class="g-px" src="img/kern/crystals/${c.file}.png" alt="">${formatCrys(hud.basket[c.key])}</div>`).join('');
  if (panel) panel.hidden = total === 0;   // все нули — панели груза нет
}

function claimBonus() {
  playerState.money += 500;
  playerState.crystals += 25;
  updateMoneyUI();
  showToast('Бонусы: +$500, +25 кредитов', 'ok');
}

// ── Копание: кристалл в корзину, опыт навыкам ───────────────────
const ORE_TO_CRYSTAL = { 'ore-titanium': 'c', 'ore-gold': 'g' };
function mineTileBlock(tile) {
  if (tile.classList.contains('mined-empty')) return;
  let crystal = null;
  for (const [cls, key] of Object.entries(ORE_TO_CRYSTAL)) if (tile.classList.contains(cls)) crystal = key;
  if (tile.classList.contains('lava-crack')) { playSound('alert'); simulateDamage(40); }
  tile.className = 'mine-tile mined-empty';
  delete tile.dataset.sfx;

  if (playerState.missionBlocks < playerState.missionTarget) {
    playerState.missionBlocks++;
    const pct = Math.round(playerState.missionBlocks / playerState.missionTarget * 100);
    const fill = document.getElementById('missionFill');
    const metric = document.getElementById('missionMetric');
    if (fill) fill.style.width = pct + '%';
    if (metric) metric.textContent = `${playerState.missionBlocks} / ${playerState.missionTarget}`;
  }
  growSkill('d', 9);                                    // Копание растёт от каждого блока
  if (crystal) { hud.basket[crystal] += 1 + Math.floor(rand() * 4); growSkill('m', 14); updateBasketUI(); }
}

// ── Навыки игрока (начальные слоты PlayerSkills) ────────────────
// код → [уровень, опыт 0..1, цвет SkillButton.colors]
const SKILL_SLOTS = { 0: 'm', 1: 'd', 2: 'M', 3: 'l' };
const skills = {
  m: { name: 'Добыча', level: 9, exp: 0.4, color: '#ff6600' },
  d: { name: 'Копание', level: 14, exp: 0.2, color: '#ffff00' },
  M: { name: 'Передвижение', level: 12, exp: 1.0, color: '#00ffe6' },
  l: { name: 'Защита', level: 12, exp: 0.6, color: '#ff00ff' },
};
const SLOT_POS = [20,110,21,153,38,75,38,191,74,94,74,169,59,132,100,132,51,22,93,36,51,241,93,226,281,50,256,121,312,23,307,87,377,27,376,76,374,137,375,176,165,46,145,131,185,142,332,153,203,24,248,76,341,57,343,103,138,87,248,17,297,133,214,115,175,98,205,68];
const SLOT_AMOUNT = 20;
// get_installable_skills: все навыки сервера, кроме установленных, в порядке сервера
const INSTALLABLE = ['A','L','Y','E','O','xL','F','t','p','b','c','g','r','v','w','xI','u','e'];

// build_skill_description (up_building/mod.rs): формат сервера, числа — по его формулам
function skillDescription(code) {
  const s = skills[code];
  const exp = `${s.exp.toFixed(1)}/1.0`;
  if (code === 'M') {
    const effect = Math.max(30, 70 - s.level * 0.05);
    return `Передвижение Уровень:${s.level}\nExp - ${exp}\nСкорость передвижения ${(1 / (effect * 1.2 * 0.001) * 0.3 * 3.6).toFixed(2)} км/ч`;
  }
  if (code === 'd') return `Копание Уровень:${s.level}\nExp - ${exp}\nСила копания: ${100 + s.level * 10}`;
  if (code === 'l') return `Защита Уровень:${s.level}\nExp - ${exp}\nМакс. здоровье: ${100 + s.level * 3}`;
  const x = s.level;
  const mult = x <= 0 ? 0.08 : 0.08 + Math.log10(x) * (Math.pow(x, 0.5) / 4);
  return `Добыча Уровень:${s.level}\nExp - ${exp}\nМножитель добычи: ${mult.toFixed(2)}`;
}

// MiniSkill: иконка появляется при росте навыка и живёт 5 с; при готовом
// улучшении остаётся с меткой «up», полоса красная.
const miniTimers = {};
function growSkill(code, _icon) {
  const s = skills[code];
  if (!s) return;
  s.exp = Math.min(1, s.exp + 0.08);
  renderMini(code);
}
function renderMini(code) {
  const wrap = document.getElementById('hudMiniSkills');
  if (!wrap) return;
  const s = skills[code];
  let el = wrap.querySelector(`[data-skill="${code}"]`);
  if (!el) {
    el = document.createElement('button');
    el.type = 'button';
    el.className = `g-mini sk sk-${code}`;
    el.dataset.skill = code;
    el.title = s.name;
    el.onclick = () => openSkillsWindow(Number(Object.keys(SKILL_SLOTS).find(k => SKILL_SLOTS[k] === code)));
    el.innerHTML = '<i class="g-mini-up"></i><i class="g-mini-bar"></i>';
    wrap.appendChild(el);
  }
  const ready = s.exp >= 1;
  el.style.setProperty('--c', s.color);
  el.classList.toggle('is-ready', ready);
  el.querySelector('.g-mini-bar').style.height = (ready ? 100 : s.exp * 100) + '%';
  clearTimeout(miniTimers[code]);
  if (!ready) miniTimers[code] = setTimeout(() => el.remove(), 5000);
}

// ── Окно навыков (UpPage) ───────────────────────────────────────
function openSkillsWindow(selected) {
  const robo = document.getElementById('skillsRobo');
  const panel = document.getElementById('skillsPanel');
  if (!robo || !panel) return;
  robo.querySelectorAll('.g-slot').forEach(n => n.remove());
  // как PopupManager: слоты создаются с последнего, первый рисуется поверх
  for (let i = SLOT_AMOUNT - 1; i >= 0; i--) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'g-slot';
    b.style.left = `calc(${SLOT_POS[2 * i]} * var(--g))`;
    b.style.top = `calc(${SLOT_POS[2 * i + 1]} * var(--g))`;
    const code = SKILL_SLOTS[i];
    if (code) {
      const s = skills[code];
      b.style.setProperty('--c', s.color);
      b.title = s.name;
      b.innerHTML = `<i class="g-slot-icon sk sk-${code}"></i><span class="g-slot-lvl">${s.level}</span>` + (s.exp >= 1 ? '<i class="g-slot-ready"></i>' : '');
    } else {
      b.classList.add('is-empty');
      b.title = 'Пустой слот';
    }
    if (i === selected) b.classList.add('is-selected');
    b.onclick = () => openSkillsWindow(i);
    robo.appendChild(b);
  }
  const code = SKILL_SLOTS[selected];
  if (selected < 0) {
    panel.innerHTML = `<div class="g-skill-text">Выберите скилл или пустой слот</div>
      <button type="button" class="btn-gold-action g-skill-act" onclick="buySkillSlot()" data-sfx="confirm">Купить слот (1000 кредов)</button>`;
  } else if (code) {
    const s = skills[code];
    panel.innerHTML = `<i class="g-skill-big sk sk-${code}"></i>
      <div class="g-skill-tools"><button type="button" class="g-skill-tool" title="Удалить" onclick="showToast('Удаление навыка — подтверждение сервера')"><i class="g-ico g-ico--delete"></i></button>
      <button type="button" class="g-skill-tool" title="Замок" onclick="this.classList.toggle('is-on')"><i class="g-ico g-ico--lock"></i></button></div>
      <div class="g-skill-text">${skillDescription(code)}</div>` +
      (s.exp >= 1 ? `<button type="button" class="btn-gold-action g-skill-act" onclick="upgradeSkill('${code}', ${selected})" data-sfx="confirm">Улучшить</button>` : '');
  } else {
    panel.innerHTML = `<div class="g-skill-text">Выберите навык для установки</div>
      <div class="g-install">${INSTALLABLE.map(c => `<button type="button" class="g-install-item sk sk-${c}" onclick="showToast('Установка — подтверждение сервера')"></button>`).join('')}</div>`;
  }
  if (getOpenModal()?.id !== 'skillsModal') openModal('skillsModal');
}
function upgradeSkill(code, slot) {
  skills[code].level++;
  skills[code].exp = 0;
  if (code === 'l') playerState.maxHp = 100 + skills.l.level * 3;
  document.querySelector(`#hudMiniSkills [data-skill="${code}"]`)?.remove();
  updateLevel();
  updateHpUI();
  openSkillsWindow(slot);
}
function buySkillSlot() {
  if (playerState.crystals <= 1000) { showToast('Нужно больше 1000 кредов', 'warn'); return; }
  playerState.crystals -= 1000;
  updateMoneyUI();
}
function updateLevel() {
  // уровень игрока — сумма уровней навыков (lvlsummary)
  const lvl = Object.values(skills).reduce((s, k) => s + k.level, 0);
  const el = document.getElementById('hudLevel');
  if (el) el.textContent = 'ур.' + lvl;
}

// ── Чат: три режима Legacy, каналы сервера FED/DNO ─────────────
const chatHistory = {
  FED: [
    { sys: true, text: 'Сервер: добро пожаловать.' },
    { t: '18:42', nick: 'ШАХТЁР-102', color: '#ffd9a8', text: 'кто у респа?' },
    { t: '18:43', nick: 'ШАХТЁР-774', color: '#c8f0ff', text: 'я, иду вниз' },
    { t: '18:44', nick: 'DVM_LEADER', color: '#e6ccff', text: 'голубые на 1200:840' },
  ],
  DNO: [{ t: '18:20', nick: 'ШАХТЁР-305', color: '#d8ffc8', text: 'кто на дне?' }],
};
const CHANNEL_NAMES = { FED: 'Федеральный чат', DNO: 'Дно' };
function renderChat() {
  const lines = chatHistory[hud.channel];
  const down = document.getElementById('chatDownLines');
  const col = document.getElementById('chatColumnLines');
  const line = m => m.sys ? `<div class="g-line g-line--sys">${escapeHtml(m.text)}</div>`
    : `<div class="g-line"><span class="g-line-t">${m.t}</span> <span style="color:${m.color}">${escapeHtml(m.nick)}</span>: ${escapeHtml(m.text)}</div>`;
  if (col) col.innerHTML = lines.map(line).join('');
  // DownChat: последние три сообщения, «ник: текст»
  if (down) down.innerHTML = lines.filter(m => !m.sys).slice(-3)
    .map(m => `<div><span style="color:${m.color}">${escapeHtml(m.nick)}</span>: ${escapeHtml(m.text)}</div>`).join('');
  for (const tag of Object.keys(chatHistory)) {
    const last = chatHistory[tag].filter(m => !m.sys).slice(-1)[0];
    const el = document.getElementById('chatLast' + tag);
    if (el && last) { const s = `${last.nick}: ${last.text}`; el.textContent = s.length > 22 ? s.slice(0, 20) + '...' : s; }
  }
  const title = document.getElementById('chatTitle');
  if (title) title.textContent = hud.chatPanel === 'list' ? 'СПИСОК ЧАТОВ' : `${hud.channel} – ${CHANNEL_NAMES[hud.channel]}`;
}
function cycleChatMode() {
  hud.chatMode = (hud.chatMode + 1) % 3;
  document.getElementById('gameHud').dataset.chat = hud.chatMode;
  if (hud.chatMode === 1) setTimeout(() => document.getElementById('chatColumnInput')?.focus(), 0);
}
function setChatPanel(panel) {
  hud.chatPanel = panel;
  document.getElementById('chatChannelList').hidden = panel !== 'list';
  document.getElementById('chatColumnLines').hidden = panel === 'list';
  document.getElementById('chatColumnInput').hidden = panel === 'list';
  document.getElementById('gameHud').classList.toggle('is-chat-list', panel === 'list');
  if (panel === 'list') document.getElementById('chatUnread').hidden = true;
  renderChat();
}
function selectChatChannel(tag) { hud.channel = tag; setChatPanel('chat'); }
function sendColumnChat() {
  const input = document.getElementById('chatColumnInput');
  const text = input.value.trim();
  if (!text) return;
  const now = new Date();
  chatHistory[hud.channel].push({ t: `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`, nick: 'ШАХТЁР-774', color: '#c8f0ff', text });
  input.value = '';
  playSound('click');
  renderChat();
}

// ── Кнопки правого верха и прочее ───────────────────────────────
function toggleFullscreen() {
  if (document.fullscreenElement) document.exitFullscreen?.(); else document.documentElement.requestFullscreen?.();
}
function toggleHudSound(btn) { btn.classList.toggle('is-off'); }
function doRespawn() {
  closeModal('respawnModal');
  for (const c of CRYSTALS) hud.basket[c.key] = Math.floor(hud.basket[c.key] * 0.9);
  updateBasketUI();
  showToast('Респаун: 10% груза потеряно, остальное упаковано в бокс');
}
function toggleHudProgram() {
  hud.progRunning = !hud.progRunning;
  const run = document.getElementById('hudProgRun');
  const label = document.getElementById('hudProgLabel');
  run.innerHTML = hud.progRunning ? '<span class="g-stop">■</span>' : '<i class="g-ico g-ico--play"></i>';
  run.title = hud.progRunning ? 'Остановить программу' : 'Запустить программу';
  label.innerHTML = hud.progRunning ? '<span class="g-srv">имя программы</span>' : 'Программатор';
  document.getElementById('hudOnlineProg').textContent = hud.progRunning ? '7' : '6';
}

// Нагруженное состояние: всё, что видно только по данным сервера
function toggleHudBusy() {
  hud.busy = !hud.busy;
  const btn = document.getElementById('devBusyToggle');
  btn?.setAttribute('aria-pressed', String(hud.busy));
  btn?.classList.toggle('active', hud.busy);
  document.getElementById('gameHud').classList.toggle('is-busy', hud.busy);
  const geo = document.getElementById('hudGeology');
  geo.hidden = !hud.busy;
  geo.innerHTML = 'Геология: 3/10 (<span class="g-srv">текст</span>)';
  document.getElementById('hudStates').innerHTML = hud.busy
    ? '<div class="g-state" style="color:#8fd3ff"><span class="g-srv">строка состояния</span></div><div class="g-state" style="color:#ffb35c"><span class="g-srv">строка состояния</span></div>' : '';
  document.getElementById('hudInvHint').hidden = !hud.busy;
  hud.basket = hud.busy ? { g: 4608, r: 96, b: 1204, v: 12, w: 3, c: 57 } : { g: 412, r: 96, b: 1204, v: 0, w: 0, c: 57 };
  if (hud.busy !== hud.progRunning) toggleHudProgram();
  updateBasketUI();
}

function toggleTouchLayout(force) {
  hud.touch = force ?? !hud.touch;
  document.body.classList.toggle('is-touch', hud.touch);
  const btn = document.getElementById('devTouchToggle');
  btn?.setAttribute('aria-pressed', String(hud.touch));
  btn?.classList.toggle('active', hud.touch);
}

// Обязательное обновление: заменяет кнопку спуска, пока клиент не обновлён
function applyUpdateState() {
  const need = hud.updateRequired && !isGameUpdated;
  document.getElementById('btnUpdateRequired').hidden = !need;
  document.getElementById('updateRequiredNote').hidden = !need;
  document.getElementById('btnStartDescent').hidden = need;
  const btn = document.getElementById('devUpdateToggle');
  if (btn) { btn.textContent = need ? 'ОБНОВЛЕНИЕ: НУЖНО' : 'ОБНОВЛЕНИЕ: НЕТ'; btn.setAttribute('aria-pressed', String(need)); }
}
function toggleUpdateRequired() {
  hud.updateRequired = !hud.updateRequired;
  if (hud.updateRequired) isGameUpdated = false; else isGameUpdated = true;
  applyUpdateState();
}

// Перехват клавиш до app.js: Enter — колонка чата (Legacy), Tab остаётся инвентарём
document.addEventListener('keydown', e => {
  if (currentMode !== 'ingame' || getOpenModal()) return;
  const inField = e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA');
  if (e.key === 'Enter' && !inField) {
    e.preventDefault();
    e.stopImmediatePropagation();
    if (hud.chatMode !== 1) { hud.chatMode = 0; cycleChatMode(); } else document.getElementById('chatColumnInput')?.focus();
  }
  if (inField && e.key !== 'Escape') e.stopImmediatePropagation();
}, true);

document.addEventListener('DOMContentLoaded', () => {
  // после инициализации app.js: прежние значения state в формате Legacy
  playerState.maxHp = 100 + skills.l.level * 3;
  playerState.hp = Math.round(playerState.maxHp * 0.83);
  updateHpUI();
  updateMoneyUI();
  updateBasketUI();
  updateLevel();
  renderChat();
  renderMini('M');                                  // Передвижение готово к улучшению
  const mf = document.getElementById('missionFill');
  if (mf) mf.style.width = Math.round(playerState.missionBlocks / playerState.missionTarget * 100) + '%';
  applyUpdateState();
  if (matchMedia('(hover: none) and (pointer: coarse)').matches) toggleTouchLayout(true);
});
