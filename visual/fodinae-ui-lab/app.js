
/* ----------------------------------------------------
   Тосты — короткий ответ системы

   Заменили 8 вызовов alert(). alert() — окно операционной системы: его
   нельзя оформить, нельзя перенести в UI Toolkit (там его нет вовсе) и он
   останавливает страницу целиком. Восемь действий получали ответ, которого
   у клиента быть не может.

   Тон и голос задаются ОДНИМ вызовом, поэтому вид и звук ответа не могут
   разойтись — тот же приём, что у data-sfx.
   ---------------------------------------------------- */

/* Текст тоста — по ключу, как весь остальной текст.

   Дыра, которую я сам и проделал: заменяя alert(), вписал восемь русских
   строк прямо в код — через час после того, как закрыл ровно эту дыру в
   разметке. Проверка «текст без ключа» смотрела только в index.html, и
   строка в JS не выглядела как интерфейс. Теперь смотрит и сюда.

   {0} в значении подставляется позиционно — так же, как string.Format в
   LocalizationService игры. Порядок подстановок переводчик менять вправе:
   в другом языке дополнение может стоять перед подлежащим. */
function t(key, ...args) {
  const dict = window.i18nProbe && window.i18nProbe.DICT;
  let value = key;
  if (dict) {
    const found = dict.game[key] !== undefined ? dict.game[key] : dict.mirror[key];
    if (found !== undefined) value = found;
  }
  return value.replace(/\{(\d+)\}/g, (m, i) => (args[i] !== undefined ? args[i] : m));
}

const TOAST_TONE = {
  ok:    { mark: '✓', voice: 'confirm' },
  alert: { mark: '!', voice: 'alert' },
  info:  { mark: '·', voice: 'click' },
};

function showToast(text, tone = 'info', ms = 3600) {
  const layer = document.getElementById('toastLayer');
  if (!layer) return;
  const spec = TOAST_TONE[tone] || TOAST_TONE.info;

  const el = document.createElement('div');
  el.className = `toast toast--${tone} toast--enter`;
  const mark = document.createElement('span');
  mark.className = 'toast-mark';
  mark.textContent = spec.mark;
  // Значок — обозначение состояния, а не текст: переводу не подлежит.
  mark.setAttribute('translate', 'no');
  const body = document.createElement('span');
  // Тост — коробка, которой разрешено расти вниз: текст ответа обязан
  // читаться целиком, а обрезать его многоточием некуда — полного текста
  // больше нигде нет. Объявление обязательно: необъявленных узлов в системе
  // не бывает (см. css/text.css).
  body.dataset.fit = 'wrap';
  body.textContent = text;
  el.append(mark, body);
  layer.appendChild(el);

  // Стартовое состояние нужно ЗАФИКСИРОВАТЬ до снятия класса, иначе браузер
  // увидит только конечные значения и покажет тост рывком. Раньше здесь был
  // requestAnimationFrame — и тост не появлялся вовсе, если вкладка в фоне:
  // фоновым вкладкам браузер останавливает кадры, и класс не снимался никогда.
  // Чтение offsetWidth заставляет пересчитать раскладку немедленно и от кадров
  // не зависит. В UI Toolkit кадров тоже нет — там это schedule, — так что
  // отвязка от rAF заодно приближает поведение к переносимому.
  void el.offsetWidth;
  el.classList.remove('toast--enter');
  playSound(spec.voice);

  setTimeout(() => {
    el.classList.add('toast--enter');
    el.addEventListener('transitionend', () => el.remove(), { once: true });
  }, ms);
  return el;
}

/* ============================================================================
   ДЕТЕРМИНИРОВАННАЯ СЛУЧАЙНОСТЬ
   ============================================================================

   Сетка шахты раскрашивается случайно, и это правильно: макет показывает
   породу, а не одну конкретную раскладку. Но пока источник случайности —
   Math.random(), две загрузки страницы дают РАЗНЫЕ цвета у 267 плиток, и
   значит никакая проверка вида через перезагрузку невозможна: любое сравнение
   тонет в шуме, который не имеет отношения к правке.

   Измерено: полный откат всех изменений давал 305 расхождений вычисленных
   стилей против снимка до них — при нулевом расхождении между двумя снимками
   внутри одной загрузки. То есть шумел не измеритель, а страница.

   Отсюда: своё зерно вместо Math.random(). Раскладка остаётся произвольной на
   вид, но воспроизводимой. Зерно меняется вызовом reseed() — тогда видно
   другую породу, когда это нужно глазу, а не проверке.

   Алгоритм — mulberry32: тридцать символов, равномерное распределение,
   не требует библиотеки. Криптостойкость здесь не нужна и не заявляется.
   ============================================================================ */
let __seed = 20260830;

