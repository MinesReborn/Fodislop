// Программатор: окно OpenProgrammatorPacket. Программа — страницы 16×12 (ProgrammatorData), в клетке команда ProgAction
// и её значение (UpdateProgramPacket: Operator, Label, Value), точки остановки — отдельным списком клеток.
// Состояния — ProgramState: остановлена, работает, пауза (BreakpointHitPacket со стеком вызовов), ошибка.
(() => {
  const $ = id => document.getElementById(id);
  const D = window.PROG_DATA, C = D.code;
  const COLS = 16, ROWS = 12, PER = COLS * ROWS, MAX_PAGES = 25;
  const token = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  const CAT_COLOR = { CAT_CONTROL_FLOW: token('--prog-control'), CAT_ACTIONS: token('--prog-action'), CAT_OBSERVER: token('--prog-observer'), CAT_CONDITIONS: token('--prog-condition'), CAT_MEMORY: token('--prog-memory') };
  const catOf = {}; D.cats.forEach(c => c.ops.forEach(o => { catOf[o] = c.id; }));
  const keyOf = {}; D.keys.forEach(([k, ops]) => ops.forEach(o => { keyOf[o] ||= k; }));
  // Команды со значением (ProgrammatorInputSpec): метка или имя переменной, у сравнений ещё число
  const TEXT = new Set(['Goto', 'Call', 'CallArg', 'CallState', 'YesNoGoto', 'NoYesGoto', 'CallWhenDied', 'Label', 'DebugPause', 'DebugShow'].map(n => C[n]));
  const NUM = new Set(['VarGreaterThanNumber', 'VarLessThanNumber', 'VarEqualsNumber'].map(n => C[n]));

  // ── Программы: пример игрока ──
  const blank = () => Array.from({ length: PER }, () => null);
  const row = (page, r, cells) => cells.forEach((c, i) => { if (c) page[r * COLS + i] = typeof c === 'string' ? { op: C[c] } : { op: C[c[0]], text: c[1], num: c[2] }; });
  function sample() {
    const p1 = blank(), p2 = blank();
    row(p1, 0, ['SetStart', ['Label', 'цикл'], 'IsBasketFull', ['YesNoGoto', 'домой'], 'CellForward', 'IsEmpty', ['YesNoGoto', 'идти'], 'Dig', ['Goto', 'цикл']]);
    row(p1, 1, [['Label', 'идти'], 'MoveForward', 'CellForward', 'IsCrystal', ['YesNoGoto', 'цикл'], 'RotateRighthand', ['Goto', 'цикл']]);
    row(p1, 2, [['Label', 'домой'], ['DebugPause', 'n'], 'RotateUp', 'MoveForward', 'RepeatLastAction', ['VarGreaterThanNumber', 'n', 40], 'YesNoTerminate', ['Goto', 'домой']]);
    row(p1, 4, [['CallWhenDied', 'беда'], 'IsHealthLessThanHalf', 'YesNoReturn', 'Heal', 'Return']);
    row(p1, 5, [['Label', 'беда'], 'STDHeal', 'Terminate']);
    row(p2, 0, [['Label', 'склад'], 'BoxAll', 'RotateLeft', 'MoveForward', 'BuildRoad', ['Goto', 'склад']]);
    return [p1, p2];
  }
  const programs = [
    { name: 'Копатель', pages: sample(), breaks: new Set([2 * COLS + 1]) },
    { name: 'Дорога на склад', pages: [blank()], breaks: new Set() },
    { name: 'Шахта 3×3', pages: [blank(), blank(), blank()], breaks: new Set() },
  ];
  row(programs[1].pages[0], 0, ['SetStart', 'BuildRoad', 'MoveForward', 'RepeatLastAction', 'Terminate']);
  let cur = 0, page = 0, cat = 'CAT_ACTIONS', state = 'stopped', here = -1, multi = false;
  const sel = new Set();
  let anchor = 0;
  const undo = [], redo = [];
  let clip = null;
  const P = () => programs[cur];
  const cells = () => P().pages[page];

  // ── Отрисовка ──
  const grid = $('progGrid');
  grid.innerHTML = Array.from({ length: PER }, (_, i) => `<button type="button" class="prog-cell" role="gridcell" data-i="${i}"></button>`).join('');
  const cellEls = [...grid.children];
  // Адреса клеток — строка и столбец с единицы, как в «Пауза на клетке 1 · 3:2»: линейки над и слева от сетки
  const sheet = document.createElement('div');
  sheet.className = 'prog-sheet';
  sheet.innerHTML = `<ol class="prog-cols" aria-hidden="true">${Array.from({ length: COLS }, (_, i) => `<li>${i + 1}</li>`).join('')}</ol>`
    + `<ol class="prog-rows" aria-hidden="true">${Array.from({ length: ROWS }, (_, i) => `<li>${i + 1}</li>`).join('')}</ol>`;
  grid.replaceWith(sheet); sheet.append(grid);
  const colEls = [...sheet.querySelector('.prog-cols').children], rowEls = [...sheet.querySelector('.prog-rows').children];
  // Адрес исполняемой клетки (пауза на этой странице), иначе — последней выбранной
  function markAddress() {
    const i = state === 'paused' && Math.floor(here / PER) === page ? here % PER : [...sel].at(-1);
    colEls.forEach((li, c) => li.classList.toggle('is-on', i != null && i % COLS === c));
    rowEls.forEach((li, r) => li.classList.toggle('is-on', i != null && Math.floor(i / COLS) === r));
  }
  const icon = op => `<img src="img/prog/${op}.png" alt="">`;
  function paintCell(i) {
    const c = cells()[i], el = cellEls[i];
    el.innerHTML = c ? icon(c.op) + (c.text != null ? `<small>${c.text}${c.num != null ? ' ' + c.num : ''}</small>` : '') : '';
    el.title = c ? D.names[c.op] : '';
    if (c) el.dataset.loc = D.loc[c.op]; else delete el.dataset.loc;
    el.classList.toggle('is-sel', sel.has(i));
    el.classList.toggle('is-break', P().breaks.has(page * PER + i));
    el.classList.toggle('is-here', state === 'paused' && here === page * PER + i);
  }
  function render() {
    cellEls.forEach((_, i) => paintCell(i));
    const n = P().pages.length;
    $('progPage').textContent = `Стр. ${page + 1}/${n}`;
    $('progPrev').disabled = page === 0; $('progNext').disabled = page === n - 1;
    $('progAddPage').disabled = n >= MAX_PAGES; $('progDelPage').disabled = n === 1;
    $('progPickName').textContent = P().name;
    const run = state === 'running' || state === 'paused';
    $('progWin').classList.toggle('is-run', run);
    $('progState').className = 'prog-state' + (state === 'running' ? ' is-run' : state === 'paused' ? ' is-paused' : '');
    $('progState').textContent = { stopped: 'Остановлена', running: 'Работает', paused: `Пауза на клетке ${cellName(here)}`, failed: 'Остановлена с ошибкой' }[state];
    $('progGo').classList.toggle('is-stop', run);
    $('progGo').innerHTML = `<svg><use href="#i-${run ? 'stop' : 'play'}"/></svg><span>${run ? 'Остановить' : 'Запустить'}</span>`;
    $('progPause').disabled = state !== 'running';
    ['progStepIn', 'progStepOver', 'progStepOut'].forEach(id => { $(id).disabled = state !== 'paused'; });
    $('progError').hidden = state !== 'failed';
    $('progError').textContent = 'Ошибка программы: неизвестная инструкция ?73 в ячейке 3:6.';
    renderMem(); foot(); markAddress();
  }
  // Клетка как «стр. 1 · 3:5» — строка и столбец с единицы
  const cellName = g => { const pg = Math.floor(g / PER), i = g % PER; return `${pg + 1} · ${Math.floor(i / COLS) + 1}:${i % COLS + 1}`; };
  function renderPalette() {
    $('progCats').innerHTML = D.cats.map(c => `<button type="button" aria-pressed="${c.id === cat}" data-cat="${c.id}" data-loc="programmator.cat.${c.id}" style="--c:${CAT_COLOR[c.id]}"><i></i>${c.name}</button>`).join('');
    const c = D.cats.find(x => x.id === cat);
    $('progOps').innerHTML = c.ops.map(o => `<button type="button" class="prog-op" data-op="${o}" data-loc="${D.loc[o]}" style="--c:${CAT_COLOR[cat]}" title="${D.names[o]}">${icon(o)}</button>`).join('');
  }
  // Память (ProgramMemoryPacket) и стек вызовов (BreakpointHitPacket) — только на паузе
  function renderMem() {
    $('progMem').innerHTML = state !== 'paused'
      ? '<p class="prog-mem-note">Значения переменных и стек вызовов видны, когда программа стоит на паузе: на точке остановки или по кнопке «Пауза».</p>'
      : `<table><tr><th>Переменная</th><th>Значение</th></tr>${[['n', 38, 1], ['k', 4, 0], ['x', -12, 0], ['сост.', 1, 1]].map(([v, n, ch]) => `<tr class="${ch ? 'is-changed' : ''}"><td>${v}</td><td>${n}</td></tr>`).join('')}</table>
         <table style="margin-top:16px"><tr><th>Стек вызовов</th><th>Клетка</th></tr><tr><td>домой</td><td>${cellName(here)}</td></tr><tr><td>цикл</td><td>${cellName(3)}</td></tr></table>`;
  }
  let hover = -1;
  function foot() {
    const i = hover >= 0 ? hover : sel.size === 1 ? [...sel][0] : -1;
    const c = i >= 0 ? cells()[i] : null;
    const where = i >= 0 ? `Клетка <b>${cellName(page * PER + i)}</b>` : '';
    $('progFoot').innerHTML = !c ? (where ? where + '<span>Пусто</span>' : '<span>Выберите клетку и команду в палитре. Двойное нажатие — изменить значение</span>')
      : `${where}<span><b data-loc="${D.loc[c.op]}">${D.names[c.op]}</b></span><span>Код ${c.op}</span>${keyOf[c.op] ? `<span>Клавиша <kbd>${keyOf[c.op]}</kbd></span>` : ''}${sel.size > 1 ? `<span>Выбрано ${sel.size}</span>` : ''}`;
  }

  // ── Правка: всё через снимок для отмены ──
  const snap = () => { undo.push(JSON.stringify(P().pages)); if (undo.length > 50) undo.shift(); redo.length = 0; };
  const restore = (from, to) => { if (!from.length) return; to.push(JSON.stringify(P().pages)); P().pages = JSON.parse(from.pop()); page = Math.min(page, P().pages.length - 1); render(); };
  function put(op) {
    if (!sel.size) sel.add(0);
    snap();
    const text = TEXT.has(op) ? 'a' : undefined, num = NUM.has(op) ? 0 : undefined;
    [...sel].forEach(i => { cells()[i] = { op, text, num }; });
    // После одной клетки выбор идёт дальше по строке — как ввод текста
    if (sel.size === 1) { const i = [...sel][0]; sel.clear(); sel.add(Math.min(PER - 1, i + 1)); anchor = [...sel][0]; }
    render();
    if (text !== undefined && sel.size === 1) editValue([...sel][0] - 1);
  }
  function edit(cmd) {
    const idx = [...sel].sort((a, b) => a - b);
    if (cmd === 'undo') return restore(undo, redo);
    if (cmd === 'redo') return restore(redo, undo);
    if (cmd === 'multi') { multi = !multi; $('progMulti').setAttribute('aria-pressed', String(multi)); return; }
    if (!idx.length) return;
    if (cmd === 'copy' || cmd === 'cut') {
      const r0 = Math.min(...idx.map(i => Math.floor(i / COLS))), c0 = Math.min(...idx.map(i => i % COLS));
      clip = idx.map(i => [Math.floor(i / COLS) - r0, i % COLS - c0, cells()[i]]);
    }
    if (cmd === 'cut' || cmd === 'delete') { snap(); idx.forEach(i => { cells()[i] = null; }); }
    if (cmd === 'paste' && clip) {
      snap();
      const r = Math.floor(idx[0] / COLS), c = idx[0] % COLS;
      clip.forEach(([dr, dc, v]) => { if (r + dr < ROWS && c + dc < COLS) cells()[(r + dr) * COLS + c + dc] = v && { ...v }; });
    }
    render();
  }
  function shift(dx, dy) {
    snap();
    const src = cells(), out = blank();
    src.forEach((v, i) => { const r = Math.floor(i / COLS) + dy, c = i % COLS + dx; if (v && r >= 0 && r < ROWS && c >= 0 && c < COLS) out[r * COLS + c] = v; });
    P().pages[page] = out; render();
  }
  // Значение команды прямо в клетке
  function editValue(i) {
    const c = cells()[i]; if (!c || c.text === undefined) return;
    const el = cellEls[i];
    el.innerHTML = icon(c.op) + `<input class="prog-cell-input" value="${c.text}${c.num != null ? ' ' + c.num : ''}" maxlength="${NUM.has(c.op) ? 8 : 24}" aria-label="Значение">`;
    const inp = el.querySelector('input'); inp.focus(); inp.select();
    const done = ok => { if (ok) { const [t, n] = inp.value.trim().split(/\s+/); snap(); c.text = (t || 'a').slice(0, NUM.has(c.op) || c.op === C.DebugPause || c.op === C.DebugShow ? 3 : 24); if (NUM.has(c.op)) c.num = Number(n) || 0; } paintCell(i); };
    inp.onkeydown = e => { e.stopPropagation(); if (e.key === 'Enter') done(true); if (e.key === 'Escape') done(false); };
    inp.onblur = () => done(true);
  }

  // ── Выбор клеток: нажатие, Ctrl/«Несколько» — добавить, Shift — прямоугольник ──
  grid.addEventListener('click', e => {
    const el = e.target.closest('.prog-cell'); if (!el || e.target.matches('input')) return;
    const i = +el.dataset.i;
    if (e.shiftKey) {
      const r0 = Math.min(Math.floor(anchor / COLS), Math.floor(i / COLS)), r1 = Math.max(Math.floor(anchor / COLS), Math.floor(i / COLS));
      const c0 = Math.min(anchor % COLS, i % COLS), c1 = Math.max(anchor % COLS, i % COLS);
      sel.clear(); for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) sel.add(r * COLS + c);
    } else if (e.ctrlKey || e.metaKey || multi) { sel.has(i) ? sel.delete(i) : sel.add(i); anchor = i; }
    else { sel.clear(); sel.add(i); anchor = i; }
    render();
  });
  grid.addEventListener('dblclick', e => { const el = e.target.closest('.prog-cell'); if (el) editValue(+el.dataset.i); });
  grid.addEventListener('pointerover', e => { const el = e.target.closest('.prog-cell'); hover = el ? +el.dataset.i : -1; foot(); });
  grid.addEventListener('pointerleave', () => { hover = -1; foot(); });
  $('progCats').onclick = e => { const b = e.target.closest('[data-cat]'); if (b) { cat = b.dataset.cat; renderPalette(); } };
  $('progOps').onclick = e => { const b = e.target.closest('[data-op]'); if (b) put(+b.dataset.op); };
  $('progOps').addEventListener('pointerover', e => { const b = e.target.closest('[data-op]'); if (!b) return; const o = +b.dataset.op;
    $('progFoot').innerHTML = `<span><b data-loc="${D.loc[o]}">${D.names[o]}</b></span><span>Код ${o}</span>${keyOf[o] ? `<span>Клавиша <kbd>${keyOf[o]}</kbd></span>` : '<span>Нет горячей клавиши</span>'}`; });
  $('progOps').addEventListener('pointerleave', foot);
  $('progTabs').onclick = e => { const b = e.target.closest('[data-tab]'); if (!b) return;
    $('progTabs').querySelectorAll('button').forEach(x => x.setAttribute('aria-checked', String(x === b)));
    document.querySelectorAll('.prog-side [data-page]').forEach(p => { p.hidden = p.dataset.page !== b.dataset.tab; }); };
  document.querySelectorAll('#progDialog [data-edit]').forEach(b => b.onclick = () => edit(b.dataset.edit));
  document.querySelectorAll('#progDialog [data-shift]').forEach(b => b.onclick = () => { const [dx, dy] = b.dataset.shift.split(',').map(Number); shift(dx, dy); });
  $('progPrev').onclick = () => { page--; sel.clear(); render(); };
  $('progNext').onclick = () => { page++; sel.clear(); render(); };
  $('progAddPage').onclick = () => { snap(); P().pages.splice(page + 1, 0, blank()); page++; render(); };
  $('progDelPage').onclick = () => { snap(); P().pages.splice(page, 1); page = Math.max(0, page - 1); render(); };
  $('progBreak').onclick = () => { const b = P().breaks; sel.forEach(i => { const g = page * PER + i; b.has(g) ? b.delete(g) : b.add(g); }); render(); };
  $('progSave').onclick = () => { $('progState').textContent = 'Сохранено'; };

  // ── Запуск: то же, что ▶/■ в HUD ──
  function setState(s) {
    state = s;
    if (s === 'paused') { here = [...P().breaks][0] ?? 0; page = Math.floor(here / PER); }
    render();
    window.kernProg?.sync(s === 'running' || s === 'paused', P().name);
  }
  $('progGo').onclick = () => setState(state === 'running' || state === 'paused' ? 'stopped' : 'running');
  $('progPause').onclick = () => setState('paused');
  const step = d => { here = Math.min(page * PER + PER - 1, here + d); render(); };
  $('progStepIn').onclick = () => step(1); $('progStepOver').onclick = () => step(1); $('progStepOut').onclick = () => step(COLS - here % COLS);

  // ── Список программ: выбрать, переименовать, удалить, создать ──
  function renderList() {
    $('progListItems').innerHTML = programs.map((p, i) => `<li class="${i === cur ? 'is-on' : ''}"><button type="button" data-pick="${i}"><b>${p.name}</b><small>${p.pages.length} стр. · ${p.pages.flat().filter(Boolean).length} команд</small></button>
      <button type="button" class="icon-btn" data-rename="${i}" title="Переименовать"><svg><use href="#i-edit"/></svg></button><button type="button" class="icon-btn" data-del="${i}" title="Удалить"${programs.length === 1 ? ' disabled' : ''}><svg><use href="#i-trash"/></svg></button></li>`).join('');
  }
  const showList = on => { $('progList').hidden = !on; $('progPick').setAttribute('aria-expanded', String(on)); if (on) renderList(); };
  $('progPick').onclick = () => showList($('progList').hidden);
  $('progListClose').onclick = () => showList(false);
  $('progListItems').onclick = e => {
    const pick = e.target.closest('[data-pick]'), ren = e.target.closest('[data-rename]'), del = e.target.closest('[data-del]');
    if (pick) { cur = +pick.dataset.pick; page = 0; sel.clear(); undo.length = redo.length = 0; showList(false); render(); }
    if (del) { programs.splice(+del.dataset.del, 1); cur = Math.min(cur, programs.length - 1); renderList(); render(); }
    if (ren) {
      const li = ren.closest('li'), p = programs[+ren.dataset.rename];
      li.firstElementChild.replaceWith(Object.assign(document.createElement('input'), { className: 'input', value: p.name, maxLength: 24 }));
      const inp = li.querySelector('input'); inp.focus(); inp.select();
      const done = () => { p.name = inp.value.trim() || p.name; renderList(); render(); };
      inp.onkeydown = ev => { ev.stopPropagation(); if (ev.key === 'Enter') done(); if (ev.key === 'Escape') renderList(); };
      inp.onblur = done;
    }
  };
  $('progCreate').onsubmit = e => {
    e.preventDefault();
    const name = $('progCreateName').value.trim() || `Программа ${programs.length + 1}`;
    programs.push({ name, pages: [blank()], breaks: new Set() }); cur = programs.length - 1; page = 0;
    $('progCreateName').value = ''; showList(false); render();
  };

  // ── Клавиатура: клавиши команд (повтор перебирает группу), правка, стрелки по клеткам ──
  let lastKey = '', lastIdx = 0;
  document.addEventListener('keydown', e => {
    if ($('progDialog').hidden || e.target.matches('input, textarea')) return;
    const k = e.key.toLowerCase();
    if (e.key === 'Escape') { $('progList').hidden ? close() : showList(false); return; }
    if (e.ctrlKey || e.metaKey) {
      const m = { z: 'undo', y: 'redo', c: 'copy', x: 'cut', v: 'paste', я: 'undo', н: 'redo', с: 'copy', ч: 'cut', м: 'paste' }[k];
      if (m) { e.preventDefault(); edit(m); } return;
    }
    if (e.key === 'Delete') { edit('delete'); return; }
    if (e.key === 'F9') { e.preventDefault(); $('progBreak').click(); return; }
    if (e.key.startsWith('Arrow') && sel.size) {
      e.preventDefault();
      const i = [...sel].at(-1), d = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -COLS, ArrowDown: COLS }[e.key];
      sel.clear(); sel.add(Math.max(0, Math.min(PER - 1, i + d))); anchor = [...sel][0]; render(); return;
    }
    if (e.key === 'Enter' && sel.size === 1) { editValue([...sel][0]); return; }
    const name = (e.shiftKey ? 'Shift+' : '') + (e.code.startsWith('Key') ? e.code.slice(3) : e.code === 'Backspace' ? 'Backspace' : '');
    const group = D.keys.find(([kk]) => kk === name);
    if (!group) return;
    e.preventDefault();
    // Повтор той же клавиши заменяет команду в предыдущей клетке следующей из группы
    if (name === lastKey && sel.size === 1 && [...sel][0] > 0) { lastIdx = (lastIdx + 1) % group[1].length; sel.clear(); sel.add(anchor - 1); }
    else lastIdx = 0;
    lastKey = name; put(group[1][lastIdx]);
  });

  // ── Открыть и закрыть ──
  const close = () => { $('progDialog').hidden = true; showList(false); };
  window.openProg = () => { $('progDialog').hidden = false; renderPalette(); render(); };
  window.kernProgState = setState;
  $('progDialog').querySelector('[data-close]').onclick = close;
  document.querySelector('.prog-open').addEventListener('click', window.openProg);
  // Сброс сцены — только то, что отличается от исходного: лишняя перерисовка стёрла бы подсказки геймпада на кнопках
  window.kernScene.onReset(() => { if (state !== 'stopped') setState('stopped'); if (!$('progList').hidden) showList(false); sel.clear(); const t = $('progTabs').querySelector('[data-tab="ops"]'); if (t.getAttribute('aria-checked') !== 'true') t.click(); });
  window.kernScene.on(1, p => {
    const q = p.get('prog'); if (!q) return;
    $('progDialog').style.transition = 'none'; window.openProg();
    if (q === 'debug') setState('paused');
    if (q === 'run') setState('running');
    if (q === 'error') setState('failed');
    if (q === 'list') showList(true);
    if (q === 'debug') $('progTabs').querySelector('[data-tab="mem"]').click();
    if (q === 'sel') { [17, 18, 19, 33, 34, 35].forEach(i => sel.add(i)); render(); }
  });
})();
