// HUD MainGame: данные, форматы и поведение.
// Форматы — Legacy GUIManager (NHP, $N, Груз N%, пороги груза, сокращения
// кристаллов), чат — Legacy ChatManager (3 режима, каналы FED/DNO сервера),
// мини-навыки — Legacy MiniSkill (видны 5 с при росте, готовые остаются).
(() => {
  const $ = id => document.getElementById(id);
  const token = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  // Системные кнопки узнаются по русской подписи, снятой до перевода: title меняется вместе с языком
  document.querySelectorAll('.sysbuttons button').forEach(b => { b.dataset.sys = b.title; });
  const group = n => String(Math.trunc(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

  const state = {
    hp: 113, maxHp: 136, money: 14850, creds: 340,
    basket: { g: 412, r: 96, b: 1204, v: 0, w: 0, c: 57 }, capacity: 1800, geo: [3, 10],
    chat: 0, channel: 'FED', autodig: false, prog: false,
  };
  const CRYSTALS = [['g', 'green'], ['r', 'red'], ['b', 'blue'], ['v', 'violet'], ['w', 'white'], ['c', 'cyan']];
  // Режим стенда «много денег и груза» (список «Груз и деньги», ?rich=1|2|3): уровень 1 — деньги до 100 млрд (КК), 2 — ×1000 (ККК), 3 — ×1 000 000 (КККК); груз с лимитом 10 млрд ×то же
  const RICH = { money: 99165000000, creds: 1250000, capacity: 10000000000, basket: { g: 9900000000, r: 320000000, b: 4700000000, v: 85000000, w: 12000000, c: 1300000000 } };
  let richOn = 0, normal = null;

  // Число от 100 000 сокращается тысячами — K, KK, KKK, KKKK, перед буквами не больше 99 999 (как в окнах сервера, js/srvgui.js: short):
  // панель не растёт от данных. Полное значение — по наведению (.pk-cut из css/srvgui.css). Деньги, кредиты и кристаллы
  const cut = n => {
    if (n < 1e5) return group(n);
    let k = 0, v = n; while (v >= 1e5 && k < 4) { v /= 1e3; k++; }
    return `<span class="pk-cut" tabindex="0" data-full="${group(n)}">${group(Math.floor(v))} ${'K'.repeat(k)}</span>`;
  };
  // SetBasket: цвет и полосы по порогам 50 / 100 / 115 / 200
  const cargoTone = p => p < 50 ? [token('--cargo-low'), token('--cargo-low-fill'), null] : p < 100 ? [token('--cargo-normal'), token('--cargo-normal-fill'), null]
    : p < 115 ? [token('--cargo-warn'), token('--cargo-warn-fill'), token('--cargo-warn-line')] : p < 200 ? [token('--cargo-high'), token('--cargo-high-fill'), token('--cargo-danger-line')] : [token('--cargo-danger'), token('--cargo-danger-fill'), token('--cargo-danger-line')];
  // SetHP: цвет числа и полосы по доле
  const hpTone = f => `rgb(${Math.round(Math.sqrt(Math.sqrt(1 - f)) * 255)},${Math.round(Math.sqrt(Math.sqrt(f)) * 255)},${Math.round(f * (1 - f) * 255)})`;

  function renderPlayer() {
    const f = Math.max(0, Math.min(1, state.hp / state.maxHp));
    // Точка — 5%: живой робот — хотя бы одна точка, раненый — никогда не все 20
    const on = f <= 0 ? 0 : f >= 1 ? 20 : Math.min(19, Math.max(1, Math.round(f * 20)));
    $('hpDots').style.setProperty('--c', hpTone(f));
    $('hpDots').innerHTML = Array.from({ length: 20 }, (_, i) => `<i${i < on ? ' class="on"' : ''}></i>`).join('');
    $('hpValue').firstElementChild.textContent = state.hp;
    $('hpValue').style.color = hpTone(f);
    $('money').innerHTML = cut(state.money);
    $('creds').innerHTML = cut(state.creds);
  }
  // Включает и выключает режим; обычные значения запоминаются и возвращаются как были
  window.kernHudRich = level => {
    if (level === richOn) return;
    if (!richOn) normal = { money: state.money, creds: state.creds, capacity: state.capacity, basket: state.basket };
    if (level) {
      const f = 1000 ** (level - 1);
      Object.assign(state, { money: RICH.money * f, creds: RICH.creds * f, capacity: RICH.capacity * f, basket: Object.fromEntries(Object.entries(RICH.basket).map(([k, v]) => [k, v * f])) });
    } else Object.assign(state, normal);
    richOn = level; renderPlayer(); renderCargo();
    window.dispatchEvent(new Event('kern-scene'));                // ширина левой колонки — по новым значениям
  };
  function renderCargo() {
    const values = CRYSTALS.map(([k]) => state.basket[k]);
    const percent = Math.round(Math.max(...values) * 100 / state.capacity);
    const [text, bar] = cargoTone(percent);
    $('cargoPercent').firstElementChild.textContent = percent;
    $('cargoPercent').style.color = text;
    // Ячейка — 5%; непустой груз — хотя бы одна ячейка, даже если процент округлился до 0. Перегруз — полная полоса цвета порога
    const lit = Math.min(20, Math.ceil(Math.max(...values) * 100 / state.capacity / 5));
    $('cargoCells').style.setProperty('--bar', bar);
    $('cargoCells').innerHTML = Array.from({ length: 20 }, (_, i) => `<i${i < lit ? ' class="on"' : ''}></i>`).join('');
    // Между иконкой и числом — доля лимита этого кристалла (лимит у каждого типа свой), до 100%; непустой — не уже 2 px
    $('crystals').innerHTML = CRYSTALS.filter(([k]) => state.basket[k] > 0)
      .map(([k, f]) => `<li style="color:var(--cry-${k})"><img src="img/crystals/${f}.png" alt=""><i class="share"><i style="width:max(2px, ${Math.min(100, state.basket[k] / state.capacity * 100).toFixed(1)}%)"></i></i><span>${cut(state.basket[k])}</span></li>`).join('');
    // Геология: ячейка — блок (до 20 ячеек, больше — доля на 20)
    const [gN, gMax] = state.geo, gCells = Math.min(gMax, 20), gLit = gN ? Math.max(1, Math.round(gN / gMax * gCells)) : 0;
    $('geoCells').style.gridTemplateColumns = `repeat(${gCells}, 1fr)`;
    $('geoCells').innerHTML = Array.from({ length: gCells }, (_, i) => `<i${i < gLit ? ' class="on"' : ''}></i>`).join('');
    $('geoCount').textContent = `${gN}/${gMax}`;
    $('cargo').hidden = values.every(v => v === 0);   // пустая корзина — панели нет
  }

  // ── Навыки: начальные слоты игрока (PlayerSkills), цвета SkillButton ──
  const skills = {
    d: { name: 'Копание', level: 14, exp: .2, color: token('--skill-dig') },
    m: { name: 'Добыча', level: 9, exp: .4, color: token('--skill-mining') },
    M: { name: 'Передвижение', level: 12, exp: 1, color: token('--skill-move') },
    l: { name: 'Защита', level: 12, exp: .6, color: token('--skill-defense') },
    A: { name: 'Строительство дорог', level: 8, exp: .3, color: token('--skill-road') },
    L: { name: 'Зелёное строительство', level: 15, exp: .7, color: token('--skill-green') },
    Y: { name: 'Жёлтое строительство', level: 6, exp: .1, color: token('--skill-yellow') },
    E: { name: 'Красное строительство', level: 4, exp: .5, color: token('--skill-red') },
    F: { name: 'Охлаждение', level: 7, exp: .8, color: token('--skill-cooling') },
    t: { name: 'Передвижение по дороге', level: 10, exp: .2, color: token('--skill-road-move') },
    p: { name: 'Упаковка', level: 5, exp: .4, color: token('--skill-pack') },
    b: { name: 'Упаковка синих кристаллов', level: 3, exp: .6, color: token('--skill-blue-pack') },
    c: { name: 'Упаковка голубых кристаллов', level: 2, exp: .9, color: token('--skill-cyan-pack') },
    r: { name: 'Упаковка красных кристаллов', level: 1, exp: .3, color: token('--skill-red-pack') },
  };
  window.kernSkills = skills;
  const SLOT_OF = { m: 0, d: 1, M: 2, l: 3 };
  const previewSkills = {
    ...skills,
    A: { name: 'Строительство дорог' }, L: { name: 'Зелёное строительство' },
    Y: { name: 'Жёлтое строительство' }, E: { name: 'Красное строительство' },
    F: { name: 'Охлаждение' }, t: { name: 'Передвижение по дороге' },
    p: { name: 'Упаковка' }, b: { name: 'Упаковка синих кристаллов' },
    c: { name: 'Упаковка голубых кристаллов' }, v: { name: 'Упаковка фиолетовых кристаллов' },
    r: { name: 'Упаковка красных кристаллов' }, w: { name: 'Упаковка белых кристаллов' },
  };
  let allMiniSkills = false;
  const timers = {};
  function renderMiniPreview() {
    const list = $('miniSkills');
    Object.keys(timers).forEach(code => clearTimeout(timers[code]));
    list.replaceChildren();
    // В игре одновременно растут не больше 6 мини-навыков
    if (allMiniSkills) Object.keys(previewSkills).slice(0, 6).forEach(code => showMini(code));
    else showMini('M');
  }
  function showMini(code) {
    const s = skills[code] || { ...previewSkills[code], exp: .55, color: token('--skill-preview') };
    let el = document.querySelector(`.mini-skill[data-code="${code}"]`);
    if (!el) {
      el = document.createElement('li');
      el.className = 'mini-skill';
      el.dataset.code = code;
      el.title = s.name;
      el.innerHTML = `<i class="ico sk sk-${code}"></i><span class="bar"><i></i></span><i class="up"></i>`;
      el.onclick = () => window.openSkills?.(SLOT_OF[code]);
      $('miniSkills').appendChild(el);
    }
    const ready = s.exp >= 1;
    el.style.setProperty('--c', s.color);
    el.classList.toggle('is-ready', ready);
    el.querySelector('.bar i').style.height = (ready ? 100 : s.exp * 100) + '%';
    clearTimeout(timers[code]);
    if (!ready && !allMiniSkills) timers[code] = setTimeout(() => el.remove(), 5000);
  }
  const grow = (code, step) => { skills[code].exp = Math.min(1, skills[code].exp + step); showMini(code); };

  // ── Копание: клик по клетке рядом с игроком ──
  function dig(x, y) {
    const t = window.kernWorld.dig(x, y);
    grow('d', .06);
    if (t.crystal) { state.basket[t.crystal] += 1 + Math.floor(Math.random() * 4); grow('m', .1); renderCargo(); }
    $('coords').textContent = `${1184 + x - window.kernWorld.PX}:${902 + y - window.kernWorld.PY}`;
  }
  $('world').addEventListener('click', e => {
    const r = $('world').getBoundingClientRect(), k = r.width / $('world').width;
    const c = window.kernWorld.cellAt((e.clientX - r.left) / k, (e.clientY - r.top) / k);
    if (c && c.type !== 'empty') dig(c.x, c.y);
  });
  let digTimer = null;
  function autodigTick() {
    const w = window.kernWorld;
    for (let d = 1; d < 12; d++) for (const [dx, dy] of [[d, 0], [-d, 0], [0, d], [0, -d]]) {
      const t = w.map[w.PY + dy]?.[w.PX + dx];
      if (t && t !== 'empty' && t !== 'road' && t !== 'support') { dig(w.PX + dx, w.PY + dy); return; }
    }
  }

  // ── Чат: 0 — три строки, 1 — колонка, 2 — скрыт (Tab по кругу, как в Legacy) ──
  const chat = {
    FED: [{ t: '18:42', nick: 'ШАХТЁР-102', c: '#ffd9a8', text: 'кто у респа?' },
      { t: '18:43', nick: 'ШАХТЁР-774', c: '#c8f0ff', text: 'я, иду вниз' },
      { t: '18:44', nick: 'DVM_LEADER', c: '#e6ccff', text: 'голубые на 1200:840' }],
    DNO: [{ t: '18:20', nick: 'ШАХТЁР-305', c: '#d8ffc8', text: 'кто на дне?' }],
  };
  // Личные сообщения: переписка с каждым игроком отдельно
  const pm = {
    'ШАХТЁР-102': { unread: 1, lines: [{ t: '17:58', nick: 'ШАХТЁР-102', c: '#ffd9a8', text: 'деньги закинул почтой' }, { t: '18:01', nick: 'ШАХТЁР-774', c: '#c8f0ff', text: 'спасибо, верну в субботу' }, { t: '18:40', nick: 'ШАХТЁР-102', c: '#ffd9a8', text: 'идёшь на сбор?' }] },
    'DVM_LEADER': { unread: 0, lines: [{ t: 'вчера', nick: 'DVM_LEADER', c: '#e6ccff', text: 'возьми синих на пушку' }] },
  };
  state.pm = null;
  // Непрочитанное по каналам считает клиент: сервер шлёт только список каналов и сообщения
  const CHANNELS = [['FED', 'Федеральный чат'], ['DNO', 'Дно']];
  const chanUnread = { FED: 0, DNO: 3 };
  chat.DNO.push({ t: '18:31', nick: 'ГЛУБИНА', c: '#ffe0a8', text: 'на 1100:1300 лава, не ходите' }, { t: '18:36', nick: 'ШАХТЁР-305', c: '#d8ffc8', text: 'спасибо' }, { t: '18:40', nick: 'ORE_KING', c: '#ffd0f0', text: 'продам синие, пишите в личку' });
  function renderChatList() {
    const last = l => l.filter(m => !m.sys).at(-1);
    const pmUnread = Object.values(pm).reduce((n, th) => n + th.unread, 0), pmLast = Object.values(pm).map(th => th.lines.at(-1)).filter(Boolean).at(0);
    const rows = [...CHANNELS.map(([tag, name]) => [tag, `${tag} – ${name}`, last(chat[tag]), chanUnread[tag]]), ['PM', 'Личные', pmLast, pmUnread]];
    $('chatList').innerHTML = rows.map(([tag, name, m, n]) => `<li><button type="button" data-chan="${tag}" class="${tag === state.channel ? 'is-on' : ''}"><b>${name}</b>${n ? `<i>${n}</i>` : ''}<span>${m ? `${esc(m.nick)}: ${esc(m.text)}` : 'Сообщений нет'}</span></button></li>`).join('');
  }
  const showChatList = on => { $('chatList').hidden = !on; $('chatListBtn').setAttribute('aria-expanded', String(on)); if (on) renderChatList(); };
  const nickLink = (nick, c) => `<b class="nick-link" data-nick="${esc(nick)}" style="color:${c}">${esc(nick)}</b>`;
  const esc = s => s.replace(/[&<>]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[ch]));
  function renderChat() {
    const line = m => m.sys ? `<li class="sys">${esc(m.sys)}</li>` : `<li><time>${m.t}</time>${nickLink(m.nick, m.c)}: ${esc(m.text)}</li>`;
    let lines;
    if (state.channel === 'PM' && !state.pm) {
      // Список переписок: последний ответ и непрочитанное
      lines = [];
      $('chatLog').innerHTML = Object.entries(pm).map(([nick, th]) => { const last = th.lines.at(-1);
        return `<li class="pm-thread${th.unread ? ' is-unread' : ''}"><button type="button" data-pm="${esc(nick)}"><b>${esc(nick)}</b><time>${last.t}</time><span>${esc(last.text)}</span>${th.unread ? `<i>${th.unread}</i>` : ''}</button></li>`; }).join('')
        || '<li class="sys">Личных сообщений пока нет. Нажмите на ник игрока, чтобы написать ему.</li>';
    } else {
      if (state.channel === 'PM') { const th = pm[state.pm]; th.unread = 0; lines = th.lines; }
      else lines = chat[state.channel];
      $('chatLog').innerHTML = (state.channel === 'PM' ? `<li class="pm-head"><button type="button" class="pm-back" title="К списку">‹</button>${nickLink(state.pm, '#c8f0ff')}</li>` : '') + lines.map(line).join('');
    }
    $('chatPeek3').innerHTML = lines.filter(m => !m.sys).slice(-3)
      .map(m => `<li>${nickLink(m.nick, m.c)}: ${esc(m.text)}</li>`).join('');
    $('chatPeekTag').textContent = state.channel === 'PM' ? 'ЛС' : state.channel;
    const unread = Object.values(pm).reduce((n, th) => n + th.unread, 0);
    $('pmBadge').textContent = unread || '';
    chanUnread[state.channel] = 0;
    document.querySelectorAll('[data-unread]').forEach(b => { b.textContent = chanUnread[b.dataset.unread] || ''; });
    $('chatListBadge').textContent = unread + chanUnread.FED + chanUnread.DNO || '';
    if (!$('chatList').hidden) renderChatList();
    $('chatInput').placeholder = state.channel === 'PM' ? (state.pm ? `Сообщение для ${state.pm}…` : 'Выберите переписку') : 'Напишите сообщение…';
    $('chatInput').disabled = state.channel === 'PM' && !state.pm;
    document.querySelectorAll('.chat-tab').forEach(b => b.classList.toggle('is-on', b.dataset.tag === state.channel));
  }
  function setChat(mode) {
    state.chat = mode;
    $('hud').dataset.chat = mode;
    press($('touchChat'), mode === 1);
    if (mode === 1) setTimeout(() => $('chatInput').focus(), 0); else $('chatInput').blur();
  }
  $('chatPeek').onclick = () => setChat(1);
  $('chatListBtn').onclick = () => showChatList($('chatList').hidden);
  $('chatList').onclick = e => { const b = e.target.closest('[data-chan]'); if (!b) return; state.channel = b.dataset.chan; if (state.channel === 'PM') state.pm = null; showChatList(false); renderChat(); };
  $('chatClose').onclick = () => setChat(0);
  document.querySelectorAll('.chat-tab').forEach(b => b.onclick = () => { state.channel = b.dataset.tag; if (b.dataset.tag === 'PM') state.pm = null; renderChat(); });
  $('chatLog').addEventListener('click', e => {
    const th = e.target.closest('[data-pm]'); if (th) { state.pm = th.dataset.pm; renderChat(); return; }
    if (e.target.closest('.pm-back')) { state.pm = null; renderChat(); }
  });
  // Ник в чате, превью чата и списке клана — открыть профиль игрока
  document.addEventListener('click', e => { const n = e.target.closest('.nick-link'); if (n && !n.closest('.pm-head')) { e.stopPropagation(); window.openProfile(n.dataset.nick); } }, true);
  window.openPM = nick => {
    pm[nick] ||= { unread: 0, lines: [] };
    state.channel = 'PM'; state.pm = nick; renderChat(); setChat(1);
  };
  $('chatForm').onsubmit = e => {
    e.preventDefault();
    const text = $('chatInput').value.trim();
    if (!text) return;
    const d = new Date();
    (state.channel === 'PM' ? pm[state.pm].lines : chat[state.channel]).push({ t: `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`, nick: 'ШАХТЁР-774', c: '#c8f0ff', text });
    $('chatInput').value = '';
    renderChat();
  };

  // ── Переключатели и кнопки ──
  const press = (el, on) => el.setAttribute('aria-pressed', String(on));
  $('autodig').onclick = () => {
    state.autodig = !state.autodig; press($('autodig'), state.autodig);
    clearInterval(digTimer); if (state.autodig) digTimer = setInterval(autodigTick, 450);
  };
  // ▶/■ в HUD и кнопка запуска в окне программатора — одно состояние
  function setProg(on, name) {
    state.prog = on;
    $('progRun').classList.toggle('is-running', on);
    $('progRun').title = on ? 'Остановить программу' : 'Запустить программу';
    // Меняется только значок: подсказка геймпада, вставленная в кнопку, остаётся на месте
    const use = $('progRun').querySelector('svg use');
    if (use) use.setAttribute('href', `#i-${on ? 'stop' : 'play'}`); else $('progRun').insertAdjacentHTML('afterbegin', `<svg><use href="#i-${on ? 'stop' : 'play'}"/></svg>`);
    // Запущенная программа подписывает кнопку своим именем вместо «Программатор» — одна строка, высота не меняется
    $('progName').textContent = on ? name : ''; $('progName').hidden = !on; $('progLabel').hidden = on;
    document.querySelectorAll('.online em').forEach(em => em.textContent = on ? '(7)' : '(6)');
  }
  window.kernProg = { sync: setProg };
  $('progRun').onclick = () => window.kernProgState(state.prog ? 'stopped' : 'running');
  $('respawnBtn').onclick = () => { $('respawnDialog').hidden = false; $('respawnOk').focus(); };
  $('respawnDialog').querySelector('[data-close]').onclick = () => { $('respawnDialog').hidden = true; };
  $('respawnOk').onclick = () => {
    for (const [k] of CRYSTALS) state.basket[k] = Math.floor(state.basket[k] * .9);
    renderCargo(); $('respawnDialog').hidden = true;
  };
  document.querySelector('.player .hp').onclick = () => { state.hp = state.hp > 40 ? state.hp - 30 : state.maxHp; renderPlayer(); };

  // ── Инвентарь: 4 видимых слота, остальное по раскрытию ──
  // Предметы и описания — DummyItemInfo клиента; количество — пример набора опытного игрока
  const items = [
    ['geopack', 12, 'Геопак', 'Предмет, который позволяет упаковать живой кристалл в инвентарь'],
    ['battery', 3, 'Аккумулятор', 'Компонент/Предмет используемый в крафтинге и при помощи которого можно увеличить скорость робота'],
    ['c190', 1, 'С-190', 'Компонент/Предмет используемый в крафтинге и при помощи которого можно наносить урон другим игрокам'],
    ['charge', 5, 'Заряд защиты', 'Предмет, увеличивающий здоровье строительных паков'],
    ['craft', 2, 'Крафт', 'Строительный пак, в котором можно создать паки и предметы'],
    ['teleport', 1, 'Телепорт', 'Строительный пак, который позволяет игрокам телепортироваться на другой телепорт'],
    ['resp', 2, 'Респаун', 'Строительный пак, который позволяет возрождаться и ремонтировать робота'],
    ['up', 1, 'UP', 'Строительный пак, который позволяет игрокам устанавливать и прокачивать умения'],
    ['market', 1, 'Маркет', 'Строительный пак, который позволяет покупать и продавать кристаллы, а также обмениваться друг с другом'],
    ['storage', 3, 'Склад', 'Строительный пак, в котором можно хранить кристаллы'],
    ['plasmBomb', 7, 'Плазменная бомба', 'Предмет, который позволяет взорвать блоки в радиусе 3 клеток(Красноскал с 1% шансом)'],
    ['protonBomb', 4, 'Протонная бомба', 'Предмет, который позволяет взорвать блоки 3х3 от центра(Красноскал с 100% шансом)'],
    ['razBomb', 9, 'Бомба-разряд', 'Предмет, который позволяет нанести урон игрокам (500 HP) и Строительным пакам(10 HP)'],
    ['rem', 6, 'Ремонтный бот', 'Предмет, который позволяет полностью восстановить здоровье робота'],
    ['nano', 24, 'Нано бот', 'Компонент/Предмет используемый в крафтинге и при помощи которого можно восстановить здоровье робота на 50 HP'],
    ['geoCyan', 2, 'Голубая жива', 'Живка, которая даёт плод голубыми кристаллами'],
    ['geoRed', 1, 'Красная жива', 'Живка, которая даёт плод красными кристаллами, если поблизости есть черноскал'],
    ['geoViolet', 1, 'Фиолетовая жива', 'Живка, которая даёт плод фиолетовыми кристаллами, если поблизости есть черноскал'],
    ['geoBlue', 3, 'Синяя жива', 'Живка, которая даёт плод синими кристаллами, если есть место для передвижения живки'],
    ['geoWhite', 1, 'Белая жива', 'Живка, которая даёт плод белыми кристаллами, если сверху стоит магма'],
    ['geoRainbow', 1, 'Радужная жива', 'Живка, которая даёт плод любым блоком, если с одной из сторон по горизонтали или вертикали не пусто'],
    ['aliveRadar', 2, 'Радар живок', 'Предмет, который позволяет обнаружить живые кристаллы в радиусе 200 блоков'],
    ['robotRadar', 1, 'Радар роботов', 'Предмет, который позволяет обнаружить роботов в радиусе 300 блоков'],
    ['vulkanRadar', 1, 'Радар вулканов', 'Предмет, который позволяет обнаружить вулканы'],
    ['portableTeleporter', 2, 'ТПР', 'Предмет, который позволяет игроку телепортироваться на Респаун без потери кристаллов'],
    ['poly', 40, 'Полимер', 'Компонент/Предмет используемый в крафтинге и при помощи которого можно строить полимерную дорогу'],
    ['compressor', 5, 'Компрессор', 'Компонент/Предмет используемый в крафтинге'],
    ['trans', 1, 'Транслятор', 'Компонент/Предмет используемый в крафтинге'],
    ['mineBooster', 1, 'Добыча x4', 'Предмет, который увеличивает добычу кристалла в 4 раза (12ч)'],
    ['upgradeBooster', 1, 'Прокачка x3', 'Предмет, который ускоряет прокачку в 3 раза (24ч)'],
    ['scanner', 2, 'Сканер паков', 'Предмет, при использовании которого показываются характеристики строительного пака'],
    ['disassembler', 1, 'Диззассемблер', 'Предмет, позволяющий собрать строительный пак в инвентарь'],
    ['eMI', 3, 'ЭМИ', 'Предмет, который запрещает игрокам в радиусе 20 блоков использовать инвентарь/копать'],
    ['geoBlackRock', 8, 'Чёрная скала', 'Предмет, который мгновенно ставит черноскал на пустоте'],
  ];
  window.kernItems = items;                                   // те же предметы кладутся в бокс (srvgui.js)
  $('invSlots').innerHTML = items.map(([f, n, name], i) => `<li title="${name}" data-i="${i}"><img src="img/items/${f}.png" alt=""><span>${n}</span></li>`).join('');
  $('invSlots').onclick = e => {
    const li = e.target.closest('li'); if (!li) return;
    const on = !li.classList.contains('is-on');
    document.querySelectorAll('.inv-slots li').forEach(x => x.classList.remove('is-on'));
    li.classList.toggle('is-on', on);
    $('itemHint').hidden = !on;
    if (on) { const p = window.kernPad?.active && window.kernPad; $('itemHint').textContent = `${li.title}: ${p ? p.label('useItem') : 'Enter'} — применить, ${p ? p.label('cancel') : 'Esc'} — отмена`; }
  };
  // На ПК раскрытие — как было; на пальце — отдельное окно со всеми предметами
  let invSelected = -1;
  function renderInvWindow() {
    $('invCount').textContent = `${items.length} предметов`;
    $('invGrid').innerHTML = items.map(([f, n, name], i) => `<li><button type="button" class="inv-cell${i === invSelected ? ' is-on' : ''}" data-i="${i}" title="${name}"><img src="img/items/${f}.png" alt=""><span>${n}</span></button></li>`).join('');
    const it = items[invSelected];
    $('invInfo').innerHTML = it
      ? `<img class="inv-window-big" src="img/items/${it[0]}.png" alt=""><h3>${it[2]}</h3><p class="inv-window-qty">× ${it[1]}</p><p class="inv-window-desc">${it[3]}</p>
         <button type="button" class="btn btn--primary" id="invUse">Использовать</button>`
      : '<p class="inv-window-empty">Выберите предмет</p>';
    if (it) $('invUse').onclick = () => { $('invDialog').hidden = true; document.querySelector(`.inv-slots li[data-i="${invSelected}"]`)?.click(); };
  }
  $('invGrid').onclick = e => { const b = e.target.closest('.inv-cell'); if (b) { invSelected = Number(b.dataset.i); renderInvWindow(); } };
  $('invDialog').querySelector('[data-close]').onclick = () => { $('invDialog').hidden = true; };
  window.openInventory = () => { invSelected = -1; renderInvWindow(); $('invDialog').hidden = false; };
  $('invExpand').onclick = () => {
    if ($('frame').matches('.compact, .in-touch')) { window.openInventory(); return; }   // пальцем — окно с крупными ячейками
    const open = $('inventory').classList.toggle('is-open');
    $('invExpand').setAttribute('aria-expanded', String(open));
  };

  // Штрихкод значения (ID игрока, хеш сборки): каждый знак — четыре бита, бит — узкий (1 px) или широкий (3 px) штрих,
  // по краям опорные штрихи. Декор строится из самих данных: другой ID — другой код
  window.kernBars = text => '<b class="w"></b><b></b>' + [...text.toLowerCase()].map(ch => {
    const v = parseInt(ch, 36) % 16;
    return [8, 4, 2, 1].map(bit => `<b${v & bit ? ' class="w"' : ''}></b>`).join('');
  }).join('') + '<b></b><b class="w"></b>';
  if ($('buildBars')) $('buildBars').innerHTML = window.kernBars($('buildHash').textContent);

  // ── Клавиши ──
  // Tab в игре — клавиша чата, а не обход кнопок: браузерный переход фокуса по кнопкам и окнам выключен везде,
  // кроме перехода между полями ввода (логин → пароль). Фокус-рамка остаётся у навигации геймпадом
  document.addEventListener('keydown', e => {
    if (e.key !== 'Tab') return;
    const fields = [...document.querySelectorAll('#frame input:not([type=checkbox]):not([type=range]), #frame textarea')].filter(f => f.offsetParent);
    if (fields.includes(e.target) && fields.length > 1) return;
    e.preventDefault();
  }, true);
  window.kernHud = { skillChanged: code => document.querySelector(`.mini-skill[data-code="${code}"]`)?.remove() };
  document.addEventListener('keydown', e => {
    if ($('frame').dataset.screen !== 'game' || !$('skillsDialog').hidden || !$('progDialog').hidden) return;
    const typing = e.target === $('chatInput');
    if (e.key === 'Tab') { e.preventDefault(); setChat((state.chat + 1) % 3); return; }
    if (e.key === 'Escape') {
      if (!$('invDialog').hidden) $('invDialog').hidden = true;
      else if (!$('respawnDialog').hidden) $('respawnDialog').hidden = true;
      else if (!$('menuDialog').hidden) $('menuDialog').hidden = true;
      else if (document.querySelector('.dialog-layer:not([hidden])')) return;   // своё окно закроет свой обработчик
      else if (state.chat === 1) setChat(0);
      else if (!$('itemHint').hidden) { document.querySelectorAll('.inv-slots li').forEach(x => x.classList.remove('is-on')); $('itemHint').hidden = true; }
      else if (!frame.classList.contains('compact')) $('menuDialog').hidden = false;
      return;
    }
    if (typing) return;
    if (e.key === 'Enter') { e.preventDefault(); setChat(1); }
    if (e.key === 'e' || e.key === 'E' || e.key === 'у' || e.key === 'У') $('autodig').click();
  });

  // ── Стенд: пропорция кадра, состояние данных, вписывание кадра в окно ──
  const frame = $('frame');
  function fit() {
    const st = $('stage'), pad = frame.classList.contains('bezel') ? 2 * 24 : 0;   // корпус устройства (тень 11px) и поле вокруг
    const k = Math.min(st.clientWidth / (frame.offsetWidth + pad), st.clientHeight / (frame.offsetHeight + pad));
    frame.style.transform = `translate(-50%, -50%) scale(${k})`;
    window.kernWorld?.draw();                                     // холст перерисовать под новый масштаб
  }
  document.querySelectorAll('[data-state]').forEach(b => b.onclick = () => {
    document.querySelectorAll('[data-state]').forEach(x => x.classList.toggle('is-on', x === b));
    const busy = b.dataset.state === 'busy';
    allMiniSkills = busy;
    renderMiniPreview();
    $('mission').hidden = !busy; $('geo').hidden = !busy; $('states').hidden = !busy;
    if (busy !== state.prog) $('progRun').click();
    const basket = busy ? { g: 2340, r: 96, b: 1204, v: 12, w: 3, c: 57 } : { g: 412, r: 96, b: 1204, v: 0, w: 0, c: 57 };
    if (richOn) normal.basket = basket; else state.basket = basket;                    // в режиме «много» груз не трогаем — выбор запоминается на выход
    renderCargo();
  });
  addEventListener('resize', fit);

  // Мониторы — кадр в логических пикселях: физическое разрешение / масштаб ОС.
  // Устройства — MobileDeviceMatrix: размер в dp и безопасная область (сверху, справа, снизу, слева)
  const DEVICES = {
    d768: { w: 1366, h: 768, px: '1366×768', scale: 1 },
    d800: { w: 1280, h: 800, px: '1280×800', scale: 1 },
    d1080: { w: 1920, h: 1080, px: '1920×1080', scale: 1 },
    d1440: { w: 2560, h: 1440, px: '2560×1440', scale: 1 },
    d219: { w: 2560, h: 1080, px: '2560×1080', scale: 1 },
    d4k150: { w: 2560, h: 1440, px: '3840×2160', scale: 1.5 },
    d4k200: { w: 1920, h: 1080, px: '3840×2160', scale: 2 },
    promax: { w: 973, h: 449, safe: [0, 62, 22, 62], form: 'phone', corner: 55 },
    pixel8: { w: 897, h: 404, safe: [0, 0, 0, 44], form: 'phone', corner: 40 },
    ipadmini: { w: 1112, h: 730, safe: [0, 0, 20, 0], form: 'tablet', corner: 22 },
    ipadpro: { w: 1668, h: 1251, safe: [0, 0, 24, 0], form: 'tablet', corner: 18 },
    foldin: { w: 924, h: 794, form: 'tablet', corner: 20 },
    // Экран-телевизор у десктопа (Big Picture): 4K при масштабе 400% — смотрят с дивана; поля от обрезки кадра телевизором
    tv: { w: 960, h: 540, px: '3840×2160', scale: 4, safe: [27, 48, 27, 48], input: 'xbox' },
  };
  // Ступень размера — только по вьюпорту в dp
  const sizeOf = (w, h) => w >= 2400 ? 'xl' : h < 500 ? 's' : h < 760 || w < 1100 ? 'm' : 'l';
  const sysOrig = {};
  let input = 'mouse', inputSticky = false, compact = false, lastMonitor = 'd1080', resText = '';
  // Итог стенда: вьюпорт, его размер и ввод — то, что решает вид
  const INPUT_NAME = { mouse: 'мышь', touch: 'тач', pad: 'геймпад' };
  const renderRes = () => { $('standRes').innerHTML = `${resText} · ввод <b>${INPUT_NAME[input]}</b>`; };
  function setDevice(name) {
    // Сцена стенда повторяет устройство — раскладка уже его; возвращается только ввод по умолчанию
    if (frame.dataset.device === name && DEVICES[name]) { const d0 = DEVICES[name]; if (!inputSticky && input !== (d0.input || (d0.form ? 'touch' : 'mouse'))) setInput(d0.input || (d0.form ? 'touch' : 'mouse'), true); return; }
    const d = DEVICES[name] || DEVICES.d1080, size = sizeOf(d.w, d.h);
    compact = size === 's' || size === 'm';

    // Десктоп — одно устройство с разными экранами; экраны видны, только когда выбран он
    const desktop = !d.form;
    if (desktop) lastMonitor = name;
    $('standDevice').value = desktop ? 'desktop' : name;
    if (desktop) $('standMonitor').value = name;
    $('standMonitors').hidden = !desktop;
    frame.dataset.device = name;
    // 1. Размер вьюпорта
    ['s', 'm', 'l', 'xl'].forEach(z => frame.classList.toggle('size-' + z, z === size));
    frame.classList.toggle('compact', compact);
    frame.classList.toggle('bezel', !!d.form);
    const tier = compact ? 'compact' : d.w < 1600 ? 'standard' : 'wide';
    ['compact', 'standard', 'wide'].forEach(t => frame.classList.toggle('tier-' + t, t === tier));
    ['--fw', '--fh', '--corner', '--st', '--sr', '--sb', '--sl'].forEach(v => frame.style.removeProperty(v));
    frame.style.setProperty('--fw', d.w + 'px'); frame.style.setProperty('--fh', d.h + 'px');
    const sizeName = { s: 'S', m: 'M', l: 'L', xl: 'XL' }[size];
    resText = (d.form ? `${d.w}×${d.h} dp` : d.scale === 1 ? `${d.w}×${d.h}` : `${d.px} при ${d.scale * 100}% → ${d.w}×${d.h}`) + ` · размер <b>${sizeName}</b>`;
    if (d.form) frame.style.setProperty('--corner', (d.corner || 0) + 'px');
    (d.safe || [0, 0, 0, 0]).forEach((v, i) => frame.style.setProperty(['--st', '--sr', '--sb', '--sl'][i], v + 'px'));
    // Компактная раскладка: у миникарты — бонусы, «Сказать», чат, ☰ и игровые функции; системное — в ☰; карта мира — миникартой.
    // Перенесённое помечено data-relocated: это ПК-элемент на новом месте, а не новый
    const touchNav = document.querySelector('.touch-sys'), pauseSys = $('pauseSys');
    sysOrig.list ||= [...document.querySelector('.sysbuttons').children];
    if (compact) {
      const game = sysOrig.list.filter(b => ['Почта', 'Консоль', 'Мои здания', 'Сбросить точку возрождения'].includes(b.dataset?.sys));
      touchNav.append(...game);
      pauseSys.append(...sysOrig.list.filter(b => b.tagName === 'BUTTON' && !game.includes(b)));
    } else {
      document.querySelector('.sysbuttons').append(...sysOrig.list);
    }
    // Кнопка «Меню»: в компактной раскладке паузы нет, в ☰ она — «Настройки»
    const sysMenu = $('sysMenu'), as = compact ? ['Настройки', '#i-settings'] : ['Меню', '#i-menu'];
    sysMenu.title = as[0]; sysMenu.querySelector('use').setAttribute('href', as[1]);   // перевод подхватит новую подпись сам
    const ml = sysMenu.querySelector('.control-label'); if (ml) ml.textContent = as[0];
    // Подпись под иконкой — короткая: кнопка шириной 72px, полное название остаётся в подсказке title
    const short = { 'Мои здания': 'Здания', 'Сбросить точку возрождения': 'Респаун' };
    sysOrig.list.forEach(b => { const l = b.querySelector?.('.control-label'), k = b.dataset?.sys; if (l && short[k]) l.textContent = short[k]; });
    [$('miniSkills'), ...sysOrig.list].forEach(el => el.toggleAttribute('data-relocated', compact));
    // ПК: миникарта — под рядом кнопок, миссия — под картой, инвентарь — в правый нижний угол, рост навыков — под строками состояния.
    // На пальце места прежние: телефонная раскладка задана своими правилами
    const mmBox = document.querySelector('.minimap'), inv = $('inventory'), skillsBox = $('miniSkills'), missionBox = $('mission');
    if (compact) {
      document.querySelector('.col-bottom').append(mmBox);
      document.querySelector('.player-row').append(missionBox);   // на пальце миссия — в полосе чата, место задано раскладкой
      $('itemHint').after(skillsBox);
    } else {
      document.querySelector('.sysbar').after(mmBox);
      mmBox.after(missionBox);                                    // ПК: миссия — под картой, на её ширине
      $('states').after(skillsBox);
    }
    $('hud').append(inv);                                       // место инвентаря задаёт раскладка: ПК — правый нижний угол, палец — над стиком
    frame.classList.toggle('hud-pc', !compact);
    const mm = $('minimap'), side = compact ? (size === 'm' ? 136 : 104) * 2 : 0;
    mm.width = compact ? side : 264; mm.height = compact ? side : 264;
    // 2. Управление: устройство даёт его по умолчанию, пока на стенде не выбрано своё
    if (!inputSticky) setInput(d.input || (d.form ? 'touch' : 'mouse'), true);
    else placeByInput();
    renderRes();
    fitColumn();
    fit();
  }
  // Что зависит от управления внутри раскладки: автокопание и программатор
  function placeByInput() {
    const prog = document.querySelector('.prog'), touch = input === 'touch';
    // Программатор — по размеру: в компактной раскладке над хотбаром, иначе в левой колонке
    if (compact) $('lowRow').append(prog);
    else document.querySelector('.col-bottom').insertBefore(prog, document.querySelector('.col-bottom .minimap'));
    // Автокопание — по управлению: под пальцем в дуге действий; без тача — рядом с программатором
    if (touch) $('touchBuild').after($('autodig'));
    else if (compact) $('lowRow').prepend($('autodig'));
    else document.querySelector('.col-bottom').insertBefore($('autodig'), prog);
    prog.toggleAttribute('data-relocated', compact);
    $('autodig').toggleAttribute('data-relocated', compact || touch);
    $('keyHint').style.visibility = input === 'mouse' && !compact ? '' : 'hidden';
  }
  // Ввод: mouse | touch | pad (семейство пада — у модуля геймпада)
  function setInput(kind, auto) {
    if (!auto) inputSticky = true;
    input = kind === 'xbox' || kind === 'ps' || kind === 'switch' ? 'pad' : kind;
    ['mouse', 'touch', 'pad'].forEach(k => frame.classList.toggle('in-' + k, k === input));
    $('standInput').value = input === 'pad' ? (kind === 'pad' ? window.kernPad?.family || 'xbox' : kind) : kind;
    placeByInput(); renderRes();
    frame.dispatchEvent(new CustomEvent('kern-input', { detail: { input, family: input === 'pad' && kind !== 'pad' ? kind : null } }));
  }
  // Проверки стенда переключают устройство на месте, без перезагрузки страницы (tools/*-check.html)
  window.kernStand = { device: setDevice };
  addEventListener('kern-scene', () => { fitColumn(); fit(); });
  window.kernInput = { set: setInput, get input() { return input; }, get compact() { return compact; }, get sticky() { return inputSticky; } };
  $('standInput').onchange = e => setInput(e.target.value);
  $('standDevice').onchange = e => setDevice(e.target.value === 'desktop' ? lastMonitor : e.target.value);
  $('standMonitor').onchange = e => setDevice(e.target.value);

  $('statusBar').append(document.querySelector('.diag'), $('assetLoad'));
  // Панели игрока и груза — одной ширины: колонка, а если строка с ником или «Груз N%» длиннее — по ней.
  // Строки не переносятся и не режутся; полоса правее колонки на телефоне сдвигается следом
  const heads = [document.querySelector('.player-head'), document.querySelector('.wallet'), document.querySelector('.cargo-head')];
  function fitColumn() {
    const need = heads.map(row => {
      const cs = getComputedStyle(row), kids = [...row.children].filter(c => c.offsetParent);
      const box = getComputedStyle(row.parentElement);
      // Кошелёк — две равные половины ряда: нужной ширине — две самые широкие суммы, а не сумма ширин (половины тянутся на всю половину)
      const wide = row.classList.contains('wallet'), sc = frame.getBoundingClientRect().width / frame.offsetWidth || 1;
      const widths = kids.map(c => (wide ? c.querySelector('dd') : c).getBoundingClientRect().width / sc);
      return (wide ? Math.max(...widths) * kids.length : widths.reduce((w, x) => w + x, 0))
        + parseFloat(cs.columnGap || 0) * (kids.length - 1) + parseFloat(box.paddingLeft) + parseFloat(box.paddingRight) + 2;
    });
    const col = parseFloat(getComputedStyle(frame).getPropertyValue('--col')) || 0;
    frame.style.setProperty('--pcol', Math.ceil(Math.max(col, ...need)) + 'px');
  }
  const fitObs = new ResizeObserver(fitColumn);
  heads.forEach(row => [...row.children].forEach(c => fitObs.observe(c)));
  document.querySelectorAll('.wallet dd').forEach(d => fitObs.observe(d));        // половины кошелька не меняют ширину от суммы — следим за самой суммой

  // Подписи остаются видимыми и на сенсорном экране, где нет наведения.
  document.querySelectorAll('.sysbuttons button, .menu-rail button').forEach(button => {
    const label = document.createElement('span');
    label.className = 'control-label';
    label.textContent = { 'Мои здания': 'Здания', 'Сбросить точку возрождения': 'Респаун', 'Выход из игры': 'Выход' }[button.title] ?? button.title;
    button.append(label);
  });
  // ТОЛЬКО СТЕНД, НЕ ИГРОВАЯ ФУНКЦИЯ: клик по плашке усиления листает набор иконок i-fx-*, чтобы просмотреть все глифы на месте.
  // В игре иконку усилению задаёт сервер (по имени усиления), игрок её не переключает.
  const FX_ICONS = ['ore', 'yield', 'bit', 'xp', 'core', 'skill', 'time', 'signal', 'surge'];
  $('states').querySelectorAll('li').forEach(li => {
    const use = li.querySelector('use');
    const show = () => { li.title = `i-fx-${FX_ICONS[+li.dataset.fx]} — только стенд: клик листает набор иконок`; use.setAttribute('href', `#i-fx-${FX_ICONS[+li.dataset.fx]}`); };
    li.dataset.fx = Math.max(0, FX_ICONS.indexOf(use.getAttribute('href').replace('#i-fx-', '')));
    li.style.cursor = 'pointer';
    li.addEventListener('click', () => { li.dataset.fx = (+li.dataset.fx + 1) % FX_ICONS.length; show(); });
    show();
  });
  // Ресурсы догружаются фоном после входа: «N / M», тонкая полоса; по завершении строка пропадает
  let assetTimer = 0;
  window.kernAssets = (total = 148, freezeAt = 0) => {
    clearInterval(assetTimer);
    let done = 0;
    $('assetLoad').style.setProperty('--al-digits', String(total).length);
    const tick = () => {
      $('assetLoad').hidden = done >= total;
      $('assetLoadText').textContent = `${done}/${total}`;
      $('assetLoadFill').style.width = (done * 100 / total) + '%';
      if (freezeAt && done >= freezeAt) { clearInterval(assetTimer); return; }
      if (done >= total) clearInterval(assetTimer);
      done = Math.min(total, done + 3);
    };
    tick(); assetTimer = setInterval(tick, 120);
  };

  // ── Стик (сенсорный и левый у геймпада): небольшое отклонение — повернуть робота, сильное — идти ──
  const TURN = .25, MOVE = .7;            // доли полного хода стика
  let stickDir = '', stickTimer = 0;
  const stopWalk = () => { clearInterval(stickTimer); stickTimer = 0; };
  window.kernStick = (nx, ny) => {
    const m = Math.min(1, Math.hypot(nx, ny));
    if (m < TURN) { stopWalk(); stickDir = ''; return; }
    stickDir = Math.abs(nx) > Math.abs(ny) ? (nx > 0 ? 'right' : 'left') : (ny > 0 ? 'down' : 'up');
    if (m < MOVE) { stopWalk(); window.kernWorld.face(stickDir); return; }
    if (!stickTimer) { window.kernWorld.step(stickDir); stickTimer = setInterval(() => window.kernWorld.step(stickDir), 160); }
  };

  // ── Сенсорное управление (PlayerHUD MobileControls) ──
  const pad = $('pad'), knob = $('padKnob');
  pad.onpointerdown = e => {
    pad.setPointerCapture(e.pointerId);
    const r = pad.getBoundingClientRect(), k = pad.offsetWidth / r.width, max = pad.offsetWidth / 2 - 27;
    const move = ev => {
      let x = (ev.clientX - r.left - r.width / 2) * k, y = (ev.clientY - r.top - r.height / 2) * k;
      const len = Math.hypot(x, y); if (len > max) { x *= max / len; y *= max / len; }
      knob.style.transform = `translate(${x}px, ${y}px)`;
      window.kernStick(x / max, y / max);
    };
    move(e);
    pad.onpointermove = move;
    pad.onpointerup = pad.onpointercancel = () => { pad.onpointermove = null; knob.style.transform = ''; window.kernStick(0, 0); };
  };
  const toggleBtn = b => press(b, b.getAttribute('aria-pressed') !== 'true');
  $('fastMove').onclick = () => toggleBtn($('fastMove'));
  // «Строить»: выключено — открывает веер типов; выбранный тип включает установку и встаёт на кнопку
  const builds = [...document.querySelectorAll('.build-fan .build')];
  const setBuild = b => {
    builds.forEach(x => press(x, x === b));
    press($('touchBuild'), !!b);
    if (b) $('touchBuildImg').src = b.querySelector('img').src;
    $('buildFan').hidden = true;
  };
  builds.forEach(b => b.onclick = () => setBuild(b.getAttribute('aria-pressed') === 'true' ? null : b));
  $('touchBuild').onclick = () => {
    if ($('touchBuild').getAttribute('aria-pressed') === 'true') setBuild(null);
    else $('buildFan').hidden = !$('buildFan').hidden;
  };
  function setSay(on) {
    $('sayForm').hidden = !on; press($('touchSay'), on);
    if (on) { setChat(0); setTimeout(() => $('sayInput').focus(), 0); }
  }
  $('touchChat').onclick = () => { setSay(false); setChat(state.chat === 1 ? 0 : 1); };
  $('touchSay').onclick = () => setSay($('sayForm').hidden);
  $('sayForm').onsubmit = e => { e.preventDefault(); $('sayInput').value = ''; setSay(false); };
  // ── Клан — по знаку клана ──
  const MEMBERS = [['DVM_LEADER', 'Глава', 92, 1], ['ШАХТЁР-102', 'Заместитель', 61, 1], ['ШАХТЁР-774', 'Участник', 47, 1], ['ГЛУБИНА', 'Участник', 55, 1],
    ['ROCKBOT', 'Участник', 38, 1], ['KOPATEL_9', 'Участник', 44, 1], ['ЖЕЛЕЗО', 'Участник', 29, 1], ['SANDMAN', 'Участник', 51, 0], ['МИНЁР', 'Новичок', 12, 0],
    ['BLUE_HUNTER', 'Участник', 33, 0], ['ДРОН-7', 'Участник', 40, 0], ['ORE_KING', 'Участник', 66, 0]];
  const BANK = [['+ $120 000', 'ШАХТЁР-102 · взнос', 'сегодня 18:40'], ['− $45 000', 'Постройка: Рынок', 'сегодня 14:02'], ['+ $80 500', 'Налог с добычи', 'вчера'], ['+ ₢ 2 000', 'DVM_LEADER · взнос', 'вчера'], ['− $210 000', 'Постройка: Пушка', '24.10']];
  $('clanMembers').innerHTML = MEMBERS.map(([n, r, l, on]) => `<li class="${on ? 'is-online' : ''}"><i></i>${nickLink(n, 'var(--bone)')}<em class="${r === 'Участник' || r === 'Новичок' ? '' : 'role'}">${r}</em><em>ур.${l}</em></li>`).join('');
  $('clanBank').innerHTML = BANK.map(([v, w, t]) => `<li><i></i><b>${w}</b><em class="${v[0] === '+' ? 'plus' : 'minus'}">${v}</em><em>${t}</em></li>`).join('');
  // Объявления: пишут глава и заместители, читают все
  const BOARD = [
    { who: 'DVM_LEADER', role: 'Глава', time: 'вчера 21:10', pin: true, text: 'Сбор в субботу в 20:00 у Рынка на 1200:840. Строим пушку — каждому принести по 500 синих.' },
    { who: 'ШАХТЁР-102', role: 'Заместитель', time: '24.10', text: 'Налог с добычи с понедельника 5%. Деньги идут на ремонт клановых зданий.' },
    { who: 'DVM_LEADER', role: 'Глава', time: '20.10', text: 'Не копайте под нашими опорами на 1180:860 — там клановая база.' },
  ];
  const REQUESTS = [['ГЕОЛОГ_42', 34, 'Копаю каждый день, есть свои дороги'], ['NEWBIE_MINER', 9, ''], ['STONEFACE', 51, 'Ушёл из клана DNO, ищу активный клан']];
  let clanRole = 'member';
  function renderClanBoard() {
    $('clanBoard').innerHTML = BOARD.map(p => `<li class="${p.pin ? 'is-pin' : ''}"><header>${nickLink(p.who, 'var(--bone)')}<em class="role">${p.role}</em><time>${p.time}</time>${p.pin ? '<span class="pin">Закреплено</span>' : ''}</header><p>${esc(p.text)}</p></li>`).join('');
  }
  function renderRequests() {
    $('clanRequests').innerHTML = REQUESTS.map(([n, l, msg], i) => `<li class="req"><i></i>${nickLink(n, 'var(--bone)')}<em>ур.${l}</em><span class="req-act"><button type="button" class="btn" data-accept="${i}">Принять</button><button type="button" class="btn" data-decline="${i}">Отклонить</button></span>${msg ? `<small>${esc(msg)}</small>` : ''}</li>`).join('')
      || '<li class="clan-empty">Новых заявок нет</li>';
    $('reqBadge').textContent = REQUESTS.length || '';
  }
  $('clanRequests').onclick = e => {
    const a = e.target.closest('[data-accept]'), d = e.target.closest('[data-decline]');
    if (!a && !d) return;
    const [n] = REQUESTS.splice(+(a || d).dataset[a ? 'accept' : 'decline'], 1);
    $('clanInviteNote').textContent = a ? `${n} принят в клан` : `Заявка ${n} отклонена`;
    renderRequests();
  };
  $('clanInviteForm').onsubmit = e => {
    e.preventDefault();
    const id = $('clanInviteId').value.replace(/\D/g, '');
    $('clanInviteNote').textContent = id ? `Приглашение отправлено игроку #${id}` : 'Введите ID игрока';
    $('clanInviteId').value = '';
  };
  $('clanPostForm').onsubmit = e => {
    e.preventDefault();
    const text = $('clanPostText').value.trim(); if (!text) return;
    BOARD.unshift({ who: 'ШАХТЁР-774', role: 'Глава', time: 'сейчас', text });
    $('clanPostText').value = ''; renderClanBoard();
  };
  function setClanTab(tab) {
    $('clanTabs').querySelectorAll('button').forEach(x => x.setAttribute('aria-checked', String(x.dataset.tab === tab)));
    $('clanDialog').querySelectorAll('[data-page]').forEach(p => { p.hidden = p.dataset.page !== tab; });
  }
  $('clanTabs').onclick = e => { const b = e.target.closest('button'); if (b) setClanTab(b.dataset.tab); };
  // Роль игрока в клане: заявки и объявления пишет только глава (и заместители)
  function setClanRole(role) {
    clanRole = role;
    $('clanDialog').classList.toggle('is-lead', role === 'leader');
    document.querySelectorAll('#standClanRole button').forEach(b => b.classList.toggle('is-on', b.dataset.role === role));
    if (role !== 'leader' && $('clanTabs').querySelector('[data-tab="requests"]').getAttribute('aria-checked') === 'true') setClanTab('members');
  }
  document.querySelectorAll('#standClanRole button').forEach(b => b.onclick = () => setClanRole(b.dataset.role));
  renderClanBoard(); renderRequests();
  $('clanBtn').onclick = () => { $('clanDialog').hidden = false; };
  $('clanDialog').querySelector('[data-close]').onclick = () => { $('clanDialog').hidden = true; };

  // ── Почта: письма сервера и игроков, подарки забираются кнопкой ──
  const MAIL = [
    { from: 'Сервер', title: 'Награда за неделю', time: 'сегодня 09:00', unread: true, text: 'Спасибо, что играете! За активность на этой неделе вам начислена награда.', gift: ['img/items/upgradeBooster.png', 'Прокачка x3 · 1 шт.'] },
    { from: 'DVM_LEADER', title: 'Сбор у Рынка', time: 'вчера 21:14', unread: true, text: 'В субботу в 20:00 собираемся у клановского Рынка на 1200:840. Берите синие кристаллы — будем строить пушку.' },
    { from: 'Сервер', title: 'Обновление 0.9.0', time: '24.10', text: 'Вышло обновление 0.9.0: поезда на рельсах и переработанные клановые войны. Подробности — в Discord.' },
    { from: 'ШАХТЁР-102', title: 'Перевод', time: '22.10', text: 'Держи на ремонт, вернёшь когда сможешь.', gift: ['img/crystals/green.png', '$25 000'], taken: true },
  ];
  let mailSel = 0;
  function renderMail() {
    $('mailList').innerHTML = MAIL.map((m, i) => `<li><button type="button" class="${m.unread ? 'is-unread' : ''}${i === mailSel ? ' is-on' : ''}" data-i="${i}"><i></i><b>${m.title}</b><time>${m.time}</time><span>${m.from}</span></button></li>`).join('');
    const m = MAIL[mailSel];
    $('mailRead').innerHTML = `<h3>${m.title}</h3><p class="from">${m.from} · ${m.time}</p><p>${m.text}</p>`
      + (m.gift ? `<div class="mail-gift"><img src="${m.gift[0]}" alt=""><span>${m.gift[1]}</span><button type="button" class="btn btn--primary" id="mailTake"${m.taken ? ' disabled' : ''}>${m.taken ? 'Получено' : 'Забрать'}</button></div>` : '');
    if ($('mailTake')) $('mailTake').onclick = () => { m.taken = true; renderMail(); };
    const n = MAIL.filter(x => x.unread).length;
    $('mailBadge').textContent = n || '';
  }
  $('mailList').onclick = e => { const b = e.target.closest('button'); if (!b) return; mailSel = +b.dataset.i; MAIL[mailSel].unread = false; renderMail(); };
  window.openMail = () => { MAIL[mailSel].unread = false; renderMail(); $('mailDialog').hidden = false; };
  $('mailBtn').addEventListener('click', () => window.openMail());
  $('mailDialog').querySelector('[data-close]').onclick = () => { $('mailDialog').hidden = true; };
  renderMail();
  document.addEventListener('keydown', e => { if (e.key === 'Escape') { $('clanDialog').hidden = true; $('mailDialog').hidden = true; } });

  // Профиль — по ID игрока у знака клана
  $('profileBtn').onclick = () => window.openProfile();
  $('profileDialog').querySelector('[data-close]').onclick = () => { $('profileDialog').hidden = true; };
  document.addEventListener('keydown', e => { if (e.key === 'Escape') $('profileDialog').hidden = true; });
  // Мои здания — пустое окно сервера
  document.querySelector('[data-sys="Мои здания"]').addEventListener('click', () => { $('buildingsDialog').hidden = false; });
  $('buildingsDialog').querySelector('[data-close]').onclick = () => { $('buildingsDialog').hidden = true; };
  document.addEventListener('keydown', e => { if (e.key === 'Escape') $('buildingsDialog').hidden = true; });
  $('touchMenu').onclick = () => { $('pauseDialog').hidden = false; };
  // «Меню» на ПК открывает паузу; на пальце паузы нет — та же кнопка в ☰ открывает настройки
  $('sysMenu').onclick = () => frame.classList.contains('compact') ? window.openSettings() : ($('menuDialog').hidden = false);
  $('pauseSys').addEventListener('click', e => { if (e.target.closest('button')) $('pauseDialog').hidden = true; });
  $('pauseDialog').querySelector('[data-close]').onclick = () => { $('pauseDialog').hidden = true; };
  $('touchDig').onclick = () => autodigTick();
  $('touchHeal').onclick = () => { state.hp = state.maxHp; renderPlayer(); };
  $('touchGeo').onclick = () => document.querySelector('.inv-slots li[data-i="0"]').click();   // геопак — первый предмет

  // Параметры адреса для снимков: ?aspect=1.6&state=busy&chat=1
  // Сцена стенда (js/scene.js): обработчик HUD — после окон мира, как раньше по времени. Сброс — исходное состояние стенда
  window.kernScene.onReset(() => {
    inputSticky = false;
    if (state.chat !== 0) setChat(0);
    $('buildFan').hidden = true;
    const calm = document.querySelector('[data-state="calm"]'); if (calm && !calm.classList.contains('is-on')) calm.click();
    if (clanRole !== 'member') setClanRole('member');
    if ($('clanTabs').querySelector('[data-tab="members"]').getAttribute('aria-checked') !== 'true') setClanTab('members');
    if (!$('chatList').hidden) showChatList(false);
    const fed = document.querySelector('.chat-tab[data-tag="FED"]'); if (fed && !fed.classList.contains('is-on')) fed.click();
    if ($('inventory').classList.contains('is-open')) $('invExpand').click();
    document.querySelectorAll('.inv-cell.is-on').forEach(c => c.classList.remove('is-on'));
  });
  window.kernScene.on(2, q => {
    setDevice(q.get('device') || 'd1080');
    if (q.get('input')) setInput(q.get('input'));
    renderPlayer(); renderCargo(); renderChat(); showMini('M');
    if (q.get('state')) document.querySelector(`[data-state="${q.get('state')}"]`)?.click();
    if (q.get('chat')) setChat(Number(q.get('chat')));
    if (q.get('build')) $('buildFan').hidden = false;
    if (q.get('pause')) $('pauseDialog').hidden = false;
    if (q.get('profile')) window.openProfile(q.get('profile') === '1' || q.get('profile') === 'settings' ? undefined : q.get('profile'), q.get('profile') === 'settings' ? 'settings' : '');
    if (q.get('pm')) window.openPM(q.get('pm'));
    if (q.get('clanrole')) setClanRole(q.get('clanrole'));
    if (q.get('clan')) { $('clanBtn').click(); if (q.get('clan') !== '1') setClanTab(q.get('clan')); }
    if (q.get('mail')) window.openMail();
    if (q.get('menu')) $('menuDialog').hidden = false;
    if (q.get('buildings')) $('buildingsDialog').hidden = false;
    if (q.get('assets')) window.kernAssets(148, Number(q.get('assets')));
    if (q.get('inv')) { $('invExpand').click(); if (q.get('inv') !== '1') document.querySelector(`.inv-cell[data-i="${q.get('inv')}"]`)?.click(); }
  });
})();