function rand() {
  __seed |= 0;
  __seed = (__seed + 0x6D2B79F5) | 0;
  let t = Math.imul(__seed ^ (__seed >>> 15), 1 | __seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

function reseed(seed) {
  __seed = seed === undefined ? (Date.now() | 0) : seed;
  if (typeof renderMineGrid === 'function') renderMineGrid();
}

// ----------------------------------------------------
// FODINAE — Interactive Game Prototype & State Machine
// ----------------------------------------------------

let audioContext = null;
let isSfxOn = true;
let isGameUpdated = false;

// Состояние игрока и экспедиции
/* ----------------------------------------------------------------------
   ГЕОМЕТРИЯ ПО ДАННЫМ

   Семь чисел жили в разметке как style="width:71%". Это единственный вид
   инлайна, который системой допущен: значение считает программа. Но пока
   оно набрано в разметке РУКАМИ, оно не считается — оно дублируется, и
   дубль расходится. Так и вышло: полоса переподключения показывала 35%
   при подписи «ПОПЫТКА 04 / 10», то есть 40%. Расхождение прожило до тех
   пор, пока числа не свели к одному источнику.

   Точки радара — тоже данные: положение объекта в секторе. Заглушка, но
   честная, по образцу DummyConnection.
   ---------------------------------------------------------------------- */

const networkState = { attempt: 4, attemptLimit: 10 };

const radarContacts = [
  { top: 35, left: 42, tone: null },
  { top: 60, left: 70, tone: 'danger' },
  { top: 50, left: 50, tone: 'neutral' },
];

function renderDataGeometry() {
  const pct = (a, b) => `${Math.round((a / b) * 100)}%`;

  const mission = document.getElementById('missionFill');
  if (mission) mission.style.width = pct(playerState.missionBlocks, playerState.missionTarget);

  const retry = document.getElementById('reconnectFill');
  if (retry) retry.style.width = pct(networkState.attempt, networkState.attemptLimit);

  document.querySelectorAll('.radar-dot').forEach((dot, i) => {
    const c = radarContacts[i];
    if (!c) return;
    dot.style.top = c.top + '%';
    dot.style.left = c.left + '%';
  });
}

const playerState = {
  nickname: 'ШАХТЁР-774 [DVM]',
  token: 'fdn_tok_9948a204e18',
  hp: 840,
  maxHp: 1000,
  energy: 92,
  money: 14850,
  crystals: 340,
  basketCount: 12,
  basketMax: 25,
  missionBlocks: 142,
  missionTarget: 200,
  isAutoDig: false,
  isAggression: false,
  activeHotbarIndex: 0,
  activeProgCommand: '⬇ СКАН',
  isProgRunning: false,
  progInterval: null
};

// ----------------------------------------------------
// Синтез звука (Web Audio API)
// ----------------------------------------------------
function initAudio() {
  if (!audioContext) {
    audioContext = new (window.AudioContext || window.webkitAudioContext)();
  }
}

function toggleAudio() {
  isSfxOn = !isSfxOn;
  const el = document.getElementById('sfxStatus');
  // innerText здесь стирал <svg> из разметки и подставлял эмодзи — то есть
  // код молча отменял решение, ради которого спрайт и заводили.
  if (el) {
    el.innerHTML = '<svg class="fdn-icon" viewBox="0 0 24 24" aria-hidden="true">'
      + '<use href="#i-scan"/></svg> ' + (isSfxOn ? 'ЗВУК' : 'ВЫКЛ');
  }
  playSound('click');
}

function playSound(type) {
  if (!isSfxOn) return;
  initAudio();
  if (!audioContext) return;

  const now = audioContext.currentTime;
  const osc = audioContext.createOscillator();
  const gain = audioContext.createGain();

  osc.connect(gain);
  gain.connect(audioContext.destination);

  if (type === 'hover') {
    osc.type = 'sine';
    osc.frequency.setValueAtTime(480, now);
    osc.frequency.exponentialRampToValueAtTime(720, now + 0.035);
    gain.gain.setValueAtTime(0.03, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.035);
    osc.start(now);
    osc.stop(now + 0.035);
  } else if (type === 'click') {
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(320, now);
    osc.frequency.exponentialRampToValueAtTime(140, now + 0.07);
    gain.gain.setValueAtTime(0.12, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.07);
    osc.start(now);
    osc.stop(now + 0.07);
  } else if (type === 'confirm') {
    osc.type = 'sine';
    osc.frequency.setValueAtTime(380, now);
    osc.frequency.setValueAtTime(570, now + 0.06);
    gain.gain.setValueAtTime(0.14, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.2);
    osc.start(now);
    osc.stop(now + 0.2);
  } else if (type === 'alert') {
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(220, now);
    osc.frequency.setValueAtTime(180, now + 0.1);
    gain.gain.setValueAtTime(0.16, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.25);
    osc.start(now);
    osc.stop(now + 0.25);
  } else if (type === 'drill') {
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(140, now);
    osc.frequency.linearRampToValueAtTime(280, now + 0.06);
    osc.frequency.linearRampToValueAtTime(80, now + 0.12);
    gain.gain.setValueAtTime(0.12, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.12);
    osc.start(now);
    osc.stop(now + 0.12);
  }
}

// ----------------------------------------------------
// Живой тикер хроники экспедиции
// ----------------------------------------------------
const newsTickerItems = [
  "В секторе Hades-Alpha активирован глубинный бур -2 480 м // Кластер стабилен",
  "Сектор Tartarus-02: Зафиксирован выброс лавы на горизонте -1 920 м. Повышенная опасность",
  "Турнир 'Гонка буровых установок' стартует через 4 дня // Призовой фонд 100 000 CR",
  "Рудный пласт 'Красноскал': Зафиксированы залежи титана на глубине 3 000 м",
  "Сетевой протокол MinesProtocol v7 готов к развертыванию на всех узлах"
];
let tickerIndex = 0;
setInterval(() => {
  tickerIndex = (tickerIndex + 1) % newsTickerItems.length;
  const t = document.getElementById('tickerText');
  if (t) t.innerText = newsTickerItems[tickerIndex];
}, 6000);

// ----------------------------------------------------
// Машина состояний (State Machine)
// ----------------------------------------------------
let currentMode = 'menu';
let descentInterval = null;

function switchViewState(state) {
  currentMode = state;

  // Подсветка dev-кнопок идёт по data-state, а не по разбору русского текста
  // кнопки: подпись — это контент, а не идентификатор.
  document.querySelectorAll('.dev-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.state === state);
  });

  const viewport = document.getElementById('appViewport');
  const views = {
    auth: 'authView',
    onboarding: 'onboardingView',
    descent: 'descentView',
    loading: 'descentView',
    ingame: 'ingameView',
    pause: 'pauseView',
    reconnect: 'reconnectView',
  };

  document
    .querySelectorAll('.auth-overlay-view, .onboarding-overlay-view, .descent-overlay-view, .ingame-viewport, .pause-overlay-view, .reconnect-overlay-view')
    .forEach(v => v.classList.remove('active'));

  // Видимость хрома (шапка, рейл, футер) и колонки меню задаётся правилами
  // [data-state] в CSS. Раньше здесь проставлялся style.display, который
  // намертво перебивал любое правило таблицы стилей.
  viewport.dataset.state = state;

  const routes = { menu: 'routeOrbit', loading: 'routeDescent', descent: 'routeDescent', ingame: 'routeSurface' };
  ['routeOrbit', 'routeDescent', 'routeSurface'].forEach(id => {
    const el = document.getElementById(id);
    if (el) el.classList.toggle('active', routes[state] === id);
  });

  const viewId = views[state];
  if (viewId) {
    const el = document.getElementById(viewId);
    if (el) el.classList.add('active');
  }

  // Пауза рисуется поверх живой игровой сцены.
  if (state === 'pause') {
    const ingame = document.getElementById('ingameView');
    if (ingame) ingame.classList.add('active');
  }

  if (state === 'reconnect') startReconnectCount();
}

// ----------------------------------------------------
// Заставка и Авторизация (Логин / Регистрация)
// ----------------------------------------------------
let currentAuthTab = 'login';

function startExperienceFromSplash() {
  playSound('confirm');
  switchViewState('auth');
}

function switchAuthTab(tab) {
  currentAuthTab = tab;
  const tabLogin = document.getElementById('authTabLogin');
  const tabRegister = document.getElementById('authTabRegister');
  const loginForm = document.getElementById('authLoginForm');
  const regForm = document.getElementById('authRegisterForm');
  const btnSubmit = document.getElementById('btnSubmitAuth');

  if (tab === 'login') {
    tabLogin?.classList.add('active');
    tabRegister?.classList.remove('active');
    loginForm?.classList.remove('is-hidden');
    regForm?.classList.add('is-hidden');
    if (btnSubmit) btnSubmit.innerHTML = '<span>ВОЙТИ В МИР FODINAE</span><span>↗</span>';
  } else {
    tabLogin?.classList.remove('active');
    tabRegister?.classList.add('active');
    loginForm?.classList.add('is-hidden');
    regForm?.classList.remove('is-hidden');
    if (btnSubmit) btnSubmit.innerHTML = '<span>ЗАРЕГИСТРИРОВАТЬСЯ В ЭКСПЕДИЦИИ</span><span>↗</span>';
  }
}

function togglePasswordVisibility(fieldId) {
  const field = document.getElementById(fieldId);
  if (!field) return;
  field.type = field.type === 'password' ? 'text' : 'password';
}

// Уникальный генератор позывного на основе Seed (Время + Хэш оборудования)
const CALLSIGN_ROLES = [
  'ШАХТЁР', 'БУРОВИК', 'ОПЕРАТОР', 'ПРОХОДЧИК',
  'ИНЖЕНЕР', 'СКАУТ', 'СТАЛКЕР', 'МИНЕР', 'КИБЕР'
];

const CALLSIGN_CLANS = [
  '[DVM]', '[VOID]', '[CORE]', '[HADES]', '[TITAN]', '[APEX]', '[NEO]'
];

function generateSeededCallsign() {
  playSound('click');

  // Вычисляем сид на основе времени и характеристик окружения (эмуляция MAC/Device UUID)
  const timeSeed = Date.now();
  const perfSeed = Math.floor(performance.now() * 1000);
  const combinedSeed = (timeSeed ^ perfSeed) >>> 0;

  // Псевдослучайный генератор с сидом
  const roleIndex = combinedSeed % CALLSIGN_ROLES.length;
  const clanIndex = (combinedSeed >>> 4) % CALLSIGN_CLANS.length;
  const uniqueNumber = 100 + ((combinedSeed >>> 8) % 900); // 100..999

  const callsign = `${CALLSIGN_ROLES[roleIndex]}-${uniqueNumber} ${CALLSIGN_CLANS[clanIndex]}`;
  const inputLogin = document.getElementById('inputMinerName');
  const inputReg = document.getElementById('inputRegMinerName');
  if (inputLogin) inputLogin.value = callsign;
  if (inputReg) inputReg.value = callsign;
  return callsign;
}

function submitAuthForm() {
  let nick = '';
  let pass = '';

  if (currentAuthTab === 'login') {
    nick = document.getElementById('inputMinerName')?.value.trim() || 'ШАХТЁР-774 [DVM]';
    pass = document.getElementById('inputPassword')?.value || '';
  } else {
    nick = document.getElementById('inputRegMinerName')?.value.trim() || 'ШАХТЁР-774 [DVM]';
    pass = document.getElementById('inputRegPassword')?.value || '';
    const confirmPass = document.getElementById('inputRegConfirmPassword')?.value || '';
    const acceptEula = document.getElementById('authAcceptEula')?.checked;

    if (!acceptEula) {
      showToast(t('gateway.toast.eula_required'), 'alert');
      return;
    }
    if (pass !== confirmPass) {
      showToast(t('gateway.toast.password_mismatch'), 'alert');
      return;
    }
  }

  playerState.nickname = nick;
  // Генерируем сессионный токен из логина и пароля
  playerState.token = `fdn_tok_${Math.abs(nick.split('').reduce((a, b) => { a = ((a << 5) - a) + b.charCodeAt(0); return a & a; }, 0)).toString(16)}`;

  // Обновляем плашку в шапке (User Pill)
  const userPillNick = document.querySelector('.user-pill div div:first-child');
  if (userPillNick) userPillNick.innerText = nick;

  // Обновляем профиль
  const profileNick = document.querySelector('#profileModal h3');
  if (profileNick) profileNick.innerText = nick.split(' ')[0];
  const profileToken = document.getElementById('profileTokenInput');
  if (profileToken) profileToken.value = playerState.token;

  const hudNick = document.getElementById('hudMinerNick');
  if (hudNick) hudNick.innerText = nick.split(' ')[0];

  // Переходим на экран Онбординга (Калибровка оборудования)
  switchViewState('onboarding');
}

function switchAccountFromProfile() {
  closeModal('profileModal');
  switchViewState('auth');
}

function copyTokenToClipboard() {
  const tok = document.getElementById('profileTokenInput')?.value || playerState.token;
  navigator.clipboard.writeText(tok).then(() => {
    showToast(t('gateway.toast.token_copied', tok), 'ok');
  }).catch(() => {
    showToast(t('gateway.toast.token_shown', tok), 'info');
  });
}

// ----------------------------------------------------
// Онбординг
// ----------------------------------------------------
let currentObStep = 1;

function updateOnboardingStepUI() {
  for (let i = 1; i <= 3; i++) {
    const pill = document.getElementById(`obStepPill${i}`);
    const content = document.getElementById(`obStep${i}`);
    if (pill) {
      pill.className = `onboarding-step-pill ${i === currentObStep ? 'active' : (i < currentObStep ? 'completed' : '')}`;
    }
    if (content) {
      content.className = `onboarding-step-content ${i === currentObStep ? 'active' : ''}`;
    }
  }

  const prevBtn = document.getElementById('btnObPrev');
  const nextBtn = document.getElementById('btnObNext');
  const title = document.getElementById('onboardingTitle');

  prevBtn?.classList.toggle('is-hidden', currentObStep <= 1);

  if (currentObStep === 1) {
    if (title) title.innerText = 'Шаг 1: Доступность и визуальный комфорт';
    if (nextBtn) nextBtn.innerText = 'ДАЛЕЕ (ГРАФИКА) →';
  } else if (currentObStep === 2) {
    if (title) title.innerText = 'Шаг 2: Графика и освещение';
    if (nextBtn) nextBtn.innerText = 'ДАЛЕЕ (УПРАВЛЕНИЕ) →';
  } else if (currentObStep === 3) {
    if (title) title.innerText = 'Шаг 3: Тактильный контроль и звук';
    if (nextBtn) nextBtn.innerText = 'ЗАВЕРШИТЬ КАЛИБРОВКУ (В ОРБИТУ) ↗';
  }
}

function nextOnboardingStep() {
  if (currentObStep < 3) {
    currentObStep++;
    updateOnboardingStepUI();
  } else {
    playSound('confirm');
    switchViewState('menu');
  }
}

function prevOnboardingStep() {
  if (currentObStep > 1) {
    currentObStep--;
    updateOnboardingStepUI();
  }
}

function applyUiScale(val) {
  document.documentElement.style.setProperty('--ui-scale', val);
}

function applyColorblindTheme(val) {
  document.body.classList.remove('theme-deuteranopia', 'theme-protanopia', 'theme-tritanopia', 'theme-high-contrast');
  if (val !== 'none') {
    document.body.classList.add(`theme-${val}`);
  }
}

/* Канонический контракт — атрибут на <html> (css/tokens.css §3, css/base.css).
   Раньше здесь вешался класс .reduce-motion на <body>, которого не ловил никто:
   тумблер в настройках не делал ничего. Три записи контракта сведены к одной. */
function toggleReduceMotion(enabled) {
  document.documentElement.setAttribute('data-reduce-motion', enabled ? 'true' : 'false');
}

/* Системная настройка гасит шкалу длительностей (tokens.css §3), пользовательский
   тумблер останавливает бесконечные циклы (base.css) — это разные средства, и
   поэтому оба нужны. Но стартовать тумблер обязан из системного значения, иначе
   при включённой настройке ОС он показывает «выключено» и на вид ничего не делает. */
function initReduceMotion() {
  const select = document.getElementById('reduceMotionSelect');
  const system = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  document.documentElement.setAttribute('data-reduce-motion', system ? 'true' : 'false');
  if (select) {
    select.value = system ? 'yes' : 'no';
  }
}

// ----------------------------------------------------
// Спуск в шахту
// ----------------------------------------------------
function handleDeployClick() {
  if (!isGameUpdated) {
    openMandatoryUpdateModal();
  } else {
    startDescentSequence();
  }
}

function startDescentSequence() {
  switchViewState('descent');
  closeModal('serverBrowserModal');

  let p = 0;
  const fill = document.getElementById('descentProgressFill');
  const metric = document.getElementById('descentSpeedMetric');
  const tag = document.getElementById('descentPhaseNum');
  const label = document.getElementById('descentAssetLabel');

  if (descentInterval) clearInterval(descentInterval);

  const phases = [
    { pct: 15, tag: 'ФАЗА СПУСКА 01 / 05', label: 'Авторизация и валидация токена шахтёра...', id: 'dp-1' },
    { pct: 35, tag: 'ФАЗА СПУСКА 02 / 05', label: 'Потоковая передача World Manifest (Регион 32x32)...', id: 'dp-2' },
    { pct: 70, tag: 'ФАЗА СПУСКА 03 / 05', label: 'ClientAssetLoader: Скачивание текстур пород и FMOD банков...', id: 'dp-3' },
    { pct: 90, tag: 'ФАЗА СПУСКА 04 / 05', label: 'SingleMeshTerrainRenderer: Компиляция меша и UV атласа...', id: 'dp-4' },
    { pct: 100, tag: 'ФАЗА СПУСКА 05 / 05', label: 'Синхронизация позиции шахтёра на горизонте высадки...', id: 'dp-5' }
  ];

  descentInterval = setInterval(() => {
    p += 1.8;
    if (p >= 100) {
      p = 100;
      clearInterval(descentInterval);
      playSound('confirm');
      label.innerText = 'ГОТОВО! Вход в шахту выполнен.';
      setTimeout(() => switchViewState('ingame'), 300);
    }

    fill.style.width = p + '%';
    const currentMB = Math.round((p / 100) * 421);
    metric.innerText = `${currentMB} / 421 МБ (26.4 МБ/с)`;

    for (let ph of phases) {
      if (p >= ph.pct) {
        tag.innerText = ph.tag;
        label.innerText = ph.label;
        document.querySelectorAll('.phase-step').forEach(el => el.className = 'phase-step done');
        const target = document.getElementById(ph.id);
        if (target) target.className = 'phase-step current';
      }
    }
  }, 45);
}

function cancelDescentSequence() {
  if (descentInterval) clearInterval(descentInterval);
  switchViewState('menu');
}

// ----------------------------------------------------
// 2D Шахта и Игровой HUD
// ----------------------------------------------------
function initMineStrataGrid() {
  const container = document.getElementById('mineStrataGrid');
  if (!container) return;

  container.innerHTML = '';
  const cols = 24;
  const rows = 14;

  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const tile = document.createElement('div');
      tile.className = 'mine-tile';
      tile.dataset.sfx = 'drill';

      const roll = rand();
      if (r < 3) {
        tile.classList.add('basalt');
        if (roll > 0.85) tile.classList.add('ore-titanium');
      } else if (r < 9) {
        tile.classList.add('redrock');
        if (roll > 0.82) tile.classList.add('ore-titanium');
        else if (roll > 0.74) tile.classList.add('ore-gold');
      } else {
        tile.classList.add('redrock');
        if (roll > 0.88) tile.classList.add('lava-crack');
        else if (roll > 0.76) tile.classList.add('ore-gold');
      }

      if ((r === 6 || r === 7) && (c === 11 || c === 12)) {
        tile.className = 'mine-tile mined-empty';
        delete tile.dataset.sfx;
      }

      tile.addEventListener('click', () => mineTileBlock(tile));
      container.appendChild(tile);
    }
  }
}

