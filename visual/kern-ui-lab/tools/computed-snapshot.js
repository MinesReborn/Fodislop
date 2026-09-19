/* ============================================================================
   ОТПЕЧАТОК ВЫЧИСЛЕННЫХ СТИЛЕЙ
   ============================================================================

   Инлайн-стили переезжают в классы. Отпечаток каскада (tools/check-cascade.py)
   здесь бесполезен: мы НАМЕРЕННО меняем то, где живёт объявление. Проверять
   надо не расположение, а результат — что видит браузер после всех правил.

   Поэтому снимаем вычисленные значения у каждого элемента на каждом экране.
   Совпали до и после — переезд ничего не изменил. Это единственная проверка,
   которая не зависит от того, КАК мы переносили.

   Путь элемента строится по индексам детей, а не по классам: классы мы как раз
   и меняем, и опираться на них значило бы измерять изменение измерителем,
   который сам меняется.

   Использование (в консоли страницы):
     await snapshotAll()   -> объект; сохранить, переехать, снять снова, diff
   ============================================================================ */

const WATCHED = [
  'display', 'position', 'width', 'height', 'padding', 'margin',
  'font-family', 'font-size', 'font-weight', 'line-height', 'letter-spacing',
  'color', 'background-color', 'background-image', 'border-width',
  'border-style', 'border-color', 'border-radius', 'gap', 'flex-direction',
  'align-items', 'justify-content', 'flex', 'grid-template-columns',
  'text-align', 'white-space', 'overflow', 'text-overflow', 'opacity',
];

function pathOf(el) {
  const parts = [];
  for (let n = el; n && n !== document.body; n = n.parentElement) {
    parts.unshift([...n.parentElement.children].indexOf(n));
  }
  return parts.join('/');
}

function snapshotScreen() {
  const out = {};
  for (const el of document.body.querySelectorAll('*')) {
    if (!el.getClientRects().length) continue;
    const cs = getComputedStyle(el);
    out[pathOf(el)] = WATCHED.map(p => cs.getPropertyValue(p)).join('|');
  }
  return out;
}

/* Снимок обязан мерить ПОКОЙ, а не мгновение.

   Четыре бесконечные анимации макета (дыхание короны, пульс маяка, волна
   пинга, свечение бусины) и все переходы в полёте дают разные значения при
   каждом замере: corona показывала opacity 0.859 против 0.800 просто потому,
   что снимок пришёлся на другую фазу. Это шум, неотличимый от дефекта, и
   он уже один раз увёл меня в ложный вывод.

   Поэтому на время снимка движение выключается целиком. Восстанавливается
   сразу после — инструмент не оставляет следов на странице. */
function freezeMotion() {
  const st = document.createElement('style');
  st.id = 'snapshot-freeze';
  st.textContent = '*, *::before, *::after { animation: none !important;'
    + ' transition: none !important; }';
  document.head.appendChild(st);
  return () => st.remove();
}

async function snapshotAll() {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const thaw = freezeMotion();
  const all = {};
  for (const s of ['auth', 'onboarding', 'menu', 'descent', 'ingame', 'pause', 'reconnect']) {
    switchViewState(s);
    await sleep(180);
    all[s] = snapshotScreen();
  }
  switchViewState('menu');
  await sleep(140);
  /* Список модалок перечисляется ИЗ ДЕРЕВА, а не рукой. Рукописный уже
     разошёлся: в нём не было mandatoryUpdateModal, и два дефекта переполнения
     в нём детектор просто не видел — их нашёл человек глазами. Обход, который
     не покрывает всё, даёт ложное спокойствие, что хуже отсутствия обхода. */
  const modals = [...document.querySelectorAll('.modal-overlay[id]')].map(el => el.id);
  for (const m of modals) {
    try { openModal(m); await sleep(180); all[m] = snapshotScreen(); closeModal(m); await sleep(60); }
    catch (e) { /* модалки может не быть — не повод ронять снимок */ }
  }
  thaw();
  return all;
}

function diffSnapshots(a, b) {
  const out = [];
  for (const screen of Object.keys(a)) {
    const x = a[screen], y = b[screen] || {};
    for (const path of Object.keys(x)) {
      if (!(path in y)) { out.push(`${screen} ${path}: элемент исчез`); continue; }
      if (x[path] === y[path]) continue;
      const av = x[path].split('|'), bv = y[path].split('|');
      const props = WATCHED.filter((_, i) => av[i] !== bv[i])
        .map((p, k) => `${p}: ${av[WATCHED.indexOf(p)]} -> ${bv[WATCHED.indexOf(p)]}`);
      out.push(`${screen} ${path}\n     ${props.join('\n     ')}`);
    }
    for (const path of Object.keys(y)) {
      if (!(path in x)) out.push(`${screen} ${path}: элемент появился`);
    }
  }
  return out;
}

window.cssProbe = { snapshotAll, diffSnapshots, WATCHED };
