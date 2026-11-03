// Поток экранов: Bootstrap → вход → онбординг (один раз) → меню → спуск → игра,
// плюс окно навыков в игре. Тексты — строки словаря Kern и сервера.
(() => {
  const $ = id => document.getElementById(id);
  const frame = $('frame');
  const timers = new Set();
  const later = (fn, ms) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; };
  const clearAll = () => { timers.forEach(clearTimeout); timers.clear(); };
  const hold = Number(new URLSearchParams(location.search).get('hold')) || 0;
  // ?still=1 — снимок стенда: без переходов (в безголовом браузере анимация появления не доигрывается, окна выходят полупрозрачными)
  if (new URLSearchParams(location.search).get('still')) document.head.insertAdjacentHTML('beforeend', '<style>*, *::before, *::after { transition: none !important; animation: none !important; }</style>');

  function show(screen) {
    clearAll();
    const screenName = screen === 'descent' ? 'menu' : screen;
    const enteringScreen = document.querySelector(`.screen[data-screen="${screenName}"]`);
    enteringScreen?.classList.add('is-entering');
    frame.dataset.screen = screen;
    if (enteringScreen) requestAnimationFrame(() => enteringScreen.classList.remove('is-entering'));
    document.querySelectorAll('[data-screen]').forEach(b => { if (b.tagName === 'BUTTON') b.classList.toggle('is-on', b.dataset.screen === screen); });
    document.querySelectorAll('[data-for]').forEach(g => g.hidden = g.dataset.for !== (screen === 'descent' ? 'menu' : screen));
    $('menuMain').hidden = screen === 'descent';
    $('descent').hidden = screen !== 'descent';
    if (screen === 'boot') runBoot();
    if (screen === 'descent') runDescent();
    if (screen === 'game') { window.kernWorld?.draw(); window.kernAssets?.(); }
  }
  document.querySelectorAll('button[data-screen]').forEach(b => b.addEventListener('click', () => show(b.dataset.screen)));

  // UI Toolkit-аналог: видимые состояния переключаются классами, свойства
  // opacity/scale/translate плавно интерполируются самими стилями.
  document.querySelectorAll('.dialog-layer').forEach(layer => {
    new MutationObserver(() => {
      if (layer.hidden) return;
      layer.classList.add('is-entering');
      requestAnimationFrame(() => layer.classList.remove('is-entering'));
    }).observe(layer, { attributes: true, attributeFilter: ['hidden'] });
  });

  // ── Bootstrap: фаза загрузки, затем вход ──
  function runBoot() {
    let p = 0;
    $('bootPhase').textContent = 'Компиляция шейдеров…';
    const tick = () => {
      p = Math.min(100, p + 7);
      $('bootFill').style.width = p + '%';
      if (p >= 60) $('bootPhase').textContent = 'Загрузка ресурсов…';
      if (hold && p >= hold) return;                              // ?hold=N — снимок на N %
      if (p < 100) later(tick, 90); else later(() => show('auth'), 400);
    };
    $('bootFill').style.width = '0';
    later(tick, 200);
  }

  // ── Вход: логин и пароль или VK ──
  // Ход входа пишется на нажатой кнопке, а не отдельной строкой внизу окна
  function enter(btn, text) {
    const label = btn.innerHTML, busy = [$('authSubmit'), $('regSubmit'), $('authVk')];
    btn.textContent = text; busy.forEach(b => b.disabled = true);
    later(() => {
      btn.innerHTML = label; busy.forEach(b => b.disabled = false);
      show(onboardingDone ? 'menu' : 'onboarding');
    }, 1100);
  }
  // Ошибка показывается под своим полем и снимается, как только поле правят
  const fieldError = (f, text) => { f.closest('.fld').querySelector('.fld-err').textContent = text; };
  document.querySelectorAll('.auth-form .fld input').forEach(f => f.addEventListener('input', () => fieldError(f, '')));
  const check = rules => {
    rules.forEach(([f]) => fieldError(f, ''));
    const bad = rules.filter(([f, ok]) => !ok(f));
    bad.forEach(([f, , text]) => fieldError(f, text));
    bad[0]?.[0].focus();
    return !bad.length;
  };
  const filled = f => f.value.trim() !== '';
  $('authForm').onsubmit = e => {
    e.preventDefault();
    if (check([[$('authLogin'), filled, 'Введите логин'], [$('authPassword'), filled, 'Введите пароль']])) enter($('authSubmit'), 'Подключение…');
  };
  $('authVk').onclick = () => enter($('authVk'), 'Подтвердите вход в браузере…');
  $('authForgot').onclick = () => { $('authForgotNote').textContent = 'Восстановить пароль можно через поддержку'; };

  // ── Вкладки входа и регистрации ──
  $('authTabs').onclick = e => {
    const b = e.target.closest('button'); if (!b) return;
    $('authTabs').querySelectorAll('button').forEach(x => x.setAttribute('aria-checked', String(x === b)));
    const reg = b.dataset.tab === 'register';
    [[$('authForm'), !reg], [$('regForm'), reg]].forEach(([f, on]) => { f.classList.toggle('is-off', !on); f.inert = !on; });
    $('authForgotNote').textContent = '';
  };
  $('regForm').onsubmit = e => {
    e.preventDefault();
    const ok = check([
      [$('regLogin'), filled, 'Введите логин'],
      [$('regPassword'), filled, 'Придумайте пароль'],
      [$('regRepeat'), f => filled(f) && f.value === $('regPassword').value, filled($('regRepeat')) ? 'Пароли не совпадают' : 'Повторите пароль'],
      [$('regTerms'), f => f.checked, 'Нужно принять правила'],
    ]);
    if (ok) enter($('regSubmit'), 'Создаём аккаунт…');
  };

  // ── Онбординг: три шага, показывается один раз ──
  let onboardingDone = false, step = 0;
  const titles = ['Доступность', 'Графика', 'Звук'];
  function renderStep() {
    document.querySelectorAll('.onb-step').forEach(f => f.classList.toggle('is-on', Number(f.dataset.step) === step));
    document.querySelectorAll('#onbSteps li').forEach((li, i) => { li.classList.toggle('is-on', i === step); li.classList.toggle('is-done', i < step); });
    $('onbTitle').textContent = titles[step];
    $('onbPrev').disabled = step === 0;
    $('onbNext').textContent = step === 2 ? 'Готово' : 'Далее';
  }
  const finish = () => { onboardingDone = true; step = 0; renderStep(); show('menu'); };
  $('onbNext').onclick = () => step === 2 ? finish() : (step++, renderStep());
  $('onbPrev').onclick = () => { if (step > 0) { step--; renderStep(); } };
  $('onbSkip').onclick = finish;
  $('onbVolume').oninput = e => { $('onbVolumeOut').textContent = e.target.value + '%'; };

  // ── Меню: обновление вместо спуска, выбор сервера ──
  let outdated = false;
  function setOutdated(on) {
    outdated = on;
    $('descendBtn').hidden = on;
    $('updateBlock').hidden = !on;
    $('versionLabel').textContent = '0.8.14';
    document.querySelectorAll('[data-update]').forEach(b => b.classList.toggle('is-on', b.dataset.update === (on ? '1' : '0')));
  }
  document.querySelectorAll('[data-update]').forEach(b => b.onclick = () => setOutdated(b.dataset.update === '1'));
  // Окно обновления: список изменений, затем загрузка с прогрессом
  function openUpdate() {
    $('updProgress').hidden = true; $('updConfirm').disabled = !outdated; $('updDefer').disabled = false;
    $('updateDialog').hidden = false;
  }
  $('updateBtn').onclick = openUpdate;
  $('updateDialog').querySelector('[data-close]').onclick = $('updDefer').onclick = () => { $('updateDialog').hidden = true; };
  $('updConfirm').onclick = () => {
    let p = 0;
    $('updConfirm').disabled = $('updDefer').disabled = true;
    $('updProgress').hidden = false;
    const tick = () => {
      p += 4;
      $('updFill').style.width = p + '%';
      $('updPercent').textContent = p + '%';
      if (p < 100) later(tick, 60);
      else later(() => { $('updateDialog').hidden = true; setOutdated(false); $('versionLabel').textContent = '0.9.0'; }, 300);
    };
    $('updFill').style.width = '0'; tick();
  };

  // Проверка доступности файлов, на которые ссылается клиентский интерфейс.
  const healthDialog = $('healthDialog');
  healthDialog.querySelectorAll('[data-close]').forEach(button => {
    button.onclick = () => { healthDialog.hidden = true; };
  });
  $('healthCheck').onclick = async () => {
    const urls = [...new Set([...document.images]
      .map(image => image.currentSrc || image.src)
      .filter(url => url && new URL(url, document.baseURI).origin === location.origin))];
    const total = urls.length;
    let checked = 0, missing = 0;
    $('healthCheck').disabled = true;
    $('healthProgress').hidden = false;
    $('healthStatus').textContent = 'Проверка файлов…';
    $('healthStatus').className = '';
    $('healthCount').textContent = `0 / ${total}`;
    $('healthFill').style.width = '0%';

    for (let index = 0; index < urls.length; index += 8) {
      const batch = urls.slice(index, index + 8);
      const results = await Promise.all(batch.map(async url => {
        try {
          const response = await fetch(url, { method: 'HEAD', cache: 'no-store' });
          return response.ok;
        } catch {
          return false;
        }
      }));
      checked += results.length;
      missing += results.filter(ok => !ok).length;
      $('healthCount').textContent = `${checked} / ${total}`;
      $('healthFill').style.width = `${total === 0 ? 0 : checked / total * 100}%`;
    }

    const healthy = total > 0 && missing === 0;
    $('healthStatus').textContent = healthy
      ? 'Все ресурсы доступны'
      : total === 0 ? 'Ресурсы для проверки не найдены' : `Не найдены ресурсы: ${missing}`;
    $('healthStatus').classList.add(healthy ? 'is-healthy' : 'has-errors');
    $('healthCheck').disabled = false;
    $('healthCheck').textContent = 'Проверить ещё раз';
  };
  $('descendBtn').onclick = () => show('descent');

  // ── Спуск: фазы экрана, как в клиенте (WorldLoadPhase), затем игра ──
  // Реальные этапы идут вперемешку: сцена игры и системы, соединение и вход параллельно проверке света и интерфейсу,
  // после ответа мира — ресурсы, данные робота и мир одновременно. Экран показывает первый ещё не готовый и не
  // откатывается: фаза готова, когда готовы она и все до неё. `ready` — мс от нажатия. Текстуры ячеек и свет входят
  // в готовность рельефа (TerrainReadiness), поэтому отдельной фазы «Загрузка текстур» нет.
  // ?cached=1 — ресурсы уже на диске и совпали по MD5: скачивания нет, вес не показывается, фаза проходит за проверку
  const warmCache = !!new URLSearchParams(location.search).get('cached');
  const STEPS = [
    { label: 'Подключение к серверу', detail: 'Устанавливаем соединение с сервером.', ready: 2400 },   // сцена, системы, соединение, вход, ответ мира
    { label: 'Загрузка ресурсов', detail: 'Подготавливаем игровые ресурсы.', ready: warmCache ? 2700 : 3600 },  // скачивание ассетов мира (из кэша — только проверка)
    { label: 'Получение позиции', detail: 'Получаем место появления вашего робота.', ready: 3800 },   // позиция, статы и вид робота
    { label: 'Построение мира', detail: 'Подготавливаем участок шахты вокруг вас.', ready: 4800 },    // рельеф, текстуры ячеек, поверхность, свет
  ];
  // Советы на спуске: меняются каждые несколько секунд
  const TIPS = ['<b>Синие кристаллы</b> встречаются глубже зелёных и стоят дороже.', 'Груз больше 100% замедляет робота — <b>сдайте кристаллы</b> на Рынке.',
    '<b>Автокопание</b> копает перед роботом, пока вы заняты другим.', 'Дорога ускоряет передвижение — навык <b>Передвижение по дороге</b> ускоряет ещё сильнее.',
    'Опоры держат свод: без них <b>песок осыпается</b>.', 'Программатор повторяет действия робота — запишите маршрут и <b>запустите программу</b>.'];
  const ASSET_FILES = 64;                                           // файлов в манифесте для показа проверки кэша
  const ASSET_MB = warmCache ? 0 : 48.4;                                            // вес ресурсов мира для показа; 0 — размеры манифеста неизвестны
  const WAIT = [1, 2, 4, 8, 16, 30];                                // паузы переподключения, с
  // ?descent=error — ошибка на фазе «Получение позиции»; retry — связь потеряна при подключении; slow — не приходят данные робота
  const descentMode = new URLSearchParams(location.search).get('descent') || '';
  const BAD = 'Не удалось войти в шахту. Проверьте соединение и попробуйте снова.';
  const STUCK = { retry: 0, error: 2, slow: 2 }[descentMode];       // фаза, на которой вход встаёт
  function runDescent() {
    const list = $('descentPhases');
    list.innerHTML = STEPS.map(st => `<li>${st.label}</li>`).join('');
    const items = [...list.children];
    const eff = STEPS.map((_, k) => Math.max(...STEPS.slice(0, k + 1).map(st => st.ready)));   // готовность с учётом предыдущих
    const retry = $('descentRetry');
    const t0 = performance.now();
    let frozen = false, failed = -1;
    retry.hidden = true;
    const note = (text, bad) => { $('descentExplain').textContent = text; $('descentExplain').classList.toggle('is-bad', !!bad); };
    const paint = t => {
      let done = eff.filter(e => t >= e).length;
      if (STUCK !== undefined) done = Math.min(done, STUCK);
      const cur = Math.min(done, STEPS.length - 1);
      items.forEach((li, k) => {
        li.classList.toggle('is-done', k < done);
        li.classList.toggle('is-on', k === done && k !== failed);
        li.classList.toggle('is-fail', k === failed);
      });
      $('descentFill').style.width = (done / STEPS.length * 100) + '%';
      // Фаза «Загрузка ресурсов»: сперва проверка кэша (проверено файлов из скольких), затем — если есть что качать — вес
      // скачанного из скольких. Всё в кэше: только проверка на всю фазу, без веса
      let mb = '';
      if (cur === 1) {
        const q = Math.min(1, Math.max(0, (t - eff[0]) / (eff[1] - eff[0])));
        const checkShare = ASSET_MB ? .25 : 1;
        if (q < checkShare) mb = `Проверка ресурсов: ${Math.round(q / checkShare * ASSET_FILES)} из ${ASSET_FILES}`;
        else if (ASSET_MB) mb = `${((q - checkShare) / (1 - checkShare) * ASSET_MB).toFixed(1)} из ${ASSET_MB.toFixed(1)} МБ`;
      }
      $('descentPhase').textContent = mb ? `${STEPS[cur].label} · ${mb}` : STEPS[cur].label;
      $('descentCount').textContent = `${cur + 1} / ${STEPS.length}`;
      return { done, cur };
    };
    // Потеря связи при подключении: клиент ждёт 1, 2, 4, 8, 16, 30 с и пробует снова; после шестой неудачи — ошибка
    const reconnect = n => {
      let left = WAIT[n - 1];
      const count = () => {
        if (left > 0) { note(`Связь потеряна. Повторная попытка ${n} через ${left} с.`, true); left--; later(count, 1000); return; }
        note(`Повторная попытка ${n}…`, true);
        later(() => {
          if (n < WAIT.length) reconnect(n + 1);
          else { failed = 0; paint(performance.now() - t0); note(BAD, true); retry.hidden = false; }
        }, 1500);
      };
      count();
    };
    const tick = () => {
      const t = performance.now() - t0;
      const { done, cur } = paint(t);
      if (!frozen) {
        if (descentMode === 'retry' && t >= 1200) { frozen = true; reconnect(1); return; }
        if (descentMode === 'error' && t >= eff[1] + 400) { frozen = true; failed = 2; paint(t); note(BAD, true); retry.hidden = false; return; }
        if (descentMode === 'slow' && t >= eff[STEPS.length - 1]) { frozen = true; note('Ждём данные робота.'); return; }
      }
      if (hold && t >= hold / 100 * eff[STEPS.length - 1]) return;  // ?hold=N — снимок на N % времени
      note(STEPS[cur].detail);
      if (done === STEPS.length) { later(() => show('game'), 500); return; }
      later(tick, 60);
    };
    $('descentFill').style.width = '0';
    let tip = Math.floor(Math.random() * TIPS.length);
    const nextTip = () => { $('descentTip').innerHTML = TIPS[tip++ % TIPS.length]; };
    nextTip(); clearInterval(runDescent.tips); runDescent.tips = setInterval(nextTip, 4000);
    retry.onclick = () => { clearAll(); runDescent(); };
    tick();
  }
  $('descentCancel').onclick = () => show('menu');

  // ── Окно навыков (UpPage) ──
  const SLOT_POS = [20,110,21,153,38,75,38,191,74,94,74,169,59,132,100,132,51,22,93,36,51,241,93,226,281,50,256,121,312,23,307,87,377,27,376,76,374,137,375,176,165,46,145,131,185,142,332,153,203,24,248,76,341,57,343,103,138,87,248,17,297,133,214,115,175,98,205,68];
  const SLOT_AMOUNT = 20;
  const skills = window.kernSkills;
  // Установленные навыки игрока: слот → код (PlayerSkills; первые четыре — начальные)
  const slots = { 0: 'm', 1: 'd', 2: 'M', 3: 'l', 4: 'L', 5: 'A', 6: 't', 7: 'F', 8: 'Y', 9: 'p', 10: 'E', 11: 'b', 12: 'c', 13: 'r' };
  const INSTALLABLE = ['A', 'L', 'Y', 'E', 'O', 'xL', 'F', 't', 'p', 'b', 'c', 'g', 'r', 'v', 'w', 'xI', 'u', 'e'];
  // build_skill_description: формат сервера, числа — по его формулам
  function describe(code, set = skills) {
    const s = set[code], exp = `${s.exp.toFixed(1)}/1.0`, x = s.level;
    if (code === 'M') return `Передвижение Уровень:${x}\nExp - ${exp}\nСкорость передвижения ${(1 / (Math.max(30, 70 - x * 0.05) * 1.2e-3) * 0.3 * 3.6).toFixed(2)} км/ч`;
    if (code === 'd') return `Копание Уровень:${x}\nExp - ${exp}\nСила копания: ${100 + x * 10}`;
    if (code === 'l') return `Защита Уровень:${x}\nExp - ${exp}\nМакс. здоровье: ${100 + x * 3}`;
    if (code === 'm') return `Добыча Уровень:${x}\nExp - ${exp}\nМножитель добычи: ${(0.08 + Math.log10(x) * Math.sqrt(x) / 4).toFixed(2)}`;
    return `${s.name} Уровень:${x}\nExp - ${exp}`;
  }
  let selected = -1, extraSlots = 0;
  // Робот со слотами — одна отрисовка для окна UP и профиля
  function paintRobot(robot, amount, sel, onPick, slotMap = slots, set = skills) {
    const cs = getComputedStyle(robot);
    const s = parseFloat(cs.getPropertyValue('--s')), pad = parseFloat(cs.getPropertyValue('--pad'));
    robot.querySelectorAll('.slot').forEach(n => n.remove());
    for (let i = amount - 1; i >= 0; i--) {                     // как PopupManager: первый рисуется поверх
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'slot';
      b.style.left = `${pad + SLOT_POS[2 * i] * s}px`;
      b.style.top = `${pad + SLOT_POS[2 * i + 1] * s}px`;
      const code = slotMap[i];
      if (code) {
        const k = set[code];
        b.style.setProperty('--c', k.color);
        b.title = k.name;
        b.innerHTML = `<i class="sk sk-${code}"></i><span class="lvl">${k.level}</span>` + (k.exp >= 1 ? '<span class="ready" title="Готово к улучшению"></span>' : '');
      } else { b.classList.add('is-empty'); b.title = 'Пустой слот'; }
      b.classList.toggle('is-on', i === sel);
      b.onclick = () => onPick(i);
      robot.appendChild(b);
    }
  }
  function renderSkills() {
    const amount = SLOT_AMOUNT + extraSlots;
    paintRobot($('skillsRobot'), amount, selected, i => { selected = i; renderSkills(); });
    $('skillsSlotsInfo').textContent = `${Object.keys(slots).length} из ${amount} слотов занято`;
    const info = $('skillInfo'), code = slots[selected];
    if (selected < 0) {
      info.innerHTML = `<p class="skill-desc">Выберите скилл или пустой слот</p>
        <div class="skill-tools"><button type="button" class="btn btn--primary" id="buySlot">Купить слот (1000 кредов)</button></div>`;
      $('buySlot').onclick = () => { if (amount < 34) { extraSlots++; renderSkills(); } };
    } else if (code) {
      const k = skills[code];
      info.innerHTML = `<i class="big sk sk-${code}"></i><h3>${k.name}</h3><p class="skill-desc">${describe(code)}</p>
        <div class="skill-tools">${k.exp >= 1 ? '<button type="button" class="btn btn--primary" id="upSkill">Улучшить</button>' : '<span class="spacer"></span>'}
        <button type="button" class="icon-btn" title="Замок"><svg><use href="#i-lock"/></svg></button>
        <button type="button" class="icon-btn" title="Удалить навык"><svg><use href="#i-trash"/></svg></button></div>`;
      if ($('upSkill')) $('upSkill').onclick = () => { k.level++; k.exp = 0; window.kernHud?.skillChanged(code); renderSkills(); };
    } else {
      info.innerHTML = `<p class="skill-desc">Выберите навык для установки</p>
        <div class="install">${INSTALLABLE.map(c => `<button type="button" class="sk sk-${c}" title="Установить"></button>`).join('')}</div>`;
    }
  }
  $('setExit').onclick = () => { $('settingsDialog').hidden = true; show('menu'); };   // на пальце выход — внизу настроек
  // Пауза на ПК
  $('menuDialog').querySelector('[data-close]').onclick = () => { $('menuDialog').hidden = true; };
  $('menuSettings').onclick = () => { $('menuDialog').hidden = true; window.openSettings(); };
  $('pauseExit').onclick = () => { $('pauseDialog').hidden = true; show('menu'); };
  $('menuExit').onclick = () => { $('menuDialog').hidden = true; show('menu'); };
  // ── Профили: свой и чужие. Чужой можно скрыть — тогда видно только ник, клан, ID и уровень ──
  const ME = 'ШАХТЁР-774';
  const ACH = [
    ['img/crystals/blue.png', 'Синий улов', 'Добыть 10 000 синих кристаллов', 1], ['img/items/geopack.png', 'Геолог', 'Применить 100 геопаков', 1],
    ['img/crystals/green.png', 'Миллионер', 'Добыть миллион кристаллов', 1], ['img/cells/Road.png', 'Дорожник', 'Построить 5 000 дорог', .64],
    ['img/cells/Support.png', 'Свод держится', 'Поставить 1 000 опор', .31], ['img/items/eMI.png', 'Тишина', 'Применить ЭМИ', 1],
    ['img/crystals/white.png', 'Белая редкость', 'Добыть 100 белых кристаллов', .12], ['img/cells/BlackRock.png', 'На дне', 'Достичь глубины 5 000', .58],
  ];
  const myStats = ['312 ч', '1 284 530', '9 412 006', '48 211', '2 940', '6'];
  const PLAYERS = {
    [ME]: { id: 10482, clan: 'FED', since: '12.03.2026', level: 47, pct: 62, hidden: false, slots, skills, stats: myStats, ach: ACH.map(a => a[3]) },
    'ШАХТЁР-102': { id: 9921, clan: 'FED', since: '02.01.2026', level: 61, pct: 18, stats: ['540 ч', '2 911 004', '15 220 870', '120 455', '4 210', '11'], ach: [1, 1, 1, 1, .8, 1, .4, .84],
      slots: { 0: 'm', 1: 'd', 2: 'M', 3: 'l', 4: 'A', 5: 't', 6: 'F' },
      skills: { m: { ...skills.m, level: 22, exp: .3 }, d: { ...skills.d, level: 25, exp: .6 }, M: { ...skills.M, level: 18, exp: .1 }, l: { ...skills.l, level: 20, exp: .9 }, A: { ...skills.A, level: 14 }, t: { ...skills.t, level: 16 }, F: { ...skills.F, level: 9 } } },
    'DVM_LEADER': { id: 101, clan: 'FED', since: '05.11.2025', level: 92, pct: 7, hidden: true },
    'ГЛУБИНА': { id: 13377, clan: 'FED', since: '20.05.2026', level: 55, pct: 40, hidden: true },
  };
  // Незнакомый ник — открытый профиль с небольшим набором навыков
  const playerOf = nick => PLAYERS[nick] ||= { id: 20000 + [...nick].reduce((a, ch) => a + ch.charCodeAt(0), 0) % 9000, clan: 'FED', since: '01.08.2026', level: 20 + nick.length * 3 % 40, pct: 50,
    slots: { 0: 'm', 1: 'd', 2: 'M', 3: 'l' }, skills: { m: { ...skills.m, level: 6 }, d: { ...skills.d, level: 9 }, M: { ...skills.M, level: 7 }, l: { ...skills.l, level: 5 } },
    stats: ['48 ч', '120 400', '611 900', '3 020', '880', '1'], ach: [1, .4, .2, .1, 0, 0, 0, .18] };
  let profileNick = ME, profileSel = 0, profileView = '';
  const STAT_NAMES = ['Время в игре', 'Выкопано блоков', 'Добыто кристаллов', 'Построено блоков', 'Глубина рекорд', 'Зданий'];
  function renderProfile() {
    const p = playerOf(profileNick), own = profileNick === ME, closed = p.hidden && !own, setup = own && profileView === 'settings';
    $('profileTitle').textContent = own ? 'Профиль' : 'Профиль игрока';
    $('profileHeadTag').textContent = '#' + p.id;
    const head = `<div class="profile-card">
        <div class="profile-robot"><img src="img/bee.png" alt=""></div>
        <div class="profile-who">
          <p class="profile-nick"><img src="img/clan.png" alt="" width="16" height="16">${profileNick}</p>
          <p class="profile-meta"><button type="button" class="pid" id="profileCopy" title="Скопировать ID"><span>#${p.id}</span></button><i class="idcode" aria-hidden="true">${window.kernBars(String(p.id))}</i><span>Клан <b>${p.clan}</b></span>${closed ? '' : `<span>В игре с ${p.since}</span>`}</p>
          <div class="profile-level"><span>Уровень <b>${p.level}</b></span>${closed ? '' : `<div class="meter"><i style="width:${p.pct}%"></i></div><small>${p.pct}%</small>`}</div>
        </div>
        <div class="profile-actions">${own
          ? (setup ? `<button type="button" class="btn" id="profileSettings"><svg class="btn-ico"><use href="#i-prev"/></svg>К профилю</button>`
                   : `<button type="button" class="btn" id="profileSettings"><svg class="btn-ico"><use href="#i-settings"/></svg>Настройки профиля</button>`)
          : '<button type="button" class="btn btn--primary" id="profileWrite">Написать</button>'}</div>
      </div>`;
    // Настройки профиля — данные сервера, а не настройки клиента: живут в самом профиле
    if (setup) {
      $('profileBody').innerHTML = head + `<section class="profile-sec"><h3><b class="sec-plate">Настройки профиля</b></h3>
        <label class="field"><span><b>Скрыть профиль</b><small>Другие игроки увидят только ник, клан, ID и уровень</small></span><input type="checkbox" class="switch" id="profileHide"${p.hidden ? ' checked' : ''}></label></section>`;
      $('profileHide').onchange = e => { p.hidden = e.target.checked; };
    } else if (closed) {
      $('profileBody').innerHTML = head + '<p class="profile-closed"><svg><use href="#i-lock"/></svg>Навыки, статистика и достижения скрыты</p>';
    } else {
      const done = p.ach.filter(v => v >= 1).length;
      $('profileBody').innerHTML = head + `
        <div class="profile-grid">
          <section class="profile-sec profile-sec--skills"><h3><b class="sec-plate">Навыки</b><span id="profileSkillsInfo"></span></h3>
            <div class="profile-skills"><div class="robot robot--profile" id="profileRobot"><svg class="robot-art" aria-hidden="true"><use href="#robot-up"/></svg></div></div></section>
          <div class="profile-side">
            <aside class="skill-info" id="profileSkill"></aside>
            <section class="profile-sec"><h3><b class="sec-plate">Статистика</b></h3>
              <dl class="profile-stats">${STAT_NAMES.map((n, i) => `<div><dt>${n}</dt><dd>${p.stats[i]}</dd></div>`).join('')}</dl></section>
          </div>
        </div>
        <section class="profile-sec"><h3><b class="sec-plate">Достижения</b><span>${done} из ${ACH.length}</span></h3>
          <ul class="profile-ach">${ACH.map(([img, name, what], i) => { const v = p.ach[i] || 0;
            return `<li class="${v >= 1 ? 'is-done' : ''}" title="${what}"><img src="${img}" alt=""><b>${name}</b><small>${what}</small>${v >= 1 ? '' : `<div class="meter"><i style="width:${Math.round(v * 100)}%"></i></div>`}</li>`; }).join('')}</ul></section>`;
      renderProfileSkills(p);
    }
    $('profileCopy').onclick = () => { navigator.clipboard?.writeText(String(p.id)).catch(() => {}); const t = $('profileCopy').firstElementChild; t.textContent = 'Скопировано'; setTimeout(() => { t.textContent = '#' + p.id; }, 1200); };
    if ($('profileSettings')) $('profileSettings').onclick = () => { profileView = setup ? '' : 'settings'; renderProfile(); };
    if ($('profileWrite')) $('profileWrite').onclick = () => { $('profileDialog').hidden = true; window.openPM(profileNick); };
  }
  function renderProfileSkills(p) {
    const amount = p === PLAYERS[ME] ? SLOT_AMOUNT + extraSlots : SLOT_AMOUNT;
    paintRobot($('profileRobot'), amount, profileSel, i => { profileSel = i; renderProfileSkills(p); }, p.slots, p.skills);
    const code = p.slots[profileSel], k = p.skills[code];
    $('profileSkillsInfo').textContent = `${Object.keys(p.slots).length} из ${amount} слотов`;
    $('profileSkill').innerHTML = code
      ? `<i class="big sk sk-${code}"></i><h3>${k.name}</h3><p class="skill-desc">${describe(code, p.skills)}</p>`
      : '<p class="skill-desc">Пустой слот</p>';
  }
  window.openProfile = (nick = ME, view = '') => { profileNick = nick; profileSel = 0; profileView = view; $('profileDialog').hidden = false; renderProfile(); $('profileBody').scrollTop = 0; };
  window.openSkills = (slot = -1) => { selected = slot; renderSkills(); $('skillsDialog').hidden = false; };
  $('openSkills').onclick = () => { show('game'); window.openSkills(-1); };
  $('skillsDialog').querySelector('[data-close]').onclick = () => { $('skillsDialog').hidden = true; };
  document.addEventListener('keydown', e => { if (e.key === 'Escape') { $('skillsDialog').hidden = true; $('updateDialog').hidden = true; healthDialog.hidden = true; } });

  // Адрес: ?screen=menu&update=1&updwin=1&skills=2&onbstep=1
  renderStep();
  // Сцена стенда (js/scene.js): экраны — при разборе страницы, окно навыков — когда мир готов
  window.kernScene.onReset(() => {
    setOutdated(false); step = 0; renderStep();
    $('authTabs').querySelector('[data-tab=login]').click();
    ['authForm', 'regForm'].forEach(id => $(id).reset());
    [...$('authForm').elements, ...$('regForm').elements].filter(f => f.closest('.fld')?.querySelector('.fld-err')).forEach(f => fieldError(f, ''));
  });
  window.kernScene.on(0, q => {
    if (q.get('update') === '1') setOutdated(true);
    if (q.get('onbstep')) { step = Number(q.get('onbstep')); renderStep(); }
    if (q.get('auth') === 'register') $('authTabs').querySelector('[data-tab=register]').click();
    if (q.get('forgot')) $('authForgot').click();                     // ?forgot=1 — снимок подсказки о пароле
    if (q.get('autherr')) ($('regForm').classList.contains('is-off') ? $('authForm') : $('regForm')).requestSubmit(); // ?autherr=1 — снимок ошибок полей
    show(q.get('screen') || 'game');
    if (q.get('updwin')) openUpdate();
    if (q.get('health')) {
      healthDialog.hidden = false;
      if (q.get('health') === 'run') $('healthCheck').click();
    }
  });
  window.kernScene.on(1, q => { if (q.get('skills')) window.openSkills(Number(q.get('skills'))); });
})();