function mineTileBlock(tile) {
  if (tile.classList.contains('mined-empty')) return;

  let gainedOre = null;
  if (tile.classList.contains('ore-titanium')) gainedOre = 'Титан';
  else if (tile.classList.contains('ore-gold')) gainedOre = 'Золото';
  else if (tile.classList.contains('lava-crack')) {
    playSound('alert');
    simulateDamage(40);
  }

  tile.className = 'mine-tile mined-empty';
  delete tile.dataset.sfx;

  if (playerState.missionBlocks < playerState.missionTarget) {
    playerState.missionBlocks++;
    const pct = Math.round((playerState.missionBlocks / playerState.missionTarget) * 100);
    const mFill = document.getElementById('missionFill');
    const mMetric = document.getElementById('missionMetric');
    if (mFill) mFill.style.width = pct + '%';
    if (mMetric) mMetric.innerText = `${playerState.missionBlocks} / ${playerState.missionTarget} блоков (${pct}%)`;
  }

  if (gainedOre && playerState.basketCount < playerState.basketMax) {
    playerState.basketCount++;
    updateBasketUI();
  }
}

function updateBasketUI() {
  const badge = document.getElementById('basketCapBadge');
  if (badge) badge.innerText = `${playerState.basketCount} / ${playerState.basketMax}`;
}

