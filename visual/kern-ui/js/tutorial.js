// Миссии и подсказки обучения — как в легаси-клиенте OpenMines (MissionPad, TutorialNavigation), в коде дизайна.
// Миссия: MissionPanelPacket «MM» (Url, ImgX, ImgY, Progress 0…100, Text) и MissionProgressPacket «MP» (Experience, Max).
//   Первая строка Text — название, остальное — задание. %P% — счёт MP («3 / 10», при Max = 1 пусто), %T%сек% — обратный
//   отсчёт. Пустой Text — панели нет. Нажатие на панель — команда Miso (сервер присылает миссию заново) и скрытие «_MISO».
// Подсказка: «MN» (Text, dx, dy, AnchorType, HideReason). Якорь % 10: 0–3 — угол экрана (правый верх, правый низ, левый низ,
//   левый верх), 4–7 — от центра; плашка встаёт к якорю своим углом, выноска из этого угла указывает дальше.
//   Якорь < 10 — линза (всё, кроме места у выноски, притушено) и робот стоит. HideReason — маркеры действий («_MISO»,
//   «_PROG»…): подсказка уходит, когда игрок сделал это действие; «:N» — через N секунд.
// Стрелка к цели миссии (GO) — серп на кольце вокруг игрока, js/events.js
(() => {
  const $ = id => document.getElementById(id);
  const frame = $('frame');

  // ── Миссии стенда: что мог бы прислать сервер ──
  const MISSIONS = {
    first: { text: 'Первая добыча\nНакопай 10 синих кристаллов', progress: 30, exp: 3, max: 10 },
    timer: { text: 'Срочная доставка\nСдай груз на Рынке — осталось %T%252%', progress: 60, exp: 3, max: 5 },
    image: { url: 'img/cells/BlackRock.png', w: 32, h: 32, text: 'Глубже\nДокопайся до чёрной породы', progress: 0, exp: 0, max: 1 },
    build: { url: 'img/items/resp.png', w: 32, h: 32, text: 'Своё место\nПоставь респаун и сохрани в нём точку возрождения', progress: 50, exp: 1, max: 2 },
    single: { text: 'Связь\nНапиши что-нибудь в чат', progress: 0, exp: 0, max: 1 },
    done: { text: 'Миссия выполнена\nНажми на панель, чтобы получить награду', progress: 100, exp: 1, max: 1 },
    long: { text: 'Экспедиция к старым выработкам за восточным хребтом\nДобудь 2 500 голубых кристаллов на глубине больше 1 200 клеток и вернись на Рынок клана до конца смены', progress: 20, exp: 500, max: 2500 },
  };

  let timer = null, endAt = 0, macro = '', count = '';
  const pad = n => String(n).padStart(2, '0');
  const left = () => {
    const s = Math.max(0, Math.ceil((endAt - Date.now()) / 1000)), h = Math.floor(s / 3600);
    return (h ? pad(h) + ':' : '') + pad(Math.floor(s / 60) % 60) + ':' + pad(s % 60);
  };
  const renderText = () => {
    const t = macro.replace('%T%', left()).replace('%P%', count);
    $('missionText').textContent = t;
    $('missionText').title = t;
  };

  function setMission(m) {
    clearInterval(timer); timer = null;
    const box = $('mission');
    if (!m || !m.text) { box.hidden = true; return; }
    const [title, ...rest] = m.text.split('\n');
    let body = rest.join(' ');
    const tm = body.match(/%T%(\d+)%/);
    if (tm) { endAt = Date.now() + Number(tm[1]) * 1000; body = body.replace(tm[0], '%T%'); timer = setInterval(renderText, 1000); }
    macro = body;
    count = m.max > 1 ? `${m.exp} / ${m.max}` : '';
    $('missionTitle').textContent = title;
    $('missionTitle').title = title;
    renderText();
    $('missionProgress').textContent = count;
    $('missionProgress').hidden = !count;
    const img = $('missionImg');
    img.hidden = !m.url;
    box.classList.toggle('has-img', !!m.url);
    if (m.url) { img.src = m.url; img.width = m.w; img.height = m.h; }
    window.kernQuant($('missionCells'), 10, Math.round(Math.max(0, Math.min(100, m.progress)) / 10));
    box.hidden = false;
    $('standMission').value = Object.keys(MISSIONS).find(k => MISSIONS[k] === m) || '';
  }
  window.kernMission = setMission;

  // ── Подсказка обучения ──
  let hideReason = '', hideTimer = null;
  const CORNERS = ['ru', 'rd', 'ld', 'lu'];   // угол плашки, из которого идёт выноска: правый верх, правый низ, левый низ, левый верх
  function setNavi(text, dx, dy, anchor, reason) {
    clearTimeout(hideTimer); hideTimer = null;
    const layer = $('tutLayer'), tut = $('tut'), lens = $('tutLens');
    hideReason = text ? reason : '';
    window.kernWorld.locked = !!text && anchor < 10;
    if (!text) { layer.hidden = true; return; }
    layer.hidden = false;
    $('tutText').textContent = text;
    const k = anchor % 10, corner = CORNERS[k % 4], center = k >= 4;
    tut.dataset.corner = corner;
    const W = layer.offsetWidth, H = layer.offsetHeight, w = tut.offsetWidth, h = tut.offsetHeight;
    const right = corner[0] === 'r', down = corner[1] === 'd';
    // Точка якоря: от угла экрана (0–3) или от центра (4–7); к ней — угол плашки с выноской
    const ax = center ? W / 2 + (right ? dx : -dx) : right ? W - dx : dx;
    const ay = center ? H / 2 + (down ? -dy : dy) : down ? H - dy : dy;
    // Плашка не выходит за слой HUD: отступы сервер считал для своего экрана, на узком кадре она сдвигается внутрь
    const clamp = (v, max) => Math.max(4, Math.min(max - 4, v));
    tut.style.left = `${clamp(right ? ax - w : ax, W - w)}px`;
    tut.style.top = `${clamp(down ? ay - h : ay, H - h)}px`;
    // Линза: просвет от конца выноски в её сторону — там то, на что указывает подсказка (колонка HUD шириной --col)
    lens.hidden = anchor >= 10;
    const lead = 22, gap = 8, hw = 288, hh = 124;
    const ex = ax + (right ? lead : -lead), ey = ay + (down ? lead : -lead);
    lens.style.left = `${right ? ex - gap : ex - hw + gap}px`;
    lens.style.top = `${down ? ey - gap : ey - hh + gap}px`;
    lens.style.width = `${hw}px`; lens.style.height = `${hh}px`;
    if (reason.startsWith(':')) hideTimer = setTimeout(() => { setNavi('', 0, 0, 10, ''); hidden('TIMEOVER'); }, Number(reason.slice(1)) * 1000);
    $('standTut').value = Object.keys(TUTS).find(n => TUTS[n].text === text) || '';
  }
  window.kernNavi = setNavi;
  // Действие игрока, которого ждёт подсказка, — она уходит (клиент шлёт серверу маркер)
  const checkHide = marker => { if (hideReason.includes(marker)) { setNavi('', 0, 0, 10, ''); hidden(marker); } };
  window.kernTutorialDone = checkHide;

  // Подсказки стенда: отступы считаются от настоящего места цели — так их задал бы сервер для этого кадра
  const rel = sel => {
    const el = document.querySelector(sel), L = $('tutLayer');
    if (!el || el.hidden) return null;
    const f = frame.getBoundingClientRect(), k = f.width / frame.offsetWidth || 1, r = el.getBoundingClientRect(), l = L.getBoundingClientRect();
    return { left: (r.left - l.left) / k, top: (r.top - l.top) / k, right: (r.right - l.left) / k, bottom: (r.bottom - l.top) / k, W: L.offsetWidth, H: L.offsetHeight };
  };
  const TUTS = {
    mission: { text: 'Это твоя миссия. Нажми на неё, чтобы узнать подробности', place: () => {
      if ($('mission').hidden) setMission(MISSIONS.first);
      const r = rel('#mission'); return [r.W - r.left + 22, r.bottom + 22, 0, '_MISO'];
    } },
    prog: { text: 'Программатор повторяет действия робота. Открой его здесь', place: () => {
      const r = rel('.panel.prog'); return [r.right + 22, r.H - r.top + 22, 12, '_PROG'];
    } },
    center: { text: 'Под тобой синие кристаллы — копай вниз', place: () => [-38, 38, 15, ':8'] },   // выноска упирается в клетку робота (центр кадра)
  };
  const show = (name, reason) => {
    $('tutLayer').hidden = false;                                  // отступы меряются по слою — он должен иметь размер
    const [dx, dy, anchor, own] = TUTS[name].place();
    setNavi(TUTS[name].text, dx, dy, anchor, reason || own);
  };

  // ── Сценарий первого входа. Клиент сценария не знает: сервер шлёт подсказки по одной, клиент на скрытие отвечает
  //   маркером (THID — действие игрока или TIMEOVER), и сервер присылает следующую. Здесь стенд играет сервер:
  //   миссия → копать (маркер _DIG — первая выкопанная клетка) → программатор → конец
  const FIRST = [['mission'], ['center', '_DIG'], ['prog']];
  let step = -1, pending = null;
  const play = () => { if (step >= 0 && step < FIRST.length) { show(...FIRST[step]); $('standTut').value = 'first'; } else { step = -1; $('standTut').value = ''; } };
  // THID: подсказка ушла — серверу уходит маркер; в сценарии через миг приходит следующая
  function hidden(marker) {
    if (step < 0) return;
    step++;
    clearTimeout(pending);
    pending = setTimeout(play, 700);
  }
  const stopScript = () => { step = -1; clearTimeout(pending); pending = null; };

  const tut = name => {
    stopScript();
    if (name === 'first') { setMission(MISSIONS.first); step = 0; play(); return; }
    if (!TUTS[name]) { $('tutLayer').hidden = false; setNavi('', 0, 0, 10, ''); return; }
    show(name);
  };

  // Первая выкопанная клетка — маркер _DIG
  const dig = window.kernWorld.dig;
  window.kernWorld.dig = (x, y) => { const r = dig(x, y); checkHide('_DIG'); return r; };

  // Нажатия, которых ждут подсказки: миссия — _MISO, программатор — _PROG
  $('mission').addEventListener('click', () => checkHide('_MISO'));
  document.querySelector('.panel.prog')?.addEventListener('click', () => checkHide('_PROG'), true);

  $('standMission').onchange = e => setMission(MISSIONS[e.target.value]);
  $('standTut').onchange = e => tut(e.target.value);
  window.kernScene.on(2, q => {
    if (q.get('mission')) setMission(MISSIONS[q.get('mission')]);
    if (q.get('tut')) requestAnimationFrame(() => tut(q.get('tut')));
  });
  window.kernScene.onReset(() => { stopScript(); setNavi('', 0, 0, 10, ''); });
})();
