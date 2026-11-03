// Серверные окна: OpenWindowPacket(WindowTag, Width, Height, Content). Клиент даёт затемнение, рамку Width×Height
// и кнопку закрытия (ServerWindowPresenter); всё внутри — дерево компонентов протокола MinesServer.Networking GUI:
// контейнеры DockPanel, Grid, Canvas, ScrollViewer, Panel; виджеты Text, Image, Line; ввод Selectable, Slider, TextBox, Dropdown.
// Присоединённые свойства — как в пакете: DockPanel.Dock, Grid.Row/Column, Canvas.X/Y. Style не задан — вид решает клиент.
// Ниже — окна зданий в коде дизайна: что сервер присылает и как клиент это рисует. ?srvwin=market|resp|gun|clan_list|clan_info
(() => {
  const $ = id => document.getElementById(id);

  // ── Короткая запись пакетов ──
  const T = (text, a = {}) => ({ t: 'Text', text, ...a });
  const I = (uri, w = 16, h = 16, a = {}) => ({ t: 'Image', uri, w, h, ...a });
  const L = (dir = 'h', a = {}) => ({ t: 'Line', dir, ...a });
  const Dock = (kids, a = {}) => ({ t: 'Dock', kids, ...a });
  const Grid = (cols, rows, kids, a = {}) => ({ t: 'Grid', cols, rows, kids, ...a });
  const Scroll = (kids, a = {}) => ({ t: 'Scroll', kids, ...a });
  const Panel = (kids, a = {}) => ({ t: 'Panel', kids, ...a });
  const Sel = (text, on, a = {}) => ({ t: 'Selectable', text, on, ...a });
  const Slider = (min, max, step, value, a = {}) => ({ t: 'Slider', min, max, step, value, ...a });
  const Box = (value, a = {}) => ({ t: 'TextBox', value, ...a });
  const Drop = (items, index, a = {}) => ({ t: 'Dropdown', items, index, ...a });
  // Кнопка в протоколе — Text с OnClickContext; без Style клиент рисует её кнопкой
  const Btn = (text, a = {}) => T(text, { click: true, ...a });
  const cell = (row, col, node) => ({ ...node, row, col });

  // Шапка окна — тоже часть пакета: значок, название и строка пояснения (у здания пояснения нет — оно в строке здания)
  const head = (icon, title, meta) => Dock([
    I(icon, 32, 32, { dock: 'Left' }),
    T(`<b>${title}</b>`, { dock: 'Top', role: 'title' }),
    ...(meta ? [T(meta, { dock: 'Top', role: 'meta' })] : []),
  ], { dock: 'Top', role: 'head' });
  // Строка здания над шапкой: «Здание ···· координаты»; пунктир между ними — Line протокола
  const rail = coords => Dock([T('Здание', { dock: 'Left' }), T(coords, { dock: 'Right' }), L('h')], { dock: 'Top', role: 'rail' });

  // Груз — тот же, что в HUD (состояние «Все»); цена — с сервера
  const CRYSTALS = [
    ['green', 'Зелёные', 2340, 12], ['blue', 'Синие', 1204, 18], ['red', 'Красные', 96, 40],
    ['violet', 'Фиолетовые', 12, 95], ['white', 'Белые', 3, 260], ['cyan', 'Голубые', 57, 70],
  ];
  // Режим стенда «много кристаллов и денег» (?rich=1|2|3, список «Груз и деньги» в панели стенда): сумма продажи ≈ $99 млрд (КК), ×1000 (ККК), ×1 000 000 (КККК)
  const RICH_CRYSTALS = [
    ['green', 'Зелёные', 3900000000, 12], ['blue', 'Синие', 1600000000, 18], ['red', 'Красные', 120000000, 40],
    ['violet', 'Фиолетовые', 55000000, 95], ['white', 'Белые', 9000000, 260], ['cyan', 'Голубые', 160000000, 70],
  ];
  let crystals = CRYSTALS, capacity = 1800, sold, total, pcs, kinds, rich = 0;
  function setRich(level) {
    const f = level ? 1000 ** (level - 1) : 1;
    rich = level; crystals = level ? RICH_CRYSTALS.map(([id, name, have, price]) => [id, name, have * f, price]) : CRYSTALS; capacity = level ? 10000000000 * f : 1800;
    sold = crystals.map(([, , have], i) => level || i < 2 ? have : Math.round(have / 2));      // богатый груз продаётся целиком
    total = crystals.reduce((a, [, , , price], i) => a + price * sold[i], 0);
    pcs = sold.reduce((a, b) => a + b, 0); kinds = sold.filter(Boolean).length;
  }
  setRich(0);
  // Бокс: в образце — синие целиком и половина красных
  const inBox = crystals.map(([, , have], i) => i === 1 ? have : i === 2 ? Math.round(have / 2) : 0);
  // Предметы инвентаря: строка — Image по имени предмета, Slider количества; в образце — полимер наполовину и вся чёрная скала
  const boxItems = (window.kernItems || []).map(([id, have, name]) => [id, name, have, id === 'poly' ? 20 : id === 'geoBlackRock' ? have : 0]);
  const afterSale = () => Math.round(Math.max(...crystals.map(([, , have], i) => have - sold[i])) * 100 / capacity);
  const plural = (n, one, few, many) => { const a = n % 10, b = n % 100; return a === 1 && b !== 11 ? one : a >= 2 && a <= 4 && (b < 12 || b > 14) ? few : many; };
  // Разряды — тонкой шпацией, как в остальном интерфейсе
  const num = n => n.toLocaleString('ru-RU').replace(/\s/g, ' ');
  const money = n => '$' + num(n);

  const WINDOWS = {
    // Рынок: продажа кристаллов из груза. Строка — кристалл, сколько в грузе, цена, ползунок количества, сумма
    get market() { return { w: 640, h: 560, root: Dock([
      rail('1154:903'),
      head('img/items/market.png', 'Рынок'),
      Dock([Btn('Продать', { dock: 'Left', on: true }), Btn('Купить', { dock: 'Left' }), T('')], { dock: 'Top', role: 'tabs' }),
      Dock([
        Panel([T('Итого к получению', { role: 'cap' }), T(money(total), { role: 'sum' })], { dock: 'Left', role: 'total' }),
        Panel([T(`${num(pcs)} шт // ${kinds} ${plural(kinds, 'вид', 'вида', 'видов')}`), T(`Груз после продажи — ${afterSale()}%`)], { dock: 'Left', role: 'annot' }),
        Btn('Продать', { dock: 'Right', primary: true, disabled: !pcs }), Btn('Продать всё', { dock: 'Right', disabled: crystals.every(c => !c[2]) }), T(''),
      ], { dock: 'Bottom', role: 'foot' }),
      // Колонки: значок, название (остаток ширины), в грузе, цена, ползунок, сколько продаётся, сумма
      Scroll([Grid([46, 0, 84, 72, 96, 88, 112], [0, ...crystals.map(() => 0)], [
        cell(0, 0, T('', { role: 'cap' })), cell(0, 1, T('Кристалл', { role: 'cap' })), cell(0, 2, T('В грузе', { role: 'cap num' })), cell(0, 3, T('Цена / шт', { role: 'cap num' })),
        cell(0, 4, T('Продать', { role: 'cap' })), cell(0, 5, T('', { role: 'cap' })), cell(0, 6, T('Сумма', { role: 'cap num' })),
        ...crystals.flatMap(([id, name, have, price], i) => {
          const r = i + 1, off = have ? '' : ' off';
          return [
            cell(r, 0, I(`img/crystals/${id}.png`, 20, 20, { role: off.trim() })),
            cell(r, 1, T(name, { role: off.trim() })),
            cell(r, 2, T(num(have), { role: 'num' + off })),
            cell(r, 3, T(money(price), { role: 'num' + off })),
            cell(r, 4, Slider(0, have, 1, sold[i], { readonly: !have })),
            cell(r, 5, T(num(sold[i]), { role: 'num strong' + off })),
            cell(r, 6, T(money(price * sold[i]), { role: 'num strong' + off })),
          ];
        }),
      ], { role: 'table' })]),
    ]) }; },

    // Респаун: точка возрождения. Чужим — цена и согласие, хозяину — та же форма плюс цена для чужих
    resp: { w: 440, h: 360, root: Dock([
      rail('1144:902'),
      head('img/items/resp.png', 'Респаун'),
      Dock([Btn('Сохранить', { dock: 'Right', primary: true }), Btn('Отмена', { dock: 'Right' }), T('')], { dock: 'Bottom', role: 'foot' }),
      Grid([0, 0], [0, 0, 0, 0], [
        cell(0, 0, T('Возрождение здесь', { role: 'label' })), cell(0, 1, T(money(120), { role: 'num strong' })),
        cell(1, 0, T('Своим и союзникам', { role: 'label' })), cell(1, 1, T('бесплатно', { role: 'num' })),
        cell(2, 0, T('Расстояние', { role: 'label' })), cell(2, 1, T('640 клеток', { role: 'num' })),
        cell(3, 0, T('Заряд', { role: 'label' })), cell(3, 1, T('18 / 20 возрождений', { role: 'num' })),
      ], { dock: 'Top', role: 'facts' }),
      L('h', { dock: 'Top' }),
      Sel('Возрождаться на этом респауне', true, { dock: 'Top' }),
      Dock([T('Цена для чужих', { dock: 'Left', role: 'label' }), Box('120', { dock: 'Right', role: 'short' }), Slider(0, 1000, 10, 120)], { dock: 'Top', role: 'row' }),
    ]) },

    // Пушка: защита территории. Заряд, цель, заправка кристаллами
    gun: { w: 460, h: 420, root: Dock([
      rail('1172:901'),
      head('img/items/gun.png', 'Пушка'),
      Dock([Btn('Заправить', { dock: 'Right', primary: true }), Box('500', { dock: 'Right', role: 'short' }), T('Голубые, шт', { role: 'label' })], { dock: 'Bottom', role: 'foot' }),
      Grid([0, 0, 0], [0, 0], [
        cell(0, 0, T('Урон', { role: 'label' })), cell(0, 1, T('Радиус', { role: 'label' })), cell(0, 2, T('Перезарядка', { role: 'label' })),
        cell(1, 0, T('<b>40</b>', { role: 'big' })), cell(1, 1, T('<b>8</b> клеток', { role: 'big' })), cell(1, 2, T('<b>1,2</b> с', { role: 'big' })),
      ], { dock: 'Top', role: 'stats' }),
      Dock([T('Заряд', { dock: 'Left', role: 'label' }), T('3 200 / 5 000', { dock: 'Right', role: 'num' }), Slider(0, 5000, 1, 3200, { readonly: true })], { dock: 'Top', role: 'row' }),
      L('h', { dock: 'Top' }),
      Dock([T('Стрелять по', { dock: 'Left', role: 'label' }), Drop(['Всем чужим', 'Врагам клана', 'Никому'], 1)], { dock: 'Top', role: 'row' }),
      Sel('Предупреждать в чате клана', true, { dock: 'Top' }),
      Sel('Пускать союзников без проверки', false, { dock: 'Top' }),
    ]) },

    // Кланы: тот же состав, что шлёт DummyClanManager (clan_list) — значок, название-кнопка, описание; без Style
    clan_list: { w: 460, h: 420, root: Dock([
      head('img/items/clans.png', 'Доступные кланы', 'Вступить можно по заявке'),
      Scroll([
        ['FED', 'Федерация', 'Копаем глубоко, держим восток. Набор открыт.', '214'],
        ['DVM', 'Дивизия', 'Пушки, тюрьма, порядок. Только с опытом.', '96'],
        ['ORE', 'Орден руды', 'Тихие шахтёры. Без войн.', '41'],
        ['RED', 'Красный сектор', 'Воюем за центр карты.', '158'],
      ].map(([tag, name, desc, n]) => Panel([
        Dock([I('img/clan.png', 24, 24, { dock: 'Left' }), T(n, { dock: 'Right', role: 'num' }), Btn(`<b>${name}</b> <span>${tag}</span>`, { role: 'link' })], { role: 'item-head' }),
        T(desc, { role: 'note' }),
      ], { role: 'item' })), { role: 'list' }),
    ]) },

    // Создание бокса: Basket.OpenBoxGui — CrystalConfig «останется / будет в боксе» на шесть кристаллов груза, текст и кнопка
    // dropbox:%M% (значения ползунков). Бокс встаёт в клетку перед роботом; строки источника нет — координаты бокса игроку не нужны
    box: { w: 560, h: 600, root: Dock([
      head('img/cells/Box.png', 'Создание бокса'),
      T('Выберите ползунками, сколько кристаллов и предметов положить в бокс.', { dock: 'Top', role: 'text' }),
      Dock([
        Panel([T('Кристаллов в боксе', { role: 'cap' }), T(num(inBox.reduce((a, b) => a + b, 0)), { role: 'sum' })], { dock: 'Left', role: 'total' }),
        Panel([T(`${inBox.filter(Boolean).length} ${plural(inBox.filter(Boolean).length, 'вид', 'вида', 'видов')} кристаллов`), T(`Предметов — ${num(boxItems.reduce((a, it) => a + it[3], 0))} шт`)], { dock: 'Left', role: 'annot' }),
        Btn('В бокс', { dock: 'Right', primary: true, disabled: !inBox.some(Boolean) && !boxItems.some(it => it[3]) }), T(''),
      ], { dock: 'Bottom', role: 'foot' }),
      // Колонки: значок, название (остаток), останется, ползунок, будет в боксе
      Scroll([Grid([46, 0, 84, 150, 96], [0, ...crystals.map(() => 0)], [
        cell(0, 0, T('', { role: 'cap' })), cell(0, 1, T('Кристалл', { role: 'cap' })), cell(0, 2, T('Останется', { role: 'cap num' })),
        cell(0, 3, T('', { role: 'cap' })), cell(0, 4, T('Будет в боксе', { role: 'cap num' })),
        ...crystals.flatMap(([id, name, have], i) => {
          const r = i + 1, off = have ? '' : ' off';
          return [
            cell(r, 0, I(`img/crystals/${id}.png`, 20, 20, { role: off.trim() })),
            cell(r, 1, T(name, { role: off.trim() })),
            cell(r, 2, T(num(have - inBox[i]), { role: 'num' + off })),
            cell(r, 3, Slider(0, have, 1, inBox[i], { readonly: !have })),
            cell(r, 4, T(num(inBox[i]), { role: 'num strong' + off })),
          ];
        }),
      ], { role: 'table' }),
      Grid([46, 0, 84, 150, 96], [0, ...boxItems.map(() => 0)], [
        cell(0, 0, T('', { role: 'cap' })), cell(0, 1, T('Предмет', { role: 'cap' })), cell(0, 2, T('Останется', { role: 'cap num' })),
        cell(0, 3, T('', { role: 'cap' })), cell(0, 4, T('Будет в боксе', { role: 'cap num' })),
        ...boxItems.flatMap(([id, name, have, put], i) => {
          const r = i + 1;
          return [
            cell(r, 0, I(`img/items/${id}.png`, 24, 24)),
            cell(r, 1, T(name)),
            cell(r, 2, T(num(have - put), { role: 'num' })),
            cell(r, 3, Slider(0, have, 1, put)),
            cell(r, 4, T(num(put), { role: 'num strong' })),
          ];
        }),
      ], { role: 'table' })]),
    ]) },

    // Мой клан: clan_info у DummyClanManager — название, номер, описание, «Покинуть клан»
    clan_info: { w: 420, h: 300, root: Dock([
      head('img/items/clans.png', 'Мой клан', 'Клан FED // №12'),
      Dock([Btn('Покинуть клан', { dock: 'Right', danger: true }), T('')], { dock: 'Bottom', role: 'foot' }),
      T('Копаем глубоко, держим восток. Набор открыт.', { dock: 'Top', role: 'text' }),
      Grid([0, 0], [0, 0], [
        cell(0, 0, T('Участников', { role: 'label' })), cell(0, 1, T('214', { role: 'num' })),
        cell(1, 0, T('Казна', { role: 'label' })), cell(1, 1, T(money(1840200), { role: 'num strong' })),
      ], { dock: 'Top', role: 'facts' }),
    ]) },
  };

  // Число длиннее пяти знаков сервер присылает полным, клиент сокращает тысячами — K, KK, KKK, KKKK (перед буквами не больше 99 999):
  // окно не растёт от данных. Полное значение — по наведению или касанию
  const short = n => { let k = 0; while (n >= 1e5 && k < 4) { n /= 1e3; k++; } return num(Math.floor(n)) + (k ? ' ' + 'K'.repeat(k) : ''); };
  function cutNumber(e) {
    const m = e.textContent.trim().match(/^(\$?)([\d\s\u202f\u00a0]+)$/);
    if (!m) return;
    const v = Number(m[2].replace(/\D/g, ''));
    if (v < 1e5) return;
    e.innerHTML = `<span class="pk-cut" tabindex="0" data-full="${m[1]}${num(v)}">${m[1]}${short(v)}</span>`;
  }

  // ── Отрисовка дерева: один узел пакета — один элемент ──
  const mk = (tag, cls) => { const e = document.createElement(tag); if (cls) e.className = cls; return e; };
  function render(n) {
    let e;
    switch (n.t) {
      case 'Text':
        e = mk(n.click ? 'button' : 'p', n.click ? 'pk-btn' : 'pk-text');
        if (n.click) e.type = 'button';
        e.innerHTML = n.text;
        e.dataset.srv = '';
        if (n.on) e.classList.add('is-on');
        if (n.primary) e.classList.add('is-primary');
        if (n.danger) e.classList.add('is-danger');
        if (n.disabled) e.disabled = true;
        if (/\b(num|sum)\b/.test(n.role || '')) cutNumber(e);
        break;
      case 'Image':
        e = mk('img', 'pk-img'); e.src = n.uri; e.alt = ''; e.width = n.w; e.height = n.h; break;
      case 'Line':
        e = mk('hr', 'pk-line pk-line--' + n.dir); break;
      case 'Dock':
        e = dock(n.kids); e.classList.add('pk-dock'); break;
      case 'Grid':
        e = mk('div', 'pk-grid');
        e.style.gridTemplateColumns = n.cols.map(c => c ? c + 'px' : (n.cols.filter(x => !x).length > 1 ? 'auto' : '1fr')).join(' ');
        n.kids.forEach(k => {
          const c = render(k); c.style.gridRow = k.row + 1; c.style.gridColumn = k.col + 1;
          if (k.col === n.cols.length - 1) c.classList.add('pk--last');
          e.append(c);
        });
        break;
      case 'Scroll':
        e = mk('div', 'pk-scroll'); n.kids.forEach(k => e.append(render(k))); break;
      case 'Panel':
        e = mk('div', 'pk-panel'); n.kids.forEach(k => e.append(render(k))); break;
      case 'Selectable':
        e = mk('label', 'pk-sel'); e.innerHTML = `<input type="checkbox"${n.on ? ' checked' : ''}><span data-srv>${n.text}</span>`; break;
      case 'Slider':
        e = mk('input', 'pk-slider'); Object.assign(e, { type: 'range', min: n.min, max: n.max, step: n.step, value: n.value });
        // --f — доля хода бегунка 0..1; CSS переводит её в ширину заливки с поправкой на ход бегунка (srvgui.css). Пустой диапазон — 0, не NaN
        const frac = () => n.max > n.min ? (e.value - n.min) / (n.max - n.min) : 0;
        e.style.setProperty('--f', frac());
        e.oninput = () => e.style.setProperty('--f', frac());
        if (n.readonly) e.disabled = true;
        break;
      case 'TextBox':
        e = mk('input', 'pk-box'); e.value = n.value; break;
      case 'Dropdown':
        e = mk('select', 'pk-drop'); e.dataset.srv = ''; n.items.forEach((t, i) => { const o = new Option(t, i); o.selected = i === n.index; e.append(o); }); break;
    }
    if (n.role) n.role.split(' ').forEach(r => e.classList.add('pk--' + r));
    return e;
  }

  // DockPanel: дети встают по краям в порядке пакета, последний заполняет остаток (LastChildFill)
  function dock(kids) {
    const box = mk('div', 'pk-dockbox');
    if (!kids.length) return box;
    const [first, ...rest] = kids;
    if (!rest.length) { box.append(render(first)); box.lastChild.classList.add('pk-fill'); return box; }
    const side = first.dock || 'Left';
    box.classList.add('pk-dock--' + side.toLowerCase());
    const inner = dock(rest); inner.classList.add('pk-fill');
    box.append(render(first), inner);
    return box;
  }

  function open(tag) {
    const layer = $('srvWinLayer');
    const def = WINDOWS[tag];
    if (!def) { layer.hidden = true; return; }
    const frame = layer.querySelector('.srv-pk');
    frame.style.width = def.w + 'px';
    frame.style.height = def.h + 'px';
    frame.dataset.tag = tag;
    const content = frame.querySelector('.srv-pk-content');
    content.replaceChildren(render(def.root));
    layer.hidden = false;
    $('standSrvWin').value = tag;
  }
  window.openSrvWin = open;
  // Кнопка «Бокс» в панели груза — сервер отвечает окном создания бокса
  $('boxBtn').onclick = () => open('box');

  $('srvWinLayer').querySelector('[data-close]').onclick = () => open('');
  $('standSrvWin').onchange = e => open(e.target.value);
  window.kernScene.on(1, q => { if (q.get('srvwin')) { $('srvWinLayer').style.transition = 'none'; open(q.get('srvwin')); } });
  // Стенд: «Груз рынка» — обычный или предельно богатый; открытое окно рынка перерисовывается на месте
  const applyRich = level => { setRich(level); window.kernHudRich?.(level); $('standRich').value = level || ''; if ($('srvWinLayer').querySelector('.srv-pk').dataset.tag === 'market' && !$('srvWinLayer').hidden) open('market'); };
  $('standRich').onchange = e => applyRich(Number(e.target.value) || 0);
  window.kernScene.on(0, q => { if (q.get('rich')) applyRich(Math.min(3, Number(q.get('rich')) || 0)); });
  window.kernScene.onReset(() => { open(''); if (rich) applyRich(0); });
})();