function toggleAutoDig() {
  playerState.isAutoDig = !playerState.isAutoDig;
  const btn = document.getElementById('btnAutoDig');
  const led = document.getElementById('ledAutoDig');
  if (btn && led) {
    if (playerState.isAutoDig) {
      btn.classList.add('active');
      led.classList.add('active');
      autoDigLoop();
    } else {
      btn.classList.remove('active');
      led.classList.remove('active');
    }
  }
}

function autoDigLoop() {
  if (!playerState.isAutoDig) return;
  const tiles = Array.from(document.querySelectorAll('.mine-tile:not(.mined-empty)'));
  if (tiles.length > 0) {
    const target = tiles[Math.floor(rand() * tiles.length)];
    mineTileBlock(target);
  }
  if (playerState.isAutoDig) setTimeout(autoDigLoop, 550);
}

function toggleAggression() {
  playerState.isAggression = !playerState.isAggression;
  const btn = document.getElementById('btnAggression');
  const led = document.getElementById('ledAggression');
  if (btn && led) {
    if (playerState.isAggression) {
      btn.classList.add('active');
      led.classList.add('alert');
    } else {
      btn.classList.remove('active');
      led.classList.remove('alert');
    }
  }
}

function selectHotbarSlot(idx) {
  playerState.activeHotbarIndex = idx;
  document.querySelectorAll('#hotbarSlotsWrap .hotbar-slot').forEach((slot, i) => {
    if (i === idx) slot.classList.add('active');
    else slot.classList.remove('active');
  });
}

