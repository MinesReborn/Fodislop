// Разрез шахты для витрин, обложек и соцсетей — из настоящих текстур клеток игры (visual/kern-ui/img/cells), тем же
// способом, что мир стенда (kern-ui/js/world.js): клетка 32 px, порода пластами по глубине, жилы кристаллов, штрек с опорами и роботы.
// Детерминирован: одно зерно — одна картинка, экспорт PNG повторяется побайтно.
//
//   await KernScene.paint(canvas, { width, height, cell: 32, seed: 7, depth: 0, shaft: 0.62, bots: 3, carve: true })
//     width/height — размер в CSS-пикселях (холст сам берёт devicePixelRatio);
//     moment: true — игровой момент (см. moment) вместо разреза; fx, fy, dir — его точка внимания;
//     cell — размер клетки на картинке; depth — с какой глубины (в клетках) начинается разрез; shaft — где по ширине вертикальный ствол (0..1, null — без ствола).
(() => {
  const here = new URL('.', document.currentScript.src);
  const IMG = new URL('../../../kern-ui/img/', here);
  const TYPES = {
    empty: 'Empty', rock: 'Rock', grock: 'GRock', heavy: 'HeavyRock', black: 'BlackRock', sand: 'YellowSand', rusty: 'RustySand',
    boulder: 'Boulder1', boulder2: 'Boulder2', road: 'Road', support: 'Support', military: 'MilitaryBlock', box: 'Box',
    g: 'Green', b: 'Blue', r: 'Red', c: 'Cyan', w: 'White', v: 'Violet',
  };
  const cache = {};
  const load = src => cache[src] ||= new Promise(res => { const i = new Image(); i.onload = () => res(i); i.onerror = () => res(null); i.src = src; });
  const noise = (x, y, s) => { const v = Math.sin(x * 12.9898 + y * 78.233 + s * 3.17) * 43758.5453; return v - Math.floor(v); };
  const smooth = (x, y, f, s) => {
    const xi = Math.floor(x / f), yi = Math.floor(y / f), tx = x / f - xi, ty = y / f - yi;
    const a = noise(xi, yi, s), b = noise(xi + 1, yi, s), c = noise(xi, yi + 1, s), d = noise(xi + 1, yi + 1, s);
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
  };

  // Порода по глубине — те же пороги, что в мире стенда
  function rockAt(x, y, s) {
    const n = smooth(x, y, 6, s) * .85 + noise(x, y, s) * .15, m = smooth(x + 50, y + 30, 3, s);
    let t = y < 30 ? (n > .55 ? 'sand' : n > .35 ? 'rusty' : 'grock') : y < 80 ? (n > .6 ? 'rock' : n > .42 ? 'heavy' : 'sand') : (n > .5 ? 'black' : 'rock');
    if (m > .84) t = ['g', 'b', 'r', 'c', 'w', 'v'][Math.floor(smooth(x, y, 3, s) * 6) % 6];
    else if (m < .12) t = noise(x, y, s + 1) > .5 ? 'boulder' : 'boulder2';
    return t;
  }

  // Игровой момент для ключевого кадра: одна точка внимания вместо равномерного разреза.
  // Штрек идёт от стороны текста к забою; в забое жила кристаллов, робот уже врезался в неё, второй подходит по штреку.
  // Над штреком — постройка клана (стены MilitaryBlock, склад Box внутри), к ней ведёт лаз из штрека и ствол сверху.
  // Вся сцена — 13 клеток шириной: от fx − 6 до fx + 6.
  // o.fx, o.fy — точка внимания (робот в забое) в долях кадра; o.dir — куда смотрит забой: 1 — вправо, -1 — влево
  function moment(map, cols, rows, carve, bots, o, seed) {
    // Фон спокойнее разреза: случайные гнёзда кристаллов убраны — кристалл в кадре один, в забое
    const crystal = new Set(['g', 'b', 'r', 'c', 'w', 'v']);
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) if (crystal.has(map[y][x])) map[y][x] = 'rock';
    const d = o.dir ?? 1, fx = Math.round(cols * (o.fx ?? .66)), fy = Math.round(rows * (o.fy ?? .56));
    // Штрек: два ряда хода и дорога по полу, опоры в кровле через пять клеток
    const from = d > 0 ? -1 : cols, to = fx;
    for (let x = Math.min(from, to); x <= Math.max(from, to); x++) { carve(x, fy - 1); carve(x, fy); carve(x, fy + 1, 'road'); }
    for (let x = fx - d * 2; d > 0 ? x >= 0 : x < cols; x -= d * 5) carve(x, fy - 2, 'support');
    // Жила: овал за забоем, в середине белый кристалл, по краю — голубой (светлые — не сливаются с синей породой); передний край уже выбран роботом
    for (let y = -4; y <= 4; y++) for (let x = 0; x <= 5; x++) {
      const nx = (x - 1.6) / 2.7, ny = (y + .3) / 3, n = noise(fx + x, fy + y, seed + 5);
      const r = nx * nx + ny * ny + (n - .5) * .45;
      if (r < 1) carve(fx + d * (x + 1), fy + y, r < .5 ? 'w' : 'c');
    }
    carve(fx + d, fy - 1); carve(fx + d, fy); carve(fx + d, fy + 1, 'road');
    bots.push([fx + d, fy], [fx - d * 4, fy]);
    // Постройка клана над штреком, над вторым роботом: коробка 6×4 из стен, внутри склад, лаз вниз в штрек и ствол сверху
    const bx = fx - d * 6, by = fy - 7, bw = 6, bh = 4;
    for (let y = by; y < by + bh; y++) for (let x = 0; x < bw; x++) {
      const cx = bx + d * x, edge = y === by || y === by + bh - 1 || x === 0 || x === bw - 1;
      carve(cx, y, edge ? 'military' : 'empty');
    }
    carve(bx + d * 3, by + bh - 2, 'box'); carve(bx + d * 4, by + bh - 2, 'box'); carve(bx + d * 4, by + bh - 3, 'box');
    for (let y = by + bh - 1; y <= fy - 2; y++) carve(bx + d * 1, y);
    for (let y = 0; y <= by; y++) carve(bx + d * 4, y);
  }

  async function paint(canvas, o = {}) {
    const W = o.width ?? canvas.clientWidth, H = o.height ?? canvas.clientHeight, C = o.cell ?? 32, seed = o.seed ?? 7, depth = o.depth ?? 0;
    const cols = Math.ceil(W / C), rows = Math.ceil(H / C);
    const map = [];
    for (let y = 0; y < rows; y++) { const row = []; for (let x = 0; x < cols; x++) row.push(rockAt(x, y + depth, seed)); map.push(row); }
    const carve = (x, y, t = 'empty') => { if (map[y]?.[x] !== undefined) map[y][x] = t; };
    const bots = [];
    if (o.moment) moment(map, cols, rows, carve, bots, o, seed);
    else if (o.carve !== false) {
      // Ствол сверху вниз и два штрека от него: дорога по полу, опоры через шесть клеток — как ходы игрока в мире
      const sx = o.shaft === null ? null : Math.round(cols * (o.shaft ?? .62));
      const levels = [Math.round(rows * .38), Math.round(rows * .74)];
      if (sx !== null) for (let y = 0; y < rows; y++) { carve(sx, y); carve(sx + 1, y); }
      levels.forEach((ly, k) => {
        const from = sx === null ? 0 : (k ? sx - Math.round(cols * .55) : sx + 2), to = sx === null ? cols : (k ? sx : sx + 2 + Math.round(cols * .3));
        for (let x = Math.max(0, from); x < Math.min(cols, to); x++) { carve(x, ly); carve(x, ly + 1); carve(x, ly + 2, 'road'); }
        for (let x = Math.max(0, from) + 2; x < Math.min(cols, to); x += 6) carve(x, ly - 1, 'support');
        // Забой: робот у конца штрека, перед ним жила
        const ex = k ? Math.max(1, from + 1) : Math.min(cols - 2, to - 1);
        bots.push([ex, ly + 1]);
        for (let i = 0; i < 4; i++) carve(ex + (k ? -1 : 1), ly - 1 + i, ['c', 'g', 'b', 'v'][(i + k) % 4]);
      });
      if (sx !== null) bots.push([sx, Math.round(rows * .55)]);
    }
    const files = [...new Set(map.flat())].map(t => TYPES[t]);
    const imgs = Object.fromEntries(await Promise.all(files.map(async f => [f, await load(new URL(`cells/${f}.png`, IMG).href)])));
    const robot = await load(new URL('bee.png', IMG).href);

    const r = o.ratio ?? (devicePixelRatio || 1);
    canvas.width = Math.round(W * r); canvas.height = Math.round(H * r);
    if (!o.keepStyle) { canvas.style.width = W + 'px'; canvas.style.height = H + 'px'; }
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    const px = v => Math.round(v * r);
    const cell = (img, x, y) => ctx.drawImage(img, px(x * C), px(y * C), px((x + 1) * C) - px(x * C), px((y + 1) * C) - px(y * C));
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) { const i = imgs[TYPES[map[y][x]]]; if (i) cell(i, x, y); }
    ctx.imageSmoothingEnabled = false;
    if (robot) bots.slice(0, o.bots ?? 3).forEach(([x, y]) => cell(robot, x, y));
    return { cols, rows, bots };
  }
  window.KernScene = { paint };
})();
