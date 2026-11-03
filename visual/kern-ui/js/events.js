// События игры поверх HUD: связь, сообщение и ссылка от сервера;
// стрелки у края экрана (миссия, своя метка), подписи над роботами; на стенде — псевдоязык и места текста сервера.
(() => {
  const $ = id => document.getElementById(id);
  const frame = $('frame'), W = window.kernWorld;

  // ── Связь — как в клиенте (ConnectionManager, ReconnectBackoff):
  //   автоповтор без предела попыток; пауза перед попыткой 1, 2, 4, 8, 16, дальше 30 с;
  //   ожидание — «Попробуем ещё раз через Nс...» (network.reconnect.retry), попытка — «Подключение...» (network.connecting);
  //   DisconnectPacket — причина от сервера, без автоповтора; обрыв вне попытки — network.error.connection_lost, без автоповтора
  const STEPS = [1, 2, 4, 8, 16, 30];
  const ATTEMPT_MS = 1500;                 // макет: сколько длится неудачная попытка
  let netTimer = 0;
  const stopNet = () => { clearInterval(netTimer); clearTimeout(netTimer); netTimer = 0; };
  function showNet(kind) {
    stopNet();
    $('netDialog').hidden = false;
    $('netReason').hidden = true;
    if (kind === 'reconnect') {
      $('netTitle').textContent = 'Связь потеряна';
      let attempt = 0, left = STEPS[0];
      const wait = () => {
        $('netText').textContent = `Попробуем ещё раз через ${left}с...`;
        netTimer = setInterval(() => {
          if (--left > 0) { $('netText').textContent = `Попробуем ещё раз через ${left}с...`; return; }
          stopNet();
          $('netText').textContent = 'Подключение...';
          // Попытка не удалась — пауза растёт по ступеням
          netTimer = setTimeout(() => { attempt++; left = STEPS[Math.min(attempt, STEPS.length - 1)]; wait(); }, ATTEMPT_MS);
        }, 1000);
      };
      wait();
      return;
    }
    $('netTitle').textContent = kind === 'kicked' ? 'Отключено сервером' : 'Связь потеряна';
    $('netText').textContent = kind === 'kicked' ? 'Сервер закрыл соединение.' : 'Соединение с сервером потеряно.';
    if (kind === 'kicked') { $('netReason').hidden = false; $('netReason').textContent = 'Причина: Перезапуск для обновления, вернёмся через 5 минут.'; }
  }
  $('netMenu').onclick = () => { stopNet(); $('netDialog').hidden = true; document.querySelector('.stand-seg [data-screen="menu"]').click(); };

  // ── Остальные окна событий ──
  const close = id => { $(id).hidden = true; };
  ['srvModal', 'linkDialog'].forEach(id => $(id).querySelectorAll('[data-close]').forEach(b => b.onclick = () => close(id)));
  $('linkOpen').onclick = () => close('linkDialog');            // в игре — открыть браузер; макет только закрывает
  document.addEventListener('keydown', e => { if (e.key === 'Escape') ['srvModal', 'linkDialog'].forEach(close); });

  const EVENTS = {
    reconnect: () => showNet('reconnect'), lost: () => showNet('lost'), kicked: () => showNet('kicked'),
    modal: () => { $('srvModal').hidden = false; },
    link: () => { $('linkDialog').hidden = false; },
  };
  function setEvent(kind) {
    stopNet(); ['netDialog', 'srvModal', 'linkDialog'].forEach(close);
    $('standEvent').value = kind;
    if (EVENTS[kind]) EVENTS[kind]();
  }
  $('standEvent').onchange = e => setEvent(e.target.value);
  window.kernScene.onReset(() => setEvent(''));

  // ── Два виртуальных кольца вокруг игрока: внутреннее — миссии (MissionArrowPacket X, Y), внешнее — остальные метки (своя метка с карты и др.) ──
  // Самих колец на экране нет — только светящийся серп там, где цель за пределами своего кольца, в её сторону: цвет — вид цели,
  // ширина растёт с близостью, подпись — название и расстояние в клетках. Цель в кадре — значок над её клеткой
  const MISSION = { kind: 'mission', x: W.PX + 58, y: W.PY - 22, color: getComputedStyle(document.documentElement).getPropertyValue('--bone').trim(), name: 'Миссия', off: () => $('mission').hidden };
  const RINGS = [{ id: 'mission', share: .29 }, { id: 'other', share: .38 }];   // радиус — доля меньшей стороны поля
  const RING_ARC_MAX = 24, RING_ARC_MIN = 8, RING_THICK = 14;                                                 // полуугол дуги, °; толщина в середине, px
  const pts = (cx, cy, r, a, w, thick, n) => {                                  // сечение дуги: толщина падает до нуля к концам
    const out = [], inn = [];
    for (let i = 0; i <= n; i++) {
      const f = -w + 2 * w * i / n, t = thick * (1 - (f / w) ** 2) ** 1.3 / 2, an = a + f;
      out.push([cx + (r + t) * Math.cos(an), cy + (r + t) * Math.sin(an)]);
      inn.push([cx + (r - t) * Math.cos(an), cy + (r - t) * Math.sin(an)]);
    }
    return { out, inn };
  };
  const num = ([x, y]) => x.toFixed(1) + ' ' + y.toFixed(1);
  const crescent = (...a) => { const { out, inn } = pts(...a, 28); return 'M' + out.concat(inn.reverse()).map(num).join('L') + 'Z'; };
  function arrows() {
    const v = W.view; if (!v) return;
    const fw = frame.offsetWidth, fh = frame.offsetHeight, cs = getComputedStyle(frame);
    const px = n => parseFloat(cs.getPropertyValue(n)) || 0;
    // Слой HUD сдвинут на безопасную зону; игрок — в центре кадра
    const left = px('--sl') + 22, right = fw - px('--sr') - 22, top = px('--st') + 22, bottom = fh - px('--sb') - 22;
    const cx = fw / 2, cy = fh / 2, lx = cx - px('--sl'), ly = cy - px('--st'), side = Math.min(right - left, bottom - top);
    const list = W.marks.filter(m => !m.off?.());
    const pins = [], parts = [];
    RINGS.forEach(ring => {
      const R = Math.round(side * ring.share), placed = [];
      list.filter(m => (m.kind === 'mission' ? 'mission' : 'other') === ring.id).forEach(m => {
        const tx = v.offX + (m.x - v.ox + .5) * W.CELL, ty = v.offY + (m.y - v.oy + .5) * W.CELL;
        const dist = Math.round(Math.hypot(m.x - W.PX, m.y - W.PY));
        if (tx > left && tx < right && ty > top && ty < bottom) pins.push(`<div class="edge-pin" style="left:${tx - px('--sl')}px;top:${ty - W.CELL / 2 - px('--st')}px;--c:${m.color}"><span>${m.name}</span></div>`);
        if (Math.hypot(tx - cx, ty - cy) <= R) return;                            // цель внутри своего кольца — дуга не нужна
        const a = Math.atan2(ty - cy, tx - cx), w = Math.max(RING_ARC_MIN, RING_ARC_MAX - dist * .15) * Math.PI / 180;
        let lr = R + 20;                                                           // подписи рядом по углу разводятся по радиусу
        while (placed.some(q => Math.abs(Math.atan2(Math.sin(a - q.a), Math.cos(a - q.a))) < .3 && Math.abs(lr - q.r) < 13)) lr += 13;
        placed.push({ a, r: lr });
        const lxp = lx + lr * Math.cos(a), lyp = ly + lr * Math.sin(a), c = Math.cos(a);
        parts.push(`<g style="--c:${m.color}"><path class="ring-glow" d="${crescent(lx, ly, R, a, w, RING_THICK)}"/><path class="ring-core" d="${crescent(lx, ly, R, a, w * .8, RING_THICK * .35)}"/><text class="ring-t" x="${lxp.toFixed(1)}" y="${(lyp + 3.5).toFixed(1)}" text-anchor="${c > .35 ? 'start' : c < -.35 ? 'end' : 'middle'}">${m.name} · ${dist}</text></g>`);
      });
    });
    $('edgeArrows').innerHTML = `<svg class="marker-ring" aria-hidden="true">${parts.join('')}</svg>` + pins.join('');
  }

  // ── Подписи над роботами (WorldLabels): ник — от правого верхнего угла клетки, облако — хвостом к роботу ──
  const bubbles = new Map([['ШАХТЁР-102', 'тут голубые, иди сюда']]);
  const esc = s => s.replace(/[&<>]/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[ch]));
  function labels() {
    const v = W.view; if (!v) return;
    const me = { nick: 'ШАХТЁР-774', clan: true, x: W.PX, y: W.PY };
    $('worldLabels').innerHTML = [...W.bots, me].map(b => {
      const x = v.offX + (b.x - v.ox) * W.CELL, y = v.offY + (b.y - v.oy) * W.CELL, say = bubbles.get(b.nick);
      return `<div class="wl-name" style="left:${x + W.CELL}px;top:${y}px">${b.clan ? '<img src="img/clan.png" alt="">' : ''}${esc(b.nick)}</div>`
        + (say ? `<div class="wl-chat" style="left:${x + W.CELL / 2}px;top:${y}px"><span>${esc(say)}</span></div>` : '');
    }).join('');
  }
  // Локальный чат: своя реплика встаёт облаком над своим роботом
  $('sayForm').addEventListener('submit', () => { const t = $('sayInput').value.trim(); if (t) { bubbles.set('ШАХТЁР-774', t); labels(); } }, true);
  W.onDraw = () => { labels(); arrows(); };
  new MutationObserver(() => W.draw()).observe($('mission'), { attributes: true, attributeFilter: ['hidden'] });

  // ── Стенд: текст под нагрузкой. Рост строки — по длине оригинала (замер en/ru из kern-ui-lab, 849 ключей) ──
  const BUCKETS = [[5, 2.2, 4.5], [10, 1.67, 3.11], [20, 1.47, 2.79], [40, 1.28, 1.64], [Infinity, 1.16, 1.38]];
  const FILLER = ['ша', 'ру', 'де', 'ло', 'ти', 'ва', 'но', 'ре', 'ки', 'ма'];
  const grow = (src, mode) => {
    const b = BUCKETS.find(x => src.trim().length <= x[0]), target = Math.round(src.trim().length * (mode === 'worst' ? b[2] : b[1]));
    let out = src.trim(), i = 0;
    while (out.length < target) out += (i++ === 0 ? ' ' : '') + FILLER[(i - 1) % FILLER.length];
    // Скобки — граница строки: виден «[» без «]» — хвост срезан
    return src.match(/^\s*/)[0] + '[' + out + ']' + src.match(/\s*$/)[0];
  };
  // Не переводятся: числа, координаты, ники и метки каналов (заглавные без пробелов), клавиши, слова игроков и имена программ
  const skip = (node, t) => !/[A-Za-zА-Яа-яЁё]{2}/.test(t) || /^\s*[A-ZА-ЯЁ0-9_\-.#$%:/]+\s*$/.test(t)
    || node.parentElement?.closest('kbd, .pg, script, style, [translate="no"], .stand-bar, .prog-cell, .nick-link, .nick, .wl-name, #chatPeek3, .chat-log li:not(.sys), .wl-chat, #progName, #progPickName');
  let mode = 'ru';
  const orig = new Map(), mine = new WeakMap();
  function apply(node) {
    if (mine.get(node) === node.data) return;                   // своя замена — не трогать
    const src = node.data;
    if (skip(node, src)) return;
    orig.set(node, src);
    node.data = grow(src, mode); mine.set(node, node.data);
  }
  const walk = root => { const it = document.createTreeWalker(root, NodeFilter.SHOW_TEXT); let n; while ((n = it.nextNode())) apply(n); };
  const obs = new MutationObserver(list => {
    if (mode !== 'p90' && mode !== 'worst') return;
    for (const m of list) {
      if (m.type === 'characterData') apply(m.target);
      else m.addedNodes.forEach(n => n.nodeType === 3 ? apply(n) : n.nodeType === 1 && walk(n));
    }
  });
  function setText(m) {
    if (m !== 'ru') window.kernLang?.set('ru');                 // проверка — по русскому тексту
    orig.forEach((src, node) => { if (node.isConnected && mine.get(node) === node.data) node.data = src; });
    orig.clear();
    mode = m;
    frame.classList.toggle('srv-slots', m === 'slots');
    $('standText').value = m;
    if (m === 'p90' || m === 'worst') { walk(frame); obs.observe(frame, { subtree: true, childList: true, characterData: true }); }
    else obs.disconnect();
  }
  $('standText').onchange = e => setText(e.target.value);
  window.kernText = { set: setText };

  W.ready.then(() => { W.marks.push(MISSION); W.draw(); });
  // Снимок: окна без плавного появления (в безголовом браузере кадры анимации не идут)
  window.kernScene.on(3, q => {
    if (q.get('event')) { ['netDialog', 'srvModal', 'linkDialog'].forEach(id => { $(id).style.transition = 'none'; }); setEvent(q.get('event')); }
    if (q.get('text')) setText(q.get('text'));
  });
  window.kernScene.onReset(() => { if (mode !== 'ru') setText('ru'); });
})();