function simulateDamageOrHeal() {
  if (playerState.hp > 300) simulateDamage(150);
  else {
    playerState.hp = 1000;
    playSound('confirm');
    updateHpUI();
  }
}

function simulateDamage(amt) {
  playerState.hp = Math.max(0, playerState.hp - amt);
  playSound('alert');
  updateHpUI();
}

function updateHpUI() {
  const hpFill = document.getElementById('hpFill');
  const hpText = document.getElementById('hpText');
  const pct = Math.round((playerState.hp / playerState.maxHp) * 100);
  if (hpFill) hpFill.style.width = pct + '%';
  if (hpText) hpText.innerText = `${playerState.hp} / ${playerState.maxHp}`;
}

function claimBonus() {
  playerState.money += 500;
  playerState.crystals += 25;
  document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  document.getElementById('hudCrystals').innerText = playerState.crystals;
}

// ----------------------------------------------------
// Инвентарь (9x6 = 54 слота)
// ----------------------------------------------------
const inventoryItems = [
  { name: 'Алмазный бур Tier-2', icon: '⛏', count: 1, rarity: 'legendary', desc: 'Усиленный бур со скоростью 0.3s. Режет базальт и красноскал.' },
  { name: 'Титановый слиток', icon: '◆', count: 18, rarity: 'rare', desc: 'Очищенный титан с глубин -2 400 м. Необходим для крафта дронов.' },
  { name: 'Самородок золота', icon: '★', count: 6, rarity: 'rare', desc: 'Высокопроводящий металл для схем программатора.' },
  { name: 'Кристалл кварца', icon: '⌬', count: 32, rarity: 'normal', desc: 'Базовый минерал верхних горизонтов.' },
  { name: 'Энергоячейка M1', icon: '#i-cell', count: 4, rarity: 'normal', desc: 'Восстанавливает 100% энергии реактора.' },
  { name: 'Гео-динамит T1', icon: '⚑', count: 8, rarity: 'rare', desc: 'Взрывчатка для направленной расчистки рудных пластов.' }
];

