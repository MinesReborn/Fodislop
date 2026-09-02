/* ============================================================================
   ТЕКСТ ПОД НАГРУЗКОЙ: ПСЕВДОЯЗЫК И ДЕТЕКТОР ПЕРЕПОЛНЕНИЯ
   ============================================================================

   Зачем. Игра переводится на много языков, а проверить раскладку сегодня
   можно ровно на двух, и оба — те, под которые её и рисовали. Псевдоязык
   снимает эту зависимость: он не перевод, а измеренная нагрузка. Строка
   растягивается ровно во столько раз, во сколько реально выросла при
   переводе строка такой же длины.

   Коэффициенты НЕ выбраны — они посчитаны по паре словарей игры
   (849 ключей, Assets/Resources/Localization/{en,ru}.json).
   Пересчёт: python3 tools/measure-i18n.py

   Рост оказался функцией длины оригинала: короткая строка растёт сильнее,
   потому что в ней нечем компенсировать служебные морфемы. Один общий
   множитель («заложим +30%») поэтому и не работает: он одновременно слишком
   мягок для подписей кнопок (там реально x4.5) и слишком жесток для абзацев
   (там x1.38). Отсюда таблица по корзинам, а не одно число.
   ============================================================================ */

/* Модуль замкнут в IIFE намеренно. index.html грузит обычные <script>, а они
   делят одну глобальную область: объявленный здесь `currentMode` столкнулся с
   таким же именем в app.js и убил весь app.js целиком — SyntaxError на этапе
   разбора, то есть молча, без единого следа в интерфейсе. Наружу выходит одно
   имя — window.i18nProbe. */
