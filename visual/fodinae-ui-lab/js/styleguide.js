/* ============================================================================
   FODINAE — движок стайлгайда.

   Читает токены из живого CSS в момент открытия страницы, а не из захардкоженной
   копии. Витрина поэтому физически не может разойтись с игрой: изменился токен —
   изменилось и то, что здесь показано.
   ============================================================================ */

(() => {
  'use strict';

  const root = document.documentElement;
  const css = getComputedStyle(root);
  const value = name => css.getPropertyValue(name).trim();

  // --- Классификация токенов ------------------------------------------------

  /* Суффиксы плотности из tokens.css §2.1. Имя токена читается «слой — цвет —
     плотность», поэтому принадлежность к слою плотности определяется концом
     имени, а не началом. */
  const DENSITY = [
    'tint', 'mist', 'film', 'hairline',
    'wash', 'haze', 'sheen', 'line',
    'glow', 'shade', 'edge', 'line-strong',
    'fill', 'veil', 'gleam',
    'dense', 'solid', 'cast',
  ];

  const isDensity = n => DENSITY.some(suffix => n.endsWith('-' + suffix));

  const GROUPS = {
    elevation: n => ['--rgb-void', '--rgb-abyss', '--rgb-slate', '--rgb-shelf'].includes(n),
    material: n => n.startsWith('--mat-'),
    primitive: n => n.startsWith('--rgb-') || n.startsWith('--hex-'),
    density: isDensity,
    semantic: n =>
      !isDensity(n) && (
        n.startsWith('--surface-') || n.startsWith('--border-') || n.startsWith('--text-') ||
        n.startsWith('--accent-') || n.startsWith('--state-') || n.startsWith('--rarity-')),
  };

  /* Классификация по списку префиксов — это вторая, ручная копия ответа на
     вопрос «какие бывают токены». Она молча расходится с первой: новое
     семейство просто не появляется в витрине, и заметить это нечем.

     Поэтому витрина обязана сама сообщать о том, чего не знает. Список ниже —
     токены, которым место в витрине не нашлось; он выводится на странице, а не
     прячется в консоль. Пустой список — единственное допустимое состояние. */
  const SHOWN_ELSEWHERE = [
    '--space-', '--size-', '--radius-', '--dur-', '--ease-', '--blur-',
    '--layer-', '--leading-', '--tracking-', '--weight-', '--face-', '--font-',
    '--focus-', '--planet-', '--menu-', '--hex-ink', '--btn-', '--cell-', '--icon-',
    '--order-', '--fit-', '--safe-',
  ];

  const isClassified = n =>
    Object.values(GROUPS).some(test => test(n)) ||
    SHOWN_ELSEWHERE.some(prefix => n.startsWith(prefix));

  /** Токен объявлен как «3 5 9», а не как цвет — превращаем в rgb() для показа. */
  const asColor = (name, raw) =>
    name.startsWith('--rgb-') ? `rgb(${raw})` : raw;

  function swatch(name, raw) {
    const el = document.createElement('div');
    el.className = 'sg-swatch';
    const chip = document.createElement('div');
    chip.className = 'sg-swatch-chip';
    const fill = document.createElement('div');
    fill.className = 'sg-swatch-fill';
    fill.style.background = asColor(name, raw);
    chip.appendChild(fill);

    const meta = document.createElement('div');
    meta.className = 'sg-swatch-meta';
    const n = document.createElement('div');
    n.className = 'sg-swatch-name';
    n.textContent = name;
    const v = document.createElement('div');
    v.className = 'sg-swatch-value';
    v.textContent = raw;
    meta.append(n, v);

    el.append(chip, meta);
    return el;
  }

  // --- Контраст -------------------------------------------------------------

  function parseColor(str) {
    const probe = document.createElement('div');
    probe.style.color = str;
    document.body.appendChild(probe);
    const computed = getComputedStyle(probe).color;
    probe.remove();
    const m = computed.match(/-?[\d.]+/g);
    return m ? m.slice(0, 3).map(Number) : null;
  }

  const toLinear = c => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);

  function luminance(rgb) {
    const [r, g, b] = rgb.map(v => toLinear(v / 255));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  function contrast(fg, bg) {
    const a = luminance(fg);
    const b = luminance(bg);
    const [lo, hi] = a < b ? [a, b] : [b, a];
    return (hi + 0.05) / (lo + 0.05);
  }

  const CONTRAST_ROWS = [
    ['--text-primary', '--surface-void', 4.5, 'Основной текст'],
    ['--text-secondary', '--surface-void', 4.5, 'Вторичный текст'],
    ['--text-tertiary', '--surface-void', 4.5, 'Третичный текст'],
    ['--text-disabled', '--surface-void', 4.5, 'Выключено (порог не требуется)'],
    ['--accent-gold', '--surface-void', 4.5, 'Золотой акцент'],
    ['--accent-cyan', '--surface-void', 4.5, 'Циановый акцент'],
    ['--state-danger', '--surface-void', 4.5, 'Опасность'],
    ['--text-on-gold', '--accent-gold', 4.5, 'Текст на золотой кнопке'],
  ];

  function renderContrast(table) {
    table.innerHTML =
      '<thead><tr><th>Пара</th><th>Токены</th><th>Контраст</th><th>Итог</th></tr></thead>';
    const body = document.createElement('tbody');

    for (const [fgName, bgName, min, label] of CONTRAST_ROWS) {
      const fg = parseColor(value(fgName));
      const bg = parseColor(value(bgName));
      if (!fg || !bg) continue;

      const ratio = contrast(fg, bg);
      const optional = label.includes('порог не требуется');
      const pass = ratio >= min;
      const badge = optional
        ? '<span class="sg-uss sg-uss--via">по замыслу</span>'
        : pass
          ? '<span class="sg-uss sg-uss--ok">AA</span>'
          : '<span class="sg-uss sg-uss--no">ниже нормы</span>';

      const tr = document.createElement('tr');
      tr.innerHTML =
        `<td style="color:${value(fgName)};background:${value(bgName)}">${label}</td>` +
        `<td><code>${fgName}</code> на <code>${bgName}</code></td>` +
        `<td class="fdn-font-data">${ratio.toFixed(2)}:1</td>` +
        `<td>${badge}</td>`;
      body.appendChild(tr);
    }
    table.appendChild(body);
  }

  // --- Шкалы ----------------------------------------------------------------

  function renderScale(table, prefix, names, render) {
    table.innerHTML = '<thead><tr><th>Токен</th><th>Значение</th><th>Образец</th></tr></thead>';
    const body = document.createElement('tbody');
    for (const name of names) {
      const raw = value(name);
      if (!raw) continue;
      const tr = document.createElement('tr');
      tr.innerHTML = `<td><code>${name}</code></td><td class="fdn-font-data">${raw}</td><td>${render(raw)}</td>`;
      body.appendChild(tr);
    }
    table.appendChild(body);
  }

  // --- Сборка ---------------------------------------------------------------

  async function build() {
    let source = '';
    try {
      source = await (await fetch('css/tokens.css')).text();
    } catch {
      document.querySelectorAll('[data-swatches]').forEach(el => {
        el.textContent = 'Открой страницу через http-сервер — под file:// токены не читаются.';
      });
      return;
    }

    // Имена в порядке объявления, без дублей.
    const names = [...new Set(
      [...source.matchAll(/(--[a-z0-9-]+)\s*:/gi)].map(m => m[1])
    )];

    for (const el of document.querySelectorAll('[data-swatches]')) {
      const group = GROUPS[el.dataset.swatches];
      const picked = names.filter(n => {
        if (!group(n)) return false;
        // Ступени рампы показываем только в своей секции, не дублируя в примитивах.
        if (el.dataset.swatches === 'primitive' && GROUPS.elevation(n)) return false;
        return true;
      });
      picked.forEach(n => el.appendChild(swatch(n, value(n))));
    }

    // Самопроверка витрины: назвать то, чего она не знает.
    const orphans = names.filter(n => !isClassified(n));
    const orphanBox = document.querySelector('[data-orphans]');
    if (orphanBox) {
      orphanBox.textContent = orphans.length
        ? `Не показано в витрине (${orphans.length}): ${orphans.join(', ')}. `
          + 'Витрина уже́ у́же системы — допишите классификацию в js/styleguide.js.'
        : `Все ${names.length} объявленных токенов показаны. Витрина полна.`;
      orphanBox.classList.toggle('sg-note--warn', orphans.length > 0);
    }

    const contrastTable = document.querySelector('[data-contrast]');
    if (contrastTable) renderContrast(contrastTable);

    const typeScale = document.querySelector('[data-typescale]');
    if (typeScale) {
      renderScale(
        typeScale, '--size-',
        names.filter(n => n.startsWith('--size-')),
        raw => `<span style="font-size:${raw}">Глубина 2480</span>`
      );
    }

    const spaceTable = document.querySelector('[data-scale="space"]');
    if (spaceTable) {
      renderScale(
        spaceTable, '--space-',
        names.filter(n => n.startsWith('--space-')),
        raw => `<div class="sg-ruler" style="width:${raw}"></div>`
      );
    }

    const layerTable = document.querySelector('[data-scale="layer"]');
    if (layerTable) {
      layerTable.innerHTML = '<thead><tr><th>Токен</th><th>Значение</th><th>Что живёт на слое</th></tr></thead>';
      const notes = {
        '--layer-backdrop': 'звёздное небо, туманность',
        '--layer-scene': 'планета, орбиты, маяк',
        '--layer-content': 'колонка меню, HUD',
        '--layer-chrome': 'шапка, футер',
        '--layer-rail': 'иконочный рейл',
        '--layer-gate': 'авторизация',
        '--layer-overlay': 'онбординг, пауза',
        '--layer-modal': 'модальные окна',
        '--layer-toast': 'тосты, тултипы',
        '--layer-devtools': 'dev-панель прототипа, в игру не переносится',
      };
      const body = document.createElement('tbody');
      for (const n of names.filter(x => x.startsWith('--layer-'))) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td><code>${n}</code></td><td class="fdn-font-data">${value(n)}</td><td>${notes[n] || ''}</td>`;
        body.appendChild(tr);
      }
      layerTable.appendChild(body);
    }

    /* --order-* — вторая, ЛОКАЛЬНАЯ шкала порядка: внутри одного слоя.
       Отдельная таблица, а не строки в предыдущей: смешать их значило бы
       сказать, что маяк и модальное окно сравнимы по глубине. Они не
       сравнимы — маяк упорядочен только относительно планеты. */
    const orderTable = document.querySelector('[data-scale="order"]');
    if (orderTable) {
      orderTable.innerHTML = '<thead><tr><th>Токен</th><th>Значение</th><th>Внутри чего</th></tr></thead>';
      const within = {
        '--order-corona': 'планета', '--order-orbit': 'планета',
        '--order-planet': 'планета', '--order-beacon': 'планета',
        '--order-surface': 'шахта', '--order-magma': 'шахта',
        '--order-geogrid': 'шахта', '--order-night': 'шахта',
        '--order-atmosphere': 'шахта', '--order-target': 'шахта',
        '--order-mine-canvas': 'игровой экран', '--order-mine-robot': 'игровой экран',
        '--order-bead': 'таймлайн хроники',
      };
      const ob = document.createElement('tbody');
      for (const n of names.filter(x => x.startsWith('--order-'))) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td><code>${n}</code></td><td class="fdn-font-data">${value(n)}</td><td>${within[n] || ''}</td>`;
        ob.appendChild(tr);
      }
      orderTable.appendChild(ob);
    }

    /* Лестница сейф-зон читается из токенов, а не набирается в таблице:
       набранная руками таблица расходится со значениями при первой правке
       шкалы, и витрина начинает врать. */
    const safeTable = document.querySelector('[data-scale="safe"]');
    if (safeTable) {
      safeTable.innerHTML = '<thead><tr><th>Токен</th><th>Значение</th><th>Уровень</th></tr></thead>';
      const level = {
        '--safe-screen': 'край экрана: шапка, футер',
        '--safe-panel': 'карточка, модалка, крупная панель',
        '--safe-box': 'вложенная коробка внутри панели',
        '--safe-tight': 'плотная строка, чип, бейдж',
      };
      const tb = document.createElement('tbody');
      for (const n of names.filter(x => x.startsWith('--safe-'))) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td><code>${n}</code></td>`
          + `<td class="fdn-font-data">${value(n)} → ${getComputedStyle(document.documentElement).getPropertyValue(n).trim()}</td>`
          + `<td>${level[n] || ''}</td>`;
        tb.appendChild(tr);
      }
      safeTable.appendChild(tb);
    }

    /* Шкала ширин модалки — тоже из токенов, по той же причине. */
    const modalTable = document.querySelector('[data-scale="modal"]');
    if (modalTable) {
      modalTable.innerHTML =
        '<thead><tr><th>Токен</th><th>Значение</th><th>Кто занимает</th></tr></thead>';
      const who = {
        '--modal-sm': 'чат — одна колонка сообщений',
        '--modal-md': 'профиль, клан, обновление — карточка с двумя колонками',
        '--modal-lg': 'инвентарь — сетка и инспектор рядом',
        '--modal-xl': 'браузер серверов, хроника — таблица во всю ширину',
      };
      const tb = document.createElement('tbody');
      for (const n of names.filter(x => x.startsWith('--modal-'))) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td><code>${n}</code></td>`
          + `<td class="fdn-font-data">${value(n)}</td>`
          + `<td>${who[n] || ''}</td>`;
        tb.appendChild(tr);
      }
      modalTable.appendChild(tb);
    }

    const radii = document.querySelector('[data-radii]');
    if (radii) {
      for (const n of names.filter(x => x.startsWith('--radius-'))) {
        const box = document.createElement('div');
        box.style.cssText =
          `width:78px;height:78px;background:var(--accent-cyan-wash);` +
          `border:1px solid var(--accent-cyan);border-radius:${value(n)};` +
          `display:flex;align-items:center;justify-content:center;text-align:center;` +
          `font-family:var(--face-data);font-size:var(--size-micro);color:var(--accent-cyan)`;
        box.textContent = n.replace('--radius-', '') + ' · ' + value(n);
        radii.appendChild(box);
      }
    }

    const motion = document.querySelector('[data-motion]');
    if (motion) {
      motion.innerHTML = '<thead><tr><th>Токен</th><th>Значение</th><th>Наведи курсор</th></tr></thead>';
      const body = document.createElement('tbody');
      const eases = names.filter(x => x.startsWith('--ease-'));
      for (const n of names.filter(x => x.startsWith('--dur-'))) {
        const tr = document.createElement('tr');
        tr.innerHTML =
          `<td><code>${n}</code></td><td class="fdn-font-data">${value(n)}</td>` +
          `<td><div class="sg-ruler sg-motion" style="width:40px;transition:width ${value(n)} var(--ease-signature)"></div></td>`;
        body.appendChild(tr);
      }
      for (const n of eases) {
        const tr = document.createElement('tr');
        tr.innerHTML =
          `<td><code>${n}</code></td><td class="fdn-font-data">${value(n)}</td>` +
          `<td><div class="sg-ruler sg-motion" style="width:40px;transition:width var(--dur-cinematic) ${value(n)}"></div></td>`;
        body.appendChild(tr);
      }
      motion.appendChild(body);
      motion.addEventListener('mouseover', e => {
        if (e.target.classList.contains('sg-motion')) e.target.style.width = '260px';
      });
      motion.addEventListener('mouseout', e => {
        if (e.target.classList.contains('sg-motion')) e.target.style.width = '40px';
      });
    }

    const tier = document.querySelector('[data-tier]');
    if (tier) {
      const update = () => {
        const w = window.innerWidth;
        const name = w < 900 ? 'compact' : w < 1600 ? 'standard' : 'wide';
        tier.innerHTML =
          `Текущая ширина окна <strong class="fdn-font-data">${w}px</strong> — тир ` +
          `<strong class="fdn-text--cyan">${name}</strong>. ` +
          `<code>--size-md</code> сейчас <strong class="fdn-font-data">${value('--size-md')}</strong>, ` +
          `<code>--size-3xl</code> — <strong class="fdn-font-data">${value('--size-3xl')}</strong>. ` +
          `Измени размер окна, чтобы увидеть переключение.`;
      };
      update();
      window.addEventListener('resize', update);
    }
  }

  document.addEventListener('DOMContentLoaded', build);
})();