/* Значок предмета: геометрический глиф либо ссылка на спрайт (#i-*).

   Правило проекта записано словами в index.html: цветные эмодзи убраны,
   потому что рендерятся по-разному на разных платформах; моноширинные
   геометрические глифы оставлены намеренно — они наследуют currentColor.
   Записанное словами не соблюдалось: 🔋 пережил замену. Теперь у данных есть
   способ сослаться на спрайт, и повода тянуть эмодзи не осталось. */
function iconMarkup(icon) {
  return icon.startsWith('#')
    ? `<svg class="fdn-icon" viewBox="0 0 24 24" aria-hidden="true"><use href="${icon}"/></svg>`
    : icon;
}

function initFullInventoryGrid() {
  const container = document.getElementById('fullInventoryGrid');
  if (!container) return;

  container.innerHTML = '';
  const totalSlots = 54;

  for (let i = 0; i < totalSlots; i++) {
    const cell = document.createElement('div');
    cell.className = 'inv-grid-cell';
    cell.dataset.sfx = 'click';

    const item = inventoryItems[i];
    if (item) {
      cell.classList.add(item.rarity);
      cell.innerHTML = `
        <span class="inv-cell-icon">${iconMarkup(item.icon)}</span>
        <span class="inv-cell-count">${item.count}</span>
      `;
      cell.addEventListener('click', () => selectInventoryItem(item, cell));
    } else {
      cell.addEventListener('click', () => {
        playSound('click');
        document.querySelectorAll('.inv-grid-cell').forEach(c => c.classList.remove('selected'));
        cell.classList.add('selected');
      });
    }

    container.appendChild(cell);
  }
}

function selectInventoryItem(item, cell) {
  playSound('click');
  document.querySelectorAll('.inv-grid-cell').forEach(c => c.classList.remove('selected'));
  cell.classList.add('selected');

  const inspName = document.getElementById('inspName');
  const inspDesc = document.getElementById('inspDesc');
  if (inspName) inspName.innerText = item.name;
  if (inspDesc) inspDesc.innerText = item.desc;
}

function useCurrentItem() {
}

// ----------------------------------------------------
// Программатор (16x12)
// ----------------------------------------------------
function initProgrammatorGrid() {
  const container = document.getElementById('progGrid16x12');
  if (!container) return;

  container.innerHTML = '';
  const totalCells = 16 * 12;

  for (let i = 0; i < totalCells; i++) {
    const cell = document.createElement('div');
    cell.className = 'prog-grid-cell';
    cell.dataset.sfx = 'click';
    cell.innerText = '·';
    cell.addEventListener('click', () => {
      playSound('click');
      cell.innerText = playerState.activeProgCommand.split(' ')[0];
      cell.classList.add('prog-grid-cell--filled');
    });
    container.appendChild(cell);
  }
}

function selectProgCommand(cmd) {
  playerState.activeProgCommand = cmd;
}

function clearProgrammatorGrid() {
  document.querySelectorAll('.prog-grid-cell').forEach(cell => {
    cell.innerText = '·';
    cell.classList.remove('prog-grid-cell--filled', 'active-step');
  });
}

function runProgrammatorExec() {
  stopProgrammatorExec();
  playerState.isProgRunning = true;
  const status = document.getElementById('progStatus');
  if (status) {
    status.innerText = 'ВЫПОЛНЕНИЕ...';
    status.dataset.state = 'running';
  }

  const cells = Array.from(document.querySelectorAll('.prog-grid-cell'));
  let cur = 0;

  playerState.progInterval = setInterval(() => {
    cells.forEach(c => c.classList.remove('active-step'));
    if (cur < cells.length) {
      cells[cur].classList.add('active-step');
      playSound('hover');
      cur++;
    } else {
      stopProgrammatorExec();
    }
  }, 100);
}

function stopProgrammatorExec() {
  if (playerState.progInterval) clearInterval(playerState.progInterval);
  playerState.isProgRunning = false;
  const status = document.getElementById('progStatus');
  if (status) {
    status.innerText = 'ОСТАНОВЛЕН';
    status.dataset.state = 'stopped';
  }
  document.querySelectorAll('.prog-grid-cell').forEach(c => c.classList.remove('active-step'));
}

// ----------------------------------------------------
// Чат
// ----------------------------------------------------
function switchChatTab(btn, tab) {
  document.querySelectorAll('.chat-tab-btn').forEach(b => b.classList.remove('active'));
  btn.classList.add('active');
}

function handleChatInputKeyDown(e) {
  if (e.key === 'Enter') sendChatMessage();
}