(function () {
'use strict';

  const GROWTH = {
    // корзина длины оригинала -> [p90, max] по замеру ru/en
    buckets: [
      { max: 5,        p90: 2.20, worst: 4.50 },
      { max: 10,       p90: 1.67, worst: 3.11 },
      { max: 20,       p90: 1.47, worst: 2.79 },
      { max: 40,       p90: 1.28, worst: 1.64 },
      { max: Infinity, p90: 1.16, worst: 1.38 },
    ],
    measuredFrom: 'en/ru, 849 ключей',
  };

  function growthFactor(len, mode) {
    const b = GROWTH.buckets.find(x => len <= x.max);
    return mode === 'worst' ? b.worst : b.p90;
  }

  /* Замена букв на диакритические двойники. Две задачи разом:
     видно, что строка прошла через псевдоязык (не спутать с настоящим текстом),
     и сразу вскрывается дыра в покрытии шрифта — если глиф отсутствует, на
     экране будет пустой прямоугольник вместо буквы, и это заметно. */
  const ACCENT = {
    a: 'ä', b: 'þ', c: 'ç', d: 'ð', e: 'ë', f: 'ƒ', g: 'ġ', h: 'ĥ', i: 'ï',
    j: 'ĵ', k: 'ķ', l: 'ł', m: 'ɱ', n: 'ñ', o: 'ö', p: 'þ', q: ' q', r: 'ř',
    s: 'š', t: 'ţ', u: 'ü', v: 'ṽ', w: 'ŵ', x: 'ẋ', y: 'ý', z: 'ž',
    A: 'Ä', B: 'ß', C: 'Ç', D: 'Ð', E: 'Ë', F: 'Ƒ', G: 'Ġ', H: 'Ĥ', I: 'Ï',
    J: 'Ĵ', K: 'Ķ', L: 'Ł', M: 'Ṁ', N: 'Ñ', O: 'Ö', P: 'Þ', Q: 'Q', R: 'Ř',
    S: 'Š', T: 'Ţ', U: 'Ü', V: 'Ṽ', W: 'Ŵ', X: 'Ẋ', Y: 'Ý', Z: 'Ž',
  };

  /* Хвост-заполнитель. Слогами, а не одной буквой: сплошное «ааааа» не имеет
     точек переноса, и коробка с data-fit="wrap" повела бы себя не так, как с
     настоящим языком. Слоги дают переносы там же, где их даёт живой текст. */
  const FILLER = ['ша', 'ру', 'де', 'ло', 'ти', 'ва', 'но', 'ре', 'ки', 'ма'];

  function pseudo(src, mode) {
    if (mode === 'off' || !src.trim()) return src;

    const accented = src.replace(/[A-Za-z]/g, ch => ACCENT[ch] || ch);
    if (mode === 'accents') return accented;

    const target = Math.round(src.length * growthFactor(src.length, mode));
    let out = accented;
    let i = 0;
    // Хвост наращивается словами: разрыв перед ним обязателен, иначе строка
    // становится одним нерушимым словом и проверяет не то, что нужно.
    while (out.length < target) {
      out += (i === 0 ? ' ' : '') + FILLER[i % FILLER.length];
      i++;
    }
    // Скобки — маркер границ. Если на экране виден «[», но не виден «]»,
    // значит хвост срезан, и это видно без всякого детектора.
    return '[' + out + ']';
  }

  /* --- применение к живому дереву --------------------------------------------
     Работаем по текстовым узлам, а не по элементам: элемент может смешивать
     свой текст с дочерними, и переписывание textContent такой узел разрушит.
     Оригинал кладётся в WeakMap, чтобы возврат к 'off' был точным, а не
     обратным преобразованием (обратного у псевдоязыка нет). */
  const ORIGINAL = new WeakMap();
  let currentMode = 'off';

  /* translate="no" — штатный атрибут HTML, ровно для этого и заведённый. Он
     уже наследуется по дереву и уже понятен машинным переводчикам, поэтому
     заводить своё «data-notranslate» значило бы держать второе имя для того же
     понятия. Псевдоязык его уважает — иначе он растягивал бы инициалы аватара
     и глифы фаз, то есть проверял бы несуществующий риск и прятал настоящие.
     Детектор считает такой узел объявившим поведение: содержимое не меняется
     при переводе, значит переполниться от языка не может. */
  function isUntranslatable(el) {
    return !!(el && el.closest('[translate="no"]'));
  }

  function textNodes(root) {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        if (!node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
        const el = node.parentElement;
        const tag = el && el.tagName;
        if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT;
        if (isUntranslatable(el)) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      },
    });
    const out = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) out.push(n);
    return out;
  }

  function setPseudoMode(mode) {
    currentMode = mode;
    for (const node of textNodes(document.body)) {
      if (!ORIGINAL.has(node)) ORIGINAL.set(node, node.nodeValue);
      const src = ORIGINAL.get(node);
      node.nodeValue = mode === 'off' ? src : pseudo(src, mode);
    }
    applyShrink();
    if (detectorOn) runDetector();
  }

  /* --- shrink ----------------------------------------------------------------
     Веб-эквивалент -unity-text-auto-size: двоичный поиск наибольшего кегля, при
     котором текст ещё влезает. Дно — --size-micro: ниже него текст нечитаем, и
     продолжать ужимать значит менять один тихий дефект на другой. Уперлись в
     дно и всё равно не влезло — это находка для детектора, а не повод сжимать
     дальше. */
  function fitShrink(el) {
    const floor = parseFloat(getComputedStyle(document.documentElement)
      .getPropertyValue('--size-micro')) || 9;
    el.style.fontSize = '';
    const ceiling = parseFloat(getComputedStyle(el).fontSize);
    if (el.scrollWidth <= el.clientWidth + 1) return;

    let lo = floor;
    let hi = ceiling;
    for (let i = 0; i < 8; i++) {
      const mid = (lo + hi) / 2;
      el.style.fontSize = mid + 'px';
      if (el.scrollWidth <= el.clientWidth + 1) lo = mid; else hi = mid;
    }
    el.style.fontSize = lo.toFixed(2) + 'px';
  }

  function applyShrink() {
    document.querySelectorAll('[data-fit="shrink"]').forEach(fitShrink);
  }

  /* --- детектор --------------------------------------------------------------
     Делает тихое громким. Переполнение = содержимое шире или выше коробки.
     Классификация — по объявленному поведению, потому что «вылезло» само по
     себе не дефект: у data-fit="clip" хвост срезан НАМЕРЕННО.

       violation  — объявленное поведение не покрыло случай:
                    clip без title (полный текст добыть неоткуда),
                    shrink, упёршийся в нижний кегль,
                    wrap/clamp, переполнившийся по высоте.
       undeclared — узел переполнился, не объявив вообще ничего. Это и есть
                    «тихий перенос»: сегодня таких 100% дерева.

     Порог 1px — субпиксельная раскладка даёт расхождение scroll/client на
     долях пикселя там, где переполнения нет. */
  let detectorOn = false;

  /* Число строк = высота содержимого / высота строки. line-height бывает
     'normal' — тогда берём 1.2 от кегля: точность не нужна, нужен ответ
     «одна строка или больше». */
  /* Режет ли коробка содержимое. Если нет — вертикальный вынос краски не
     теряет ни буквы, и дефектом не является. */
  function clipsContent(el) {
    const cs = getComputedStyle(el);
    if (cs.overflow !== 'visible' && cs.overflowY !== 'visible') return true;
    const lh = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.2;
    return el.scrollHeight - el.clientHeight >= lh;   // потеряна целая строка
  }

  /* Строки считаются по САМИМ строкам, а не по высоте коробки.

     Деление высоты на интерлиньяж — косвенная мера, и она врёт в обе стороны:
     на padding (кнопка 37/21 = «две строки» на ровном месте, 42 ложных
     переноса в одной модалке), на центрирующем флексе (ячейка руды 28px с
     интерлиньяжем 14 — «две строки» у слова из трёх букв, которому перенос
     запрещён вовсе). Range над содержимым даёт прямоугольник НА КАЖДУЮ
     отрисованную строку — это ответ на заданный вопрос, а не оценка. */
  function linesOf(el) {
    const cs = getComputedStyle(el);
    // Коробка, которой перенос запрещён, не может перенестись по определению:
    // считать в ней строки незачем.
    if (cs.whiteSpace === 'nowrap' || cs.whiteSpace === 'pre') return 1;
    try {
      const range = document.createRange();
      range.selectNodeContents(el);
      const tops = new Set();
      for (const r of range.getClientRects()) {
        if (r.width < 0.5 && r.height < 0.5) continue;
        tops.add(Math.round(r.top * 2) / 2);
      }
      if (tops.size) return tops.size;
    } catch (e) { /* к запасной мере ниже */ }

    let lh = parseFloat(cs.lineHeight);
    if (!Number.isFinite(lh)) lh = parseFloat(cs.fontSize) * 1.2;
    if (!lh) return 1;
    const pad = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
    return Math.max(1, Math.round((el.scrollHeight - pad) / lh));
  }

  /* Управляющий элемент: его текст — подпись, а не проза. Определяется по
     роли в дереве, а не по списку классов: список разошёлся бы. */
  /* Тот же набор объявлен в css/text.css как умолчание white-space: nowrap.
     Это ОДНО определение «что такое управляющий элемент», прочитанное двумя
     потребителями: каскадом и детектором. Расходиться им нельзя. */
  const CONTROL_TAGS = new Set(['BUTTON', 'SELECT', 'OPTION', 'TH', 'LABEL']);

  function isControl(el) {
    if (CONTROL_TAGS.has(el.tagName)) return true;
    const role = el.getAttribute('role');
    return role === 'button' || role === 'tab' || role === 'option';
  }

  function ownsText(el) {
    for (const n of el.childNodes) {
      if (n.nodeType === Node.TEXT_NODE && n.nodeValue.trim()) return true;
    }
    return false;
  }

  function runDetector() {
    // Наблюдатель хвостов работает через макрозадачу; детектор, запущенный
    // сразу после подстановки текста, видел бы состояние ДО восстановления
    // title и объявлял бы дефектом то, что через миллисекунду закрывается.
    // Меряем после того, как гарантия отработала.
    reexposeTails(document.body);
    const found = [];
    document.querySelectorAll('[data-overflow]').forEach(el => el.removeAttribute('data-overflow'));

    for (const el of document.querySelectorAll('body *')) {
      if (!ownsText(el) || !el.getClientRects().length) continue;

      const overX = el.scrollWidth > el.clientWidth + 1;
      const overY = el.scrollHeight > el.clientHeight + 1;

      /* Вторая половина задачи, которой здесь сначала не было.

         Детектор ловил ПЕРЕПОЛНЕНИЕ, но пропускал сам ПЕРЕНОС — а именно он
         и был назван проблемой. Коробка, которой разрешено расти, при
         переносе не переполняется: она просто становится выше, и
         scrollHeight равен clientHeight. Молчание детектора при этом значит
         не «всё хорошо», а «я смотрю не туда». Поймано человеком глазами на
         модалке обновления: «+ 148 МБ» разорвалось надвое, подписи кнопок
         сломались посреди фразы — детектор не сказал ничего. */
      const wrapped = linesOf(el) > 1;
      if (!overX && !overY && !wrapped) continue;

      /* Объявление родителя покрывает встроенных детей: растёт коробка
       родителя, а не span внутри неё. Без этого <h1 data-fit="wrap"> считался
       объявленным, а созданный рантаймом <span> внутри — нет, и один дефект
       считался дважды под разными именами. */
    const owner = el.closest('[data-fit]');
    const fit = owner ? owner.dataset.fit : undefined;
      let verdict = null;

      // Перенос сам по себе законен: абзацу он и положен. Дефект — перенос
      // ТАМ, ГДЕ ЗНАЧЕНИЕ НЕДЕЛИМО, и там, где его никто не объявлял.
      if (wrapped && !overX && !overY) {
        if (isUntranslatable(el) || fit === 'wrap' || fit === 'clamp') continue;
        // atomic и управляющий элемент не переносятся по определению; если
        // перенёсся — правило не доехало до элемента, и это дефект каскада,
        // а не раскладки. Прочее — просто не объявлено.
        const atomicByRule = fit === 'atomic' || isControl(el);
        if (!atomicByRule) continue;
        el.dataset.overflow = 'violation';
        found.push({
          verdict: el.dataset.overflow, fit: fit || (isControl(el) ? 'control' : '—'),
          selector: describe(el),
          text: (el.textContent || '').trim().slice(0, 48), overflowPx: 0,
        });
        continue;
      }

      if (isUntranslatable(el)) {
        // Не переводится — язык на него не влияет. Переполнение здесь возможно
        // только от раскладки, и это не задача текстового контракта.
        continue;
      } else if (!fit) {
        verdict = 'undeclared';
      } else if (fit === 'clip') {
        // Хвост срезан намеренно — дефект только если текст больше нигде не взять.
        if (!el.title && !el.getAttribute('aria-label')) verdict = 'violation';
      } else if (fit === 'clamp') {
        /* Исправление собственной ошибки. Я считал переполнение по высоте у
           clamp дефектом «коробку зажали снаружи». Но clamp ОБЯЗАН обрезать:
           в этом весь его смысл, и scrollHeight у него всегда больше
           clientHeight на длину отрезанного хвоста. Правило объявляло дефектом
           штатную работу правила.

           Настоящий дефект тот же, что у clip: хвост срезан, а взять полный
           текст неоткуда. */
        if (!el.title && !el.getAttribute('aria-label')) verdict = 'violation';
      } else if (fit === 'shrink') {
        const floor = parseFloat(getComputedStyle(document.documentElement)
          .getPropertyValue('--size-micro')) || 9;
        if (parseFloat(getComputedStyle(el).fontSize) <= floor + 0.5) verdict = 'violation';
      } else if (overY && clipsContent(el)) {
        /* Второе исправление. Порог в 1px слишком строг для крупного текста с
           плотным интерлиньяжем: у заголовка 52px с line-height 1.08 выносные
           элементы букв («р», «у») выходят за строчный бокс на 4px, и
           scrollHeight всегда больше clientHeight. Это не потеря текста, а
           намеренно плотная выключка титульной гарнитуры.

           Дефект — только там, где коробка РЕЖЕТ (overflow не visible) либо
           теряется целая строка. Меньше строки при видимом переполнении —
           вопрос интерлиньяжа, а не читаемости. */
        verdict = 'violation';
      }

      if (verdict) {
        el.dataset.overflow = verdict;
        found.push({
          verdict,
          fit: fit || '—',
          selector: describe(el),
          text: (el.textContent || '').trim().slice(0, 48),
          overflowPx: Math.max(el.scrollWidth - el.clientWidth, el.scrollHeight - el.clientHeight),
        });
      }
    }
    return found;
  }

  function describe(el) {
    const id = el.id ? '#' + el.id : '';
    const cls = el.className && typeof el.className === 'string'
      ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.')
      : '';
    return el.tagName.toLowerCase() + id + cls;
  }

  function setDetector(on) {
    detectorOn = on;
    if (on) {
      const found = runDetector();
      const undeclared = found.filter(f => f.verdict === 'undeclared').length;
      console.log(`[текст] переполнений: ${found.length} `
        + `(нарушений контракта ${found.length - undeclared}, без объявления ${undeclared}) `
        + `режим псевдоязыка: ${currentMode}`);
      console.table(found.slice(0, 40));
    } else {
      document.querySelectorAll('[data-overflow]').forEach(el => el.removeAttribute('data-overflow'));
    }
  }

  /* Раскладка меняется от ширины окна и от появления экранов — детектор обязан
     переспрашивать, иначе показывает состояние на момент включения. */
  let raf = 0;
  function scheduleRecheck() {
    if (!detectorOn && currentMode === 'off') return;
    cancelAnimationFrame(raf);
    raf = requestAnimationFrame(() => {
      applyShrink();
      if (detectorOn) runDetector();
    });
  }
  window.addEventListener('resize', scheduleRecheck);

  /* ==========================================================================
     СЛОВАРЬ: МАКЕТ ЧИТАЕТ ТЕКСТ ИГРЫ, А НЕ СВОЙ СОБСТВЕННЫЙ
     ==========================================================================

     До этого макет держал 388 строк захардкоженными, а игра — 849 ключей в
     Assets/Resources/Localization. Две копии одного текста, расходящиеся молча:
     ровно та болезнь, ради которой строилась вся остальная система токенов.

     Теперь источник истины один — словарь игры. 97 строк макета уже нашли там
     свой ключ и берут текст оттуда. Остальные 287 живут в i18n/mirror.ru.json:
     это не второй словарь, а ОЧЕРЕДЬ на перенос — текст, который в макете уже
     есть, а в игре ещё нет. Разделение намеренное: пока строка в mirror.*,
     видно, что игра её не знает.

     Замена — первого прямого текстового узла, а не textContent. Элемент вроде
     <h1>Планета ждёт <span>под поверхностью.</span></h1> владеет и текстом, и
     детьми; запись textContent снесла бы детей вместе с оформлением. */
  const DICT = { game: {}, mirror: {}, lang: 'ru' };

  function firstTextNode(el) {
    for (const n of el.childNodes) {
      if (n.nodeType === Node.TEXT_NODE && n.nodeValue.trim()) return n;
    }
    return null;
  }

  async function loadDict(lang) {
    const grab = async url => {
      try {
        const r = await fetch(url, { cache: 'no-cache' });
        return r.ok ? await r.json() : null;
      } catch (e) { return null; }
    };
    const game = await grab(`../../Assets/Resources/Localization/${lang}.json`);
    const mirror = await grab(`i18n/mirror.${lang}.json`);
    // Отсутствие словаря — не тихая деградация к ключам, а явная жалоба:
    // сырые ключи на экране должны иметь названную причину.
    if (!game) console.warn(`[i18n] словарь игры '${lang}' не прочитан — текст останется как в разметке`);
    DICT.game = game || {};
    DICT.mirror = mirror || {};
    DICT.lang = lang;
  }

  function resolve(key) {
    // Словарь игры выигрывает у макетного всегда: если ключ доехал до игры,
    // её формулировка и есть настоящая.
    return DICT.game[key] !== undefined ? DICT.game[key] : DICT.mirror[key];
  }

  /* Заголовок из двух строк с акцентом на второй.

     Раньше он был двумя ключами и жёстким <br> в разметке: «Планета ждёт» /
     «под поверхностью.». Так перевести нельзя — вторая часть грамматически
     продолжает первую, а порядок слов в языках разный. Мы это заметили только
     потому, что выведенные имена ключей встали рядом (_2), — то есть система
     назвала свой собственный дефект.

     Теперь ключ один, а точку разрыва ставит переводчик символом «|». Это и
     есть единственно возможное решение: где ломать фразу, знает только тот,
     кто знает язык. Разделителя нет — заголовок в одну строку, и это законный
     перевод, а не ошибка.

     @uss via — в UI Toolkit тот же ключ даёт два Label в колонке либо
     rich-text <color>; разбор строки один и тот же. */
  function renderSplit(el, value) {
    const cut = value.indexOf('|');
    el.textContent = '';
    if (cut < 0) { el.textContent = value; return; }
    el.append(value.slice(0, cut).trim());
    el.append(document.createElement('br'));
    const tail = document.createElement('span');
    if (el.dataset.i18nAccent) tail.className = el.dataset.i18nAccent;
    tail.textContent = value.slice(cut + 1).trim();
    el.append(tail);
  }

  /* Контракт clip и clamp требует, чтобы срезанный хвост был доступен в
     другом месте — иначе текст просто потерян. Ставить title руками нельзя:
     он немедленно разойдётся с переводом, и мы получим подпись на одном
     языке и подсказку на другом. Поэтому title ставит тот же код, который
     подставляет текст, из того же значения. Разойтись им теперь нечем. */
  function exposeTail(el, value) {
    const owner = el.closest('[data-fit]');
    const fit = owner && owner.dataset.fit;
    if (fit !== 'clip' && fit !== 'clamp') return;
    if (owner.getAttribute('aria-label')) return;
    owner.title = value;
  }

  function applyDict() {
    let hit = 0, miss = 0;
    for (const el of document.querySelectorAll('[data-i18n]')) {
      const value = resolve(el.dataset.i18n);
      if (value === undefined) { miss++; continue; }
      // Признак — объявление на элементе, а НЕ наличие «|» в сегодняшнем
      // переводе: иначе язык без разрыва уходил бы по другой ветке и оставлял
      // хвост от предыдущего языка. Поймано собственной проверкой.
      if (el.dataset.i18nAccent !== undefined) {
        renderSplit(el, value);
        exposeTail(el, value.replace('|', ' '));
        hit++;
        continue;
      }
      const node = firstTextNode(el);
      if (!node) { miss++; continue; }
      node.nodeValue = value;
      ORIGINAL.delete(node);
      exposeTail(el, value);
      hit++;
    }
    return { hit, miss };
  }

  async function setLanguage(lang) {
    await loadDict(lang);
    const r = applyDict();
    console.log(`[i18n] ${lang}: подставлено ${r.hit}, без перевода ${r.miss}`);
    if (currentMode !== 'off') setPseudoMode(currentMode);
    applyShrink();
    if (detectorOn) runDetector();
    return r;
  }

  /* Сердцебиение, по образцу UILocalizer.AssertLocalized в игре: после
     подстановки на экране не должно остаться ни одного ключа, который словарь
     ЗНАЕТ. Если Apply не отработал, здесь будут сырые ключи — и об этом надо
     кричать, а не показывать их пользователю. */
  function assertLocalized() {
    const raw = [];
    for (const el of document.querySelectorAll('[data-i18n]')) {
      const node = firstTextNode(el);
      if (node && resolve(node.nodeValue.trim()) !== undefined) {
        raw.push(el.dataset.i18n);
      }
    }
    if (raw.length) console.error(`[i18n] неразрешённые ключи на экране: ${raw.length}`, raw.slice(0, 10));
    return raw;
  }

  /* Хвост под сокращением обязан быть доступен ВСЕГДА, а не только когда текст
     пришёл из словаря. Полагаться на то, что каждый пишущий вызовет exposeTail,
     нельзя: инспектор инвентаря пишет описание сам — и три строки clamp молча
     съедали четвёртую. Поэтому гарантия структурная: наблюдатель следит за
     каждым сокращающим боксом и восстанавливает title после любой записи,
     кем бы она ни была сделана. */
  const FIT_CUT = "[data-fit='clip'],[data-fit='clamp']";

  function reexposeTails(root) {
    const boxes = root.matches && root.matches(FIT_CUT)
      ? [root] : [...(root.querySelectorAll ? root.querySelectorAll(FIT_CUT) : [])];
    for (const box of boxes) {
      if (box.getAttribute('aria-label')) continue;
      const text = box.textContent.trim();
      // Титул нужен только когда содержимое ДЕЙСТВИТЕЛЬНО не помещается:
      // подсказка, повторяющая видимое, — шум.
      if (box.scrollHeight - box.clientHeight < 1 && box.scrollWidth - box.clientWidth < 1) {
        if (box.title && box.title === text) box.removeAttribute('title');
        continue;
      }
      if (box.title !== text) box.title = text;
    }
  }

  let tailPending = 0;
  const tailWatch = new MutationObserver(() => {
    // Не requestAnimationFrame: в фоновой вкладке кадры не идут, и гарантия
    // молча отключалась бы ровно там, где её некому проверить.
    clearTimeout(tailPending);
    tailPending = setTimeout(() => reexposeTails(document.body), 0);
  });

  function watchTails() {
    reexposeTails(document.body);
    tailWatch.observe(document.body, { subtree: true, childList: true, characterData: true });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', watchTails);
  } else {
    watchTails();
  }

  window.i18nProbe = { setPseudoMode, setDetector, runDetector, pseudo, GROWTH,
    scheduleRecheck, setLanguage, applyDict, assertLocalized, reexposeTails, linesOf, DICT };

  }());
