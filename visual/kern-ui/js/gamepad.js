// Геймпад на всех платформах: значки кнопок по семейству пада, действия в игре,
// перемещение фокуса по экранам и окнам, переназначение кнопок в настройках.
// Режим включается первым нажатием на пад (или стендом: ?pad=xbox|ps|switch); касание экрана,
// клик или клавиша возвращают обычное управление.
(() => {
  const $ = id => document.getElementById(id);
  const frame = $('frame');

  // Кнопки по стандартной раскладке Gamepad API; подписи — по семейству
  const BUTTONS = ['a', 'b', 'x', 'y', 'lb', 'rb', 'lt', 'rt', 'select', 'start', 'ls', 'rs', 'up', 'down', 'left', 'right'];
  const DPAD = { up: '↑', down: '↓', left: '←', right: '→' };
  const GLYPHS = {
    xbox: { a: 'A', b: 'B', x: 'X', y: 'Y', lb: 'LB', rb: 'RB', lt: 'LT', rt: 'RT', ls: 'LS', rs: 'RS', select: '⧉', start: '☰', ...DPAD },
    ps: { a: '✕', b: '○', x: '□', y: '△', lb: 'L1', rb: 'R1', lt: 'L2', rt: 'R2', ls: 'L3', rs: 'R3', select: '⧉', start: '☰', ...DPAD },
    switch: { a: 'B', b: 'A', x: 'Y', y: 'X', lb: 'L', rb: 'R', lt: 'ZL', rt: 'ZR', ls: 'LS', rs: 'RS', select: '−', start: '+', ...DPAD },
  };

  // Действия в игре и кнопки по умолчанию; порядок — порядок строк в настройках
  const ACTIONS = [
    ['dig', 'Копать', 'rt'], ['heal', 'Лечение', 'lt'], ['build', 'Поставить блок', 'a'], ['geo', 'Геология', 'y'],
    ['autodig', 'Автокопание', 'x'], ['slotPrev', 'Предмет левее', 'lb'], ['slotNext', 'Предмет правее', 'rb'],
    ['useItem', 'Применить предмет', 'up'], ['prog', 'Программа', 'down'], ['blockPrev', 'Тип блока левее', 'left'], ['blockNext', 'Тип блока правее', 'right'],
    ['run', 'Бег', 'ls'], ['map', 'Карта мира', 'rs'], ['hud', 'Кнопки интерфейса', 'select'], ['menu', 'Меню', 'start'], ['cancel', 'Отмена', 'b'],
  ];
  const DEFAULTS = Object.fromEntries(ACTIONS.map(([act, , btn]) => [act, btn]));
  const bind = { ...DEFAULTS };
  const LABEL = Object.fromEntries(ACTIONS.map(([act, name]) => [act, name]));

  let family = 'xbox', forced = '', active = false, hudMode = false;

  // ── Значки ──
  const glyphOf = btn => (GLYPHS[family] || GLYPHS.xbox)[btn] || btn;
  const glyph = btn => `<span class="pg" data-btn="${btn}">${glyphOf(btn)}</span>`;
  function renderGlyphs() {
    frame.dataset.pad = family;
    document.querySelectorAll('.pg[data-act]').forEach(el => { el.dataset.btn = bind[el.dataset.act]; });
    document.querySelectorAll('.pg[data-btn]').forEach(el => { el.textContent = glyphOf(el.dataset.btn); });
    renderCluster(); renderBar();
  }

  // ── Режим пада ──
  function setActive(on) {
    if (active === on) return;
    active = on;   // класс кадра in-pad ставит выбор ввода (kernInput)
    // Веер типов блоков живёт в сенсорном доке; с падом он нужен и на ПК — переносится в HUD
    const fan = $('buildFan');
    if (on) $('hud').append(fan); else document.querySelector('.dock').prepend(fan);
    if (!on) setHudMode(false);
    renderGlyphs();
    lastScope = null; if (on) autoFocus();
  }
  function setFamily(f) { family = forced || f || 'xbox'; renderGlyphs(); }
  const familyOf = id => /054c|playstation|dualsense|dualshock|wireless controller/i.test(id) ? 'ps'
    : /057e|nintendo|pro controller|joy-con/i.test(id) ? 'switch' : 'xbox';

  // Обычный ввод возвращает обычное управление
  ['pointerdown', 'keydown', 'wheel'].forEach(t => frame.addEventListener(t, e => {
    if (e.isTrusted && active && !window.kernInput.sticky) window.kernInput.set(e.pointerType === 'touch' ? 'touch' : 'mouse', true);
  }, true));
  // Ввод выбирает kernInput (стенд, устройство, первое нажатие на пад); модуль только включается и выключается
  frame.addEventListener('kern-input', e => { if (e.detail.family) family = forced || e.detail.family; setActive(e.detail.input === 'pad'); renderGlyphs(); });

  // ── Где сейчас ходит фокус ──
  const isOpen = el => el && !el.hidden && el.getClientRects().length;
  function scope() {
    const layers = [...frame.querySelectorAll('.calib, .settings-layer, .dialog-layer')].filter(isOpen);
    if (layers.length) return layers.sort((a, b) => (+getComputedStyle(b).zIndex || 0) - (+getComputedStyle(a).zIndex || 0))[0];
    if (isOpen($('buildFan')) && frame.dataset.screen === 'game') return $('buildFan');
    const s = frame.dataset.screen;
    if (s === 'game') return hudMode ? $('hud') : null;
    return frame.querySelector(`.screen[data-screen="${s === 'descent' ? 'menu' : s}"]`);
  }
  const FOCUSABLE = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
  const focusables = root => [...root.querySelectorAll(FOCUSABLE)].filter(el =>
    !el.disabled && el.getClientRects().length && !el.closest('[inert], [hidden], .is-off') && getComputedStyle(el).visibility !== 'hidden');

  function move(dir) {
    const sc = scope(); if (!sc) return;
    const list = focusables(sc), cur = document.activeElement;
    if (!list.includes(cur)) { list[0]?.focus(); return; }
    // Ползунок и список меняют значение влево-вправо
    if ((dir === 'left' || dir === 'right') && cur.matches('input[type="range"]')) {
      dir === 'left' ? cur.stepDown() : cur.stepUp(); cur.dispatchEvent(new Event('input', { bubbles: true })); return;
    }
    if ((dir === 'left' || dir === 'right') && cur.matches('select')) {
      cur.selectedIndex = Math.max(0, Math.min(cur.options.length - 1, cur.selectedIndex + (dir === 'left' ? -1 : 1)));
      cur.dispatchEvent(new Event('change', { bubbles: true })); return;
    }
    const r = cur.getBoundingClientRect(), cx = r.left + r.width / 2, cy = r.top + r.height / 2;
    const [ax, ay] = { up: [0, -1], down: [0, 1], left: [-1, 0], right: [1, 0] }[dir];
    let best = null, score = Infinity;
    for (const el of list) {
      if (el === cur) continue;
      const q = el.getBoundingClientRect(), dx = q.left + q.width / 2 - cx, dy = q.top + q.height / 2 - cy;
      const along = dx * ax + dy * ay, across = Math.abs(dx * ay - dy * ax);
      if (along <= 1) continue;
      const s = along + across * 2;
      if (s < score) { score = s; best = el; }
    }
    best?.focus();
    best?.scrollIntoView?.({ block: 'nearest' });
  }

  // Вкладки области: LB / RB
  function tab(step) {
    const sc = scope(); if (!sc) return;
    if (sc.matches('[data-screen="onboarding"]')) { (step < 0 ? $('onbPrev') : $('onbNext')).click(); return; }
    const tabs = [...sc.querySelectorAll('[role="tab"], #authTabs [role="radio"]')].filter(t => t.getClientRects().length);
    if (!tabs.length) return;
    const i = tabs.findIndex(t => t.classList.contains('is-on') || t.getAttribute('aria-selected') === 'true' || t.getAttribute('aria-checked') === 'true');
    tabs[(Math.max(0, i) + step + tabs.length) % tabs.length].click();
  }

  // Назад: закрыть верхнее окно, выйти из кнопок интерфейса, шаг назад
  function back() {
    const sc = scope();
    if (!sc) return false;
    if (sc === $('buildFan')) { sc.hidden = true; return true; }
    if (sc === $('hud')) { setHudMode(false); return true; }
    const close = sc.querySelector('#calibCancel, [data-close]');
    if (close && isOpen(close)) { close.click(); return true; }
    if (sc.matches('[data-screen="onboarding"]') && !$('onbPrev').disabled) { $('onbPrev').click(); return true; }
    return false;
  }

  function setHudMode(on) {
    hudMode = on; frame.classList.toggle('pad-hud', on);
    if (on) focusables($('hud'))[0]?.focus(); else if ($('hud').contains(document.activeElement)) document.activeElement.blur();
    renderBar();
  }

  // ── Действия в игре ──
  const builds = () => [...document.querySelectorAll('.build-fan .build')];
  let block = 2;   // тип блока для «Поставить блок»: Военный, Дорога, Блок, Опора
  function slot(step) {
    const li = [...document.querySelectorAll('.inv-slots li')];
    if (!li.length) return;
    const i = li.findIndex(x => x.classList.contains('is-on'));
    li[(i < 0 ? (step > 0 ? 0 : li.length - 1) : (i + step + li.length) % li.length)].click();
  }
  const GAME = {
    dig: () => $('touchDig').click(),
    autodig: () => $('autodig').click(),
    build: () => builds()[block]?.click(),
    blockPrev: () => { block = (block + 3) % 4; renderCluster(); },
    blockNext: () => { block = (block + 1) % 4; renderCluster(); },
    heal: () => $('touchHeal').click(),
    geo: () => $('touchGeo').click(),
    slotPrev: () => slot(-1), slotNext: () => slot(1),
    useItem: () => { const li = document.querySelector('.inv-slots li.is-on'); if (li) li.click(); },
    prog: () => $('progRun').click(),
    run: () => $('fastMove').click(),
    map: () => document.querySelector('.minimap').click(),
    hud: () => setHudMode(true),
    menu: () => (frame.classList.contains('compact') ? $('touchMenu') : $('sysMenu')).click(),
    cancel: () => { const li = document.querySelector('.inv-slots li.is-on'); if (li) li.click(); },
  };

  // ── Нажатие кнопки пада ──
  let capture = null;   // переназначение: ждём следующую кнопку
  function press(btn) {
    if (capture) { capture(btn); return; }
    const sc = scope();
    if (sc) {
      // Меню, окна и кнопки интерфейса
      if (DPAD[btn]) return move(btn);
      if (btn === 'a') { const el = document.activeElement; if (sc.contains(el) && el !== document.body) el.click(); else focusables(sc)[0]?.focus(); return; }
      if (btn === 'b') { back(); return; }
      if (btn === 'lb' || btn === 'lt') return tab(-1);
      if (btn === 'rb' || btn === 'rt') return tab(1);
      if (btn === 'start' && frame.dataset.screen === 'game') { back(); return; }
      if (btn === 'select' && hudMode) { setHudMode(false); return; }
      return;
    }
    if (frame.dataset.screen !== 'game') return;
    const act = Object.keys(bind).find(a => bind[a] === btn);
    if (act) GAME[act]();
  }

  // ── Опрос падов ──
  const prev = {}, stick = {};
  function poll() {
    for (const gp of navigator.getGamepads?.() || []) {
      if (!gp) continue;
      const was = prev[gp.index] || [];
      const now = gp.buttons.map(b => b.pressed || b.value > .5);
      // Левый стик как крестовина для фокуса — с порогом и повтором
      const [lx = 0, ly = 0] = gp.axes;
      const dir = Math.abs(lx) > .6 || Math.abs(ly) > .6 ? (Math.abs(lx) > Math.abs(ly) ? (lx > 0 ? 'right' : 'left') : (ly > 0 ? 'down' : 'up')) : '';
      const any = now.some(Boolean) || dir;
      if (any && !active) window.kernInput.set(familyOf(gp.id), true);
      now.forEach((p, i) => { if (p && !was[i] && BUTTONS[i]) press(BUTTONS[i]); });
      const t = performance.now(), st = stick[gp.index] ||= { dir: '', at: 0 };
      if (dir && scope() && (dir !== st.dir || t - st.at > 220)) { move(dir); st.at = t; }
      // В игре левый стик — поворот и ходьба, как сенсорный
      if (frame.dataset.screen === 'game' && !scope()) window.kernStick?.(lx, ly);
      st.dir = dir;
      prev[gp.index] = now;
    }
    requestAnimationFrame(poll);
  }
  addEventListener('gamepadconnected', e => setFamily(familyOf(e.gamepad.id)));
  requestAnimationFrame(poll);

  // ── В игре: ромб кнопок пада на месте сенсорной «Копать» и значки на самих элементах ──
  const cluster = document.createElement('div');
  cluster.className = 'pad-cluster'; cluster.setAttribute('aria-label', 'Кнопки геймпада');
  $('hud').append(cluster);
  const blockNames = ['Военный', 'Дорога', 'Блок', 'Опора'];
  const icon = id => `<svg><use href="#${id}"/></svg>`;
  function renderCluster() {
    const auto = $('autodig').getAttribute('aria-pressed') === 'true';
    const item = document.querySelector('.inv-slots li.is-on');
    const slot = (act, cls, inner, label, extra = '') => `<div class="pd pd-${cls}${extra}">${inner}${glyph(bind[act])}<small>${label}</small></div>`;
    // Курки — над ромбом: левый лечит, правый копает
    cluster.innerHTML = `<div class="pd-shoulders">${slot('heal', 'lt', icon('i-heal'), 'Лечение')}${slot('dig', 'rt', '', 'Копать')}</div>`
      + `<div class="pd-diamond">`
      + slot('geo', 'y', icon('i-geo'), 'Геология')
      + slot('autodig', 'x', icon('i-autodig'), auto ? 'Авто: вкл' : 'Авто: выкл', auto ? ' is-on' : '')
      + slot('cancel', 'b', icon('i-close'), 'Отмена', item ? '' : ' is-idle')
      + slot('build', 'a', `<img src="${builds()[block]?.querySelector('img').src}" alt="">`, `${glyph(bind.blockPrev)}${blockNames[block]}${glyph(bind.blockNext)}`) + `</div>`;
  }
  // Значок кнопки — в углу элемента, который она нажимает
  const BADGES = [['#progRun', 'prog'], ['.minimap', 'map'], ['#sysMenu', 'menu'], ['#touchMenu', 'menu'],
    ['.inv-slots li:nth-child(1)', 'slotPrev'], ['.inv-slots li:nth-child(4)', 'slotNext'], ['.sysbar', 'hud'], ['.touch-sys', 'hud']];
  BADGES.forEach(([sel, act]) => document.querySelector(sel)?.insertAdjacentHTML('beforeend', `<span class="pg pg-badge" data-act="${act}"></span>`));
  const bar = document.createElement('div');
  bar.className = 'pad-bar'; bar.hidden = true; frame.append(bar);   // видна только с падом
  function renderBar() {
    const sc = active && scope();
    if (bar.hidden !== !sc) bar.hidden = !sc;
    if (!sc) return;
    const tabs = sc.matches('[data-screen="onboarding"]') || sc.querySelector('[role="tab"], #authTabs');
    const html = `<span>${glyph('a')}Выбрать</span><span>${glyph('b')}Назад</span>${tabs ? `<span>${glyph('lb')}${glyph('rb')}${sc.matches('[data-screen="onboarding"]') ? 'Шаги' : 'Вкладки'}</span>` : ''}`;
    if (bar.innerHTML !== html) bar.innerHTML = html;
  }
  // Открылся экран или окно — фокус сразу на главной кнопке: с падом без выделения не понять, где ты
  const PRIMARY = '.btn--primary:not([hidden]), .btn--hero, [role="tab"].is-on, .is-on[role="tab"]';
  let lastScope = null;
  function autoFocus() {
    const sc = active && scope();
    if (sc === lastScope) return;
    lastScope = sc;
    if (!sc || sc === $('hud')) return;
    if (sc.contains(document.activeElement) && document.activeElement !== document.body) return;
    const list = focusables(sc);
    (list.find(el => el.matches(PRIMARY)) || list[0])?.focus();
    // Настройки с падом открываются на кнопках пада
    if (sc.id === 'settingsDialog') document.querySelector('#ctlSeg [data-view="pad"]')?.click();
  }
  // Подсказка строки зависит от того, что открыто — пересчитывается при любой смене видимости
  new MutationObserver(() => active && renderCluster()).observe($('autodig'), { attributes: true, attributeFilter: ['aria-pressed'] });
  new MutationObserver(() => active && renderCluster()).observe($('invSlots'), { subtree: true, attributes: true, attributeFilter: ['class'] });
  new MutationObserver(recs => active && recs.some(r => !bar.contains(r.target) && !cluster.contains(r.target)) && (renderBar(), autoFocus())).observe(frame, { subtree: true, attributes: true, attributeFilter: ['hidden', 'data-screen', 'class'] });

  // ── Настройки: вкладка «Управление», вид «Геймпад» ──
  const list = $('padBinds');
  if (list) {
    list.innerHTML = ACTIONS.map(([act, name]) =>
      `<div class="field"><span><b>${name}</b></span><button type="button" class="keycap padcap" data-act="${act}"><span class="pg" data-act="${act}"></span></button></div>`).join('');
    list.onclick = e => {
      const cap = e.target.closest('.padcap'); if (!cap) return;
      document.querySelectorAll('.padcap.is-listening').forEach(c => c.classList.remove('is-listening'));
      cap.classList.add('is-listening');
      capture = btn => {
        capture = null; cap.classList.remove('is-listening');
        if (btn === 'b' && cap.dataset.act !== 'cancel') return;   // «Назад» отменяет ожидание
        const other = Object.keys(bind).find(a => bind[a] === btn);
        if (other) bind[other] = bind[cap.dataset.act];   // занятая кнопка меняется местами
        bind[cap.dataset.act] = btn;
        renderGlyphs();
      };
    };
    $('padReset').onclick = () => { Object.assign(bind, DEFAULTS); renderGlyphs(); };
    $('padGlyphs').onchange = e => { forced = e.target.value === 'auto' ? '' : e.target.value; setFamily(family); };
  }

  // ── Стенд: ?pad=xbox|ps|switch (то же, что ?input=…) ──
  renderCluster();
  window.kernScene.on(2.5, q => { if (q.get('pad')) window.kernInput.set(q.get('pad')); if (q.get('padhud')) setHudMode(true); });
  window.kernScene.onReset(() => { if (hudMode) setHudMode(false); });

  window.kernPad = { get active() { return active; }, get family() { return family; }, label: act => glyphOf(bind[act]), press };   // press — для проверок без пада
})();