function sendChatMessage() {
  const input = document.getElementById('chatInputField');
  if (!input) return;
  const msg = input.value.trim();
  if (!msg) return;

  playSound('click');
  const box = document.getElementById('chatMessagesBox');
  if (box) {
    const row = document.createElement('div');
    row.className = 'chat-row';
    row.innerHTML = `<span class="chat-author">[ВЫ]:</span> ${escapeHtml(msg)}`;
    box.appendChild(row);
    box.scrollTop = box.scrollHeight;
  }
  input.value = '';

  setTimeout(() => {
    if (box) {
      const reply = document.createElement('div');
      reply.className = 'chat-row';
      reply.innerHTML = `<span class="chat-author clan">[DVM_BOT]:</span> Принято: "${escapeHtml(msg)}". Координаты зафиксированы.`;
      box.appendChild(reply);
      box.scrollTop = box.scrollHeight;
      playSound('hover');
    }
  }, 700);
}

function escapeHtml(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// ----------------------------------------------------
// Торговец
// ----------------------------------------------------
function buyItem(name, cost) {
  if (playerState.money >= cost) {
    playerState.money -= cost;
    document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  } else {
    playSound('alert');
  }
}

function sellAllOre() {
  const gain = playerState.basketCount * 140;
  playerState.money += gain;
  playerState.basketCount = 0;
  playSound('confirm');
  document.getElementById('hudMoney').innerText = playerState.money.toLocaleString();
  updateBasketUI();
}

// ----------------------------------------------------
// Пауза и Реконнект
// ----------------------------------------------------
function resumeGameFromPause() {
  switchViewState('ingame');
}

let recTimer = null;
function startReconnectCount() {
  let sec = 5;
  const display = document.getElementById('reconnectTimer');
  if (recTimer) clearInterval(recTimer);
  recTimer = setInterval(() => {
    sec--;
    if (display) display.innerText = `00:0${sec}`;
    if (sec <= 0) {
      sec = 5;
      playSound('hover');
    }
  }, 1000);
}

function startOfflineDummy() {
  showToast(t('network.toast.dummy_transport'), 'info');
  switchViewState('ingame');
}

function confirmQuit() {
  if (confirm('Выйти из игры на рабочий стол?')) {
    showToast(t('mainmenu.toast.client_closed'), 'info');
  }
}

// ----------------------------------------------------
// Модальные окна
// ----------------------------------------------------
// Модальный слой. Одновременно открыта ровно одна модалка: раньше openModal
// просто добавлял класс, и любые два вызова подряд накладывали окна друг на
// друга. Плюс сохранение и возврат фокуса — без этого после закрытия окна
// фокус улетал в начало документа.
let lastFocusedBeforeModal = null;

function getOpenModal() {
  return document.querySelector('.modal-overlay.active');
}

function openModal(id) {
  const el = document.getElementById(id);
  if (!el) return;

  const current = getOpenModal();
  if (current && current !== el) current.classList.remove('active');
  else if (!current) lastFocusedBeforeModal = document.activeElement;

  el.classList.add('active');
  el.setAttribute('role', 'dialog');
  el.setAttribute('aria-modal', 'true');

  const focusable = el.querySelector(
    'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
  );
  if (focusable) focusable.focus();
}

function closeModal(id) {
  const el = id ? document.getElementById(id) : getOpenModal();
  if (!el) return;

  el.classList.remove('active');
  el.removeAttribute('aria-modal');

  if (lastFocusedBeforeModal && document.contains(lastFocusedBeforeModal)) {
    lastFocusedBeforeModal.focus();
  }
  lastFocusedBeforeModal = null;
}

// Клавиатурная активация для элементов, которые не являются <button>, но ведут
// себя как кнопки (строки таблицы серверов, карточки хроники, пилюли табов).
// Один делегированный обработчик вместо 24 инлайновых: любой новый элемент с
// role="button" получает поведение бесплатно.
document.addEventListener('keydown', event => {
  if (event.key !== 'Enter' && event.key !== ' ') return;

  const el = event.target;
  if (!el.hasAttribute || !el.hasAttribute('onclick')) return;
  if (el.tagName === 'BUTTON' || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') return;

  const role = el.getAttribute('role');
  if (role !== 'button' && role !== 'row') return;

  // Пробел на роли button прокручивает страницу — это надо погасить.
  event.preventDefault();
  el.click();
});

// Клик по затемнённому фону закрывает окно — стандартное поведение, которого
// в макете не было вовсе.
document.addEventListener('click', event => {
  if (event.target.classList && event.target.classList.contains('modal-overlay')) {
    closeModal(event.target.id);
  }
});

function openMandatoryUpdateModal() {
  openModal('mandatoryUpdateModal');
}

function switchSettingsTab(tabId, trigger) {
  const container = trigger.closest('.fdn-settings-layout');
  if (!container) return;
  container.querySelectorAll('.fdn-settings-tab').forEach(b => b.classList.remove('active'));
  container.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));

  trigger.classList.add('active');
  const target = document.getElementById(tabId);
  if (target) target.classList.add('active');
}

function selectServerDetail(row, name, region, online, ping, depth, seed, hazard) {
  row.parentElement.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
  row.classList.add('selected');

  document.getElementById('srvDetailName').innerText = name.toUpperCase();
  document.getElementById('srvDetailDepth').innerText = depth;
  document.getElementById('srvDetailSeed').innerText = seed;
  document.getElementById('srvDetailPing').innerText = ping;
  document.getElementById('srvDetailHazard').innerText = hazard;
}

function runRepairLog() {
  const log = document.getElementById('repairLogBox');
  if (!log) return;
  log.innerHTML = '<div class="term-row info">[00:00:01] Запуск глубокого сканирования кэша и диска...</div>';
  setTimeout(() => {
    log.innerHTML += '<div class="term-row ok">[00:00:02] Проверка CRC32 чанков карты: Без повреждений (4 096 / 4 096).</div>';
  }, 400);
  setTimeout(() => {
    log.innerHTML += '<div class="term-row ok">[00:00:03] FMOD Audio банки проверены: Синхронизированы с сервером.</div>';
  }, 800);
  setTimeout(() => {
    log.innerHTML += '<div class="term-row ok term-row--strong">[00:00:04] КЛИЕНТ ПОЛНОСТЬЮ ГОТОВ К РАБОТЕ.</div>';
    playSound('confirm');
  }, 1200);
}

