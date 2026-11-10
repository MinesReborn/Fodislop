// Мир за HUD — из настоящих текстур клеток Kern (Assets/Textures/Cells), клетка 32 px.
// Карта детерминирована (зерно), чтобы снимки сравнивались без шума.
(() => {
  const CELL = 32;
  const W = 220, H = 140;                 // карта шире кадра: её же рисует миникарта
  // pass — проходима (пустота, дорога), sand — сыпучая, road — дорога или опора, alive — живая клетка («живка»), crystal — кристалл
  const TYPES = {
    empty: { file: 'Empty', mm: '#2a1d12', pass: true },
    rock: { file: 'Rock', mm: '#2b2f86' },
    grock: { file: 'GRock', mm: '#3b4a3b' },
    heavy: { file: 'HeavyRock', mm: '#3a3a44' },
    black: { file: 'BlackRock', mm: '#1d1f24' },
    sand: { file: 'YellowSand', mm: '#c99b4f', sand: true },
    rusty: { file: 'RustySand', mm: '#9a5a33', sand: true },
    white: { file: 'WhiteSand', mm: '#b9b6ad', sand: true },
    boulder: { file: 'Boulder1', mm: '#5d5a55' },
    boulder2: { file: 'Boulder2', mm: '#5d5a55' },
    road: { file: 'Road', mm: '#6d6a64', pass: true, road: true },
    support: { file: 'Support', mm: '#8a7a5a', road: true },
    g: { file: 'Green', mm: '#3ee86b', crystal: 'g' },
    b: { file: 'Blue', mm: '#6f8cff', crystal: 'b' },
    r: { file: 'Red', mm: '#ff7a74', crystal: 'r' },
    c: { file: 'Cyan', mm: '#39e8f0', crystal: 'c' },
    w: { file: 'White', mm: '#eef1f3', crystal: 'w' },
    v: { file: 'Violet', mm: '#f15fd6', crystal: 'v' },
    // Живые клетки: цвет на карте — mapColor из cells.json клиента; атлас — пять вариантов столбиком, рисуется верхний
    aliveC: { file: 'AliveCyan', mm: '#327698', alive: 'c' },
    aliveR: { file: 'AliveRed', mm: '#993f37', alive: 'r' },
    aliveV: { file: 'AliveViol', mm: '#815998', alive: 'v' },
    aliveB: { file: 'AliveBlue', mm: '#204993', alive: 'b' },
  };
  let seed = 7;
  const rand = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296;
  const noise = (x, y) => { const s = Math.sin(x * 12.9898 + y * 78.233) * 43758.5453; return s - Math.floor(s); };
  const smooth = (x, y, f) => {
    const xi = Math.floor(x / f), yi = Math.floor(y / f), tx = x / f - xi, ty = y / f - yi;
    const a = noise(xi, yi), b = noise(xi + 1, yi), c = noise(xi, yi + 1), d = noise(xi + 1, yi + 1);
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
  };

  // Порода слоями, жилы кристаллов, ходы игрока, дорога с опорами
  const map = [];
  for (let y = 0; y < H; y++) {
    const row = [];
    for (let x = 0; x < W; x++) {
      const n = smooth(x, y, 6) * .85 + noise(x, y) * .15, m = smooth(x + 50, y + 30, 3);
      let t = y < 30 ? (n > .55 ? 'sand' : n > .35 ? 'rusty' : 'grock') : y < 80 ? (n > .6 ? 'rock' : n > .42 ? 'heavy' : 'sand') : (n > .5 ? 'black' : 'rock');
      if (m > .84) t = ['g', 'b', 'r', 'c', 'w', 'v'][Math.floor(smooth(x, y, 3) * 6) % 6];
      else if (m < .12) t = rand() > .5 ? 'boulder' : 'boulder2';
      row.push(t);
    }
    map.push(row);
  }
  let PX = 110, PY = 70;                   // игрок
  const carve = (x, y) => { if (map[y] && map[y][x]) map[y][x] = 'empty'; };
  for (let x = PX - 40; x <= PX + 10; x++) { carve(x, PY); carve(x, PY + 1); }       // штрек
  for (let y = PY - 24; y <= PY; y++) { carve(PX + 10, y); carve(PX + 11, y); }      // шахта вверх
  for (let x = PX - 6; x <= PX + 3; x++) for (let y = PY - 3; y <= PY + 2; y++) carve(x, y); // забой
  for (let x = PX - 40; x <= PX - 8; x++) map[PY + 2][x] = 'road';
  for (let x = PX - 38; x <= PX - 8; x += 6) map[PY - 1][x] = 'support';
  for (let i = 0; i < 9; i++) map[PY - 5 + (i % 3)][PX + 4 + Math.floor(i / 3)] = ['c', 'g', 'b'][i % 3];
  // Живки: гнёзда неправильной формы — одно у забоя, остальные дальше по карте
  [[PX + 14, PY + 5, 'aliveR'], [PX - 30, PY + 22, 'aliveC'], [PX + 46, PY - 30, 'aliveV'], [PX + 60, PY + 40, 'aliveB'], [PX - 70, PY - 40, 'aliveC']].forEach(([gx, gy, t]) => {
    for (let y = -3; y <= 3; y++) for (let x = -3; x <= 3; x++) if (map[gy + y]?.[gx + x] && x * x + y * y + noise(gx + x, gy + y) * 6 < 10) map[gy + y][gx + x] = t;
  });

  // Давность разведки: клиент видит мир чанками 32×32, которые присылает сервер. Контейнер карты помнит, когда чанк пришёл
  // последний раз (seen, мс; 0 — не приходил ни разу): карта рисует давнее бледнее, неполученное не рисует.
  // В образце: вокруг игрока — сейчас, дальше — минуты, часы, сутки назад, в углах — не был ни разу
  const CHUNK = 32, now = Date.now();
  const seen = Array.from({ length: Math.ceil(H / CHUNK) }, (_, cy) => Array.from({ length: Math.ceil(W / CHUNK) }, (_, cx) => {
    const d = Math.max(Math.abs(cx - Math.floor(PX / CHUNK)), Math.abs(cy - Math.floor(PY / CHUNK)));
    const ago = [0, 6e5, 3 * 36e5, 2 * 864e5][Math.min(3, d)];
    return (cx === 0 || cx === 6) && (cy === 0 || cy === 4) ? 0 : now - ago;
  }));

  const images = {};
  const load = Object.values(TYPES).map(t => new Promise(res => {
    const img = new Image(); img.onload = res; img.onerror = res; img.src = `img/cells/${t.file}.png`; images[t.file] = img;
  }));
  const robot = new Image();
  load.push(new Promise(res => { robot.onload = res; robot.onerror = res; }));
  robot.src = 'img/bee.png';

  // Другие роботы рядом (позиции — пример; ник и клан — из данных игрока)
  const world = { map, PX, PY, TYPES, CELL, CHUNK, seen, marks: [],
    bots: [{ nick: 'ШАХТЁР-102', clan: true, x: PX - 12, y: PY }, { nick: 'ROCKBOT', clan: false, x: PX - 4, y: PY - 2 }] };
  world.draw = () => {
    const canvas = document.getElementById('world');
    const frame = document.getElementById('frame');
    // Холст в физических пикселях экрана: кадр стенда масштабирован под окно, иначе клетки растягиваются швами
    const W = frame.offsetWidth, H = frame.offsetHeight;
    const r = (frame.getBoundingClientRect().width / W || 1) * (devicePixelRatio || 1);
    canvas.width = Math.round(W * r); canvas.height = Math.round(H * r);
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    const cols = Math.ceil(W / CELL) + 1, rows = Math.ceil(H / CELL) + 1;
    const ox = PX - Math.floor(cols / 2), oy = PY - Math.floor(rows / 2);
    const offX = (W / 2) - (PX - ox) * CELL - CELL / 2, offY = (H / 2) - (PY - oy) * CELL - CELL / 2;
    world.view = { ox, oy, offX, offY };
    world.touch(ox, oy, ox + cols, oy + rows);                    // видимое сервер присылает сейчас
    const px = v => Math.round(v * r);                           // края клеток на целых пикселях — без щелей
    // Атлас выше своей ширины (живые клетки — варианты столбиком) — берётся верхний квадрат
    const cell = (img, x, y) => ctx.drawImage(img, 0, 0, img.naturalWidth, Math.min(img.naturalWidth, img.naturalHeight), px(offX + x * CELL), px(offY + y * CELL),
      px(offX + (x + 1) * CELL) - px(offX + x * CELL), px(offY + (y + 1) * CELL) - px(offY + y * CELL));
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
      const t = TYPES[map[oy + y]?.[ox + x] || 'black'];
      const img = images[t.file];
      if (img?.naturalWidth) cell(img, x, y);
    }
    ctx.imageSmoothingEnabled = false;
    if (robot.complete) { cell(robot, PX - ox, PY - oy); world.bots.forEach(b => cell(robot, b.x - ox, b.y - oy)); }
    // Куда смотрит робот — треугольник у края его клетки
    const [fx, fy] = STEP[world.facing], cx = offX + (PX - ox + .5) * CELL, cy = offY + (PY - oy + .5) * CELL, e = CELL * .62, w = CELL * .18;
    ctx.fillStyle = '#e8b04a'; ctx.beginPath();
    ctx.moveTo(px(cx + fx * (e + w)), px(cy + fy * (e + w)));
    ctx.lineTo(px(cx + fx * e - fy * w), px(cy + fy * e + fx * w));
    ctx.lineTo(px(cx + fx * e + fy * w), px(cy + fy * e - fx * w));
    ctx.fill();
    world.drawMinimap();
    world.onDraw?.();
  };
  world.drawMinimap = () => {
    const mm = document.getElementById('minimap');
    if (!mm) return;
    // Режим — тот же, что на карте мира (js/mapmodes.js)
    const M = window.kernMapModes, s = 2;
    const ctx = mm.getContext('2d');
    const cw = mm.width / s, ch = mm.height / s;
    ctx.fillStyle = M?.background() || '#000'; ctx.fillRect(0, 0, mm.width, mm.height);
    for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
      const gx = PX - Math.floor(cw / 2) + x, gy = PY - Math.floor(ch / 2) + y, t = map[gy]?.[gx];
      const c = M ? (t ? M.color(t, gx, gy) : null) : TYPES[t || 'black'].mm;
      if (!c) continue;
      ctx.fillStyle = c; ctx.fillRect(x * s, y * s, s, s);
    }
    ctx.fillStyle = 'rgba(11,15,18,.35)'; ctx.fillRect(0, 0, mm.width, mm.height);
    // Цели на миникарте: миссия и своя метка — в краю, если дальше видимого
    world.marks.filter(m => !m.off?.()).forEach(m => {
      const x = Math.max(3, Math.min(mm.width - 3, mm.width / 2 + (m.x - PX) * s)), y = Math.max(3, Math.min(mm.height - 3, mm.height / 2 + (m.y - PY) * s));
      ctx.fillStyle = m.color; ctx.fillRect(x - 3, y - 3, 6, 6);
    });
    ctx.fillStyle = '#e8b04a'; ctx.fillRect(mm.width / 2 - 2, mm.height / 2 - 2, 4, 4);
  };
  world.cellAt = (px, py) => {
    const v = world.view; if (!v) return null;
    const x = v.ox + Math.floor((px - v.offX) / CELL), y = v.oy + Math.floor((py - v.offY) / CELL);
    return map[y] && map[y][x] ? { x, y, type: map[y][x] } : null;
  };
  world.dig = (x, y) => { const t = TYPES[map[y][x]]; map[y][x] = 'empty'; world.touch(x, y, x, y); world.draw(); return t; };
  // Чанки прямоугольника клеток пришли от сервера — запомнить время
  world.touch = (x0, y0, x1, y1) => {
    const t = Date.now();
    for (let cy = Math.max(0, Math.floor(y0 / CHUNK)); cy <= Math.min(seen.length - 1, Math.floor(y1 / CHUNK)); cy++)
      for (let cx = Math.max(0, Math.floor(x0 / CHUNK)); cx <= Math.min(seen[0].length - 1, Math.floor(x1 / CHUNK)); cx++) seen[cy][cx] = t;
  };
  world.seenAt = (x, y) => seen[Math.floor(y / CHUNK)]?.[Math.floor(x / CHUNK)] ?? 0;
  // Стик: малое отклонение — повернуться, сильное — шаг (в пустоту; в породу — только поворот)
  const STEP = { up: [0, -1], down: [0, 1], left: [-1, 0], right: [1, 0] };
  world.facing = 'right';
  world.face = dir => { if (world.facing !== dir) { world.facing = dir; world.draw(); } };
  world.step = dir => {
    if (world.locked) return;                                    // подсказка обучения с линзой держит робота (js/tutorial.js)
    world.facing = dir;
    const [dx, dy] = STEP[dir];
    if (map[PY + dy]?.[PX + dx] === 'empty') { PX += dx; PY += dy; world.PX = PX; world.PY = PY; }
    world.draw();
  };
  world.ready = Promise.all(load);
  window.kernWorld = world;
})();