function startUpdateDownloadProcess() {
  const box = document.getElementById('updateDownloadBlock');
  const bar = document.getElementById('updateProgressBar');
  const percent = document.getElementById('updatePercent');
  const btn = document.getElementById('btnStartUpdate');

  box?.classList.remove('is-hidden');
  if (btn) {
    btn.disabled = true;
    btn.innerText = 'СКАЧИВАНИЕ ПАТЧА...';
  }

  let p = 0;
  const interval = setInterval(() => {
    p += 2;
    if (bar) bar.style.width = p + '%';
    if (percent) percent.innerText = p + '%';
    if (p >= 100) {
      clearInterval(interval);
      playSound('confirm');
      isGameUpdated = true;
      if (btn) {
        btn.innerText = 'ПЕРЕЗАПУСТИТЬ КЛИЕНТ';
        btn.disabled = false;
        btn.onclick = () => {
          showToast(t('mainmenu.toast.updated'), 'ok');
          closeModal('mandatoryUpdateModal');
          const banner = document.getElementById('updateAlertBanner');
          banner?.classList.add('is-hidden');
          const fLink = document.getElementById('footerVersionStatus');
          if (fLink) {
            fLink.innerText = 'ВЕРСИЯ КЛИЕНТА 0.9.0 (АКТУАЛЬНА)';
            fLink.classList.remove('alert');
          }
        };
      }
    }
  }, 35);
}

// ----------------------------------------------------
// Клавиатурные шорткаты
// ----------------------------------------------------
window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    const activeModal = document.querySelector('.modal-overlay.active');
    if (activeModal) {
      activeModal.classList.remove('active');
    } else if (currentMode === 'ingame') {
      switchViewState('pause');
    } else if (currentMode === 'pause') {
      switchViewState('ingame');
    }
  } else if (e.key === 'Tab') {
    if (currentMode === 'ingame') {
      e.preventDefault();
      const invModal = document.getElementById('inventoryModal');
      if (invModal && invModal.classList.contains('active')) closeModal('inventoryModal');
      else openModal('inventoryModal');
    }
  } else if (e.key === 'e' || e.key === 'E' || e.key === 'у' || e.key === 'У') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) toggleAutoDig();
  } else if (e.key === 'l' || e.key === 'L' || e.key === 'д' || e.key === 'Д') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) toggleAggression();
  } else if (e.key === 'p' || e.key === 'P' || e.key === 'з' || e.key === 'З') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) openModal('programmatorModal');
  } else if (e.key === 'Enter') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) openModal('chatModal');
  } else if (e.key >= '1' && e.key <= '9') {
    if (currentMode === 'ingame' && !document.querySelector('.modal-overlay.active')) {
      selectHotbarSlot(parseInt(e.key, 10) - 1);
    }
  }
});


/* ----------------------------------------------------
   Причина и следствие: отклик на жест
   ----------------------------------------------------
   Раньше это был рукописный список из десяти селекторов, выполняемый один раз
   на DOMContentLoaded, плюс полсотни вызовов playSound, разбросанных по
   обработчикам. У такого устройства два изъяна, и оба уже сработали:

     • Список — вторая, ручная копия ответа на вопрос «что является
       интерактивом». Он молча расходился с разметкой: крестик модалки, строки
       списка серверов и ползунки были немы, хотя нажимаются.
     • Элементы, созданные позже (сетка шахты, инвентарь, программатор),
       слушателя не получали вовсе — их не существовало в момент привязки.

   Теперь причина объявлена там, где возникает: атрибут data-sfx на самом
   элементе. Его читают ОБА потребителя — этот слой и CSS в components.css,
   поэтому вид и звук состояния разойтись не могут. Слушателей два на весь
   документ, они делегированные, и им безразлично, когда элемент появился.

   Голос выбирается ролью действия, а не местом в коде:
     click    обычное действие
     confirm  принятие, после которого что-то произошло
     alert    потеря, отключение, разрушение
     drill    спуск в шахту — единственное событие своего рода
   Наведение озвучивается одинаково для всего, что вообще отзывается. */

function bindInteractionSound() {
  const voiceOf = target => {
    const el = target.closest?.('[data-sfx]');
    return el && !isInert(el) ? el.dataset.sfx : null;
  };

  document.addEventListener('mouseover', e => {
    const el = e.target.closest?.('[data-sfx]');
    if (!el || isInert(el)) return;
    // mouseover всплывает и внутри элемента; интересует только вход в него.
    if (e.relatedTarget && el.contains(e.relatedTarget)) return;
    playSound('hover');
  });

  document.addEventListener('click', e => {
    const voice = voiceOf(e.target);
    if (voice) playSound(voice);
  });
}

/** Выключенный элемент не отзывается ни видом, ни звуком. */
function isInert(el) {
  return el.disabled === true || el.getAttribute('aria-disabled') === 'true';
}

// ----------------------------------------------------
// Инициализация
// ----------------------------------------------------
document.addEventListener('DOMContentLoaded', () => {
  initReduceMotion();
  generateSeededCallsign();
  initMineStrataGrid();
  initFullInventoryGrid();
  initProgrammatorGrid();
  renderDataGeometry();

  bindInteractionSound();
});

/* Переключатель детектора переполнения. Живёт в app.js, а не в i18n.js,
   потому что это связь дев-панели с инструментом, а не сам инструмент. */
function toggleOverflowDetector(el) {
  const on = el.classList.toggle('active');
  window.i18nProbe.setDetector(on);
}
