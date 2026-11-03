// Карта мира: вся известная клиенту карта, перетаскивание и масштаб, метки и места.
// Открывается миникартой; на ПК — ещё клавишей M. Колесо и кнопки ± — масштаб, «К себе» — центр на роботе.
(() => {
  const $ = id => document.getElementById(id);
  const token = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  const dlg = $('mapDialog'), view = $('mapView'), canvas = $('mapCanvas'), pinsEl = $('mapPins');
  const ZOOMS = [2, 3, 4, 6, 8, 12, 16];
  let zi = 4, cx = 0, cy = 0;                         // масштаб — пикселей на клетку; центр вида — в клетках
  // Координаты игры = клетка карты + сдвиг (как на миникарте: робот на 1184:902)
  const gx = x => 1184 + x - window.kernWorld.PX, gy = y => 902 + y - window.kernWorld.PY;
  const PLACES = () => {
    const w = window.kernWorld, P = [w.PX, w.PY];
    return [
      { kind: 'me', name: 'Я', x: P[0], y: P[1] },
      { kind: 'clan', name: 'ШАХТЁР-102', x: P[0] - 22, y: P[1] + 1 },
      { kind: 'clan', name: 'DVM_LEADER', x: P[0] + 16, y: P[1] - 14 },
      { kind: 'clan', name: 'ROCKBOT', x: P[0] - 38, y: P[1] + 2 },
      { kind: 'build', name: 'Рынок', x: P[0] - 30, y: P[1] + 1 },
      { kind: 'build', name: 'Пушка', x: P[0] - 12, y: P[1] - 1 },
      { kind: 'build', name: 'UP', x: P[0] + 6, y: P[1] + 3 },
      { kind: 'spawn', name: 'Респаун', x: P[0] - 40, y: P[1] },
      ...(mark ? [{ kind: 'mark', name: 'Моя метка', x: mark.x, y: mark.y }] : []),
    ];
  };
  const shown = { me: true, clan: true, build: true, spawn: true, mark: true };
  // Своя метка: ставится на карте, ведёт стрелкой у края экрана и точкой на миникарте. Живёт только в клиенте
  let mark = null, marking = false;
  function setMark(m) {
    mark = m;
    window.kernWorld.marks = window.kernWorld.marks.filter(x => x.kind !== 'mark');
    if (m) window.kernWorld.marks.push({ kind: 'mark', x: m.x, y: m.y, color: token('--mark-own'), name: 'Метка' });
    window.kernWorld.draw(); renderPlaces(); if (!dlg.hidden) draw();
  }
  window.kernMark = setMark;

  function draw() {
    const w = window.kernWorld, map = w.map, H = map.length, W = map[0].length, z = ZOOMS[zi];
    const vw = view.clientWidth, vh = view.clientHeight, dpr = devicePixelRatio || 1;
    canvas.width = Math.round(vw * dpr); canvas.height = Math.round(vh * dpr);
    canvas.style.width = vw + 'px'; canvas.style.height = vh + 'px';
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = token('--map-background'); ctx.fillRect(0, 0, vw, vh);
    const x0 = Math.floor(cx - vw / 2 / z), y0 = Math.floor(cy - vh / 2 / z);
    const ox = vw / 2 - (cx - x0) * z, oy = vh / 2 - (cy - y0) * z;
    for (let y = 0; y <= vh / z + 1; y++) for (let x = 0; x <= vw / z + 1; x++) {
      const t = map[y0 + y]?.[x0 + x]; if (!t) continue;
      ctx.fillStyle = w.TYPES[t].mm; ctx.fillRect(Math.floor(ox + x * z), Math.floor(oy + y * z), Math.ceil(z), Math.ceil(z));
    }
    // Сетка по 10 клеток — с крупного масштаба
    if (z >= 6) {
      ctx.strokeStyle = token('--map-grid'); ctx.lineWidth = 1;
      for (let x = x0 - x0 % 10; x < x0 + vw / z + 1; x += 10) { const px = Math.round(ox + (x - x0) * z) + .5; ctx.beginPath(); ctx.moveTo(px, 0); ctx.lineTo(px, vh); ctx.stroke(); }
      for (let y = y0 - y0 % 10; y < y0 + vh / z + 1; y += 10) { const py = Math.round(oy + (y - y0) * z) + .5; ctx.beginPath(); ctx.moveTo(0, py); ctx.lineTo(vw, py); ctx.stroke(); }
    }
    // Метки — разметкой поверх холста: подписи читаются при любом масштабе
    pinsEl.innerHTML = PLACES().filter(p => shown[p.kind]).map(p => {
      const px = ox + (p.x - x0 + .5) * z, py = oy + (p.y - y0 + .5) * z;
      if (px < -40 || py < -40 || px > vw + 40 || py > vh + 40) return '';
      return `<div class="pin pin--${p.kind}" style="left:${px}px;top:${py}px"><i class="pin-dot pin-dot--${p.kind}"></i>${p.kind === 'me' ? '' : `<span>${p.name}</span>`}</div>`;
    }).join('');
    $('mapScale').textContent = `×${z}`;
    $('mapZoomIn').disabled = zi === ZOOMS.length - 1; $('mapZoomOut').disabled = zi === 0;
  }
  const home = () => { cx = window.kernWorld.PX + .5; cy = window.kernWorld.PY + .5; };
  function zoom(step, at) {
    const nz = Math.max(0, Math.min(ZOOMS.length - 1, zi + step)); if (nz === zi) return;
    // Точка под курсором остаётся на месте
    if (at) { const r = view.getBoundingClientRect(), k = view.clientWidth / r.width;
      const dx = ((at.clientX - r.left) * k - view.clientWidth / 2), dy = ((at.clientY - r.top) * k - view.clientHeight / 2);
      cx += dx / ZOOMS[zi] - dx / ZOOMS[nz]; cy += dy / ZOOMS[zi] - dy / ZOOMS[nz]; }
    zi = nz; draw();
  }
  // Перетаскивание
  view.onpointerdown = e => {
    if (e.target.closest('button')) return;
    view.setPointerCapture(e.pointerId); view.classList.add('is-drag');
    const r = view.getBoundingClientRect(), k = view.clientWidth / r.width;
    let lx = e.clientX, ly = e.clientY;
    const sx = e.clientX, sy = e.clientY;
    view.onpointerup = view.onpointercancel = ev => {
      view.onpointermove = null; view.classList.remove('is-drag');
      if (marking && ev.type === 'pointerup' && Math.hypot(ev.clientX - sx, ev.clientY - sy) < 6) { setMark(cellAt(ev)); setMarking(false); }
    };
    view.onpointermove = ev => { cx -= (ev.clientX - lx) * k / ZOOMS[zi]; cy -= (ev.clientY - ly) * k / ZOOMS[zi]; lx = ev.clientX; ly = ev.clientY; draw(); };
  };
  const cellAt = e => { const r = view.getBoundingClientRect(), k = view.clientWidth / r.width, z = ZOOMS[zi];
    return { x: Math.floor(cx + ((e.clientX - r.left) * k - view.clientWidth / 2) / z), y: Math.floor(cy + ((e.clientY - r.top) * k - view.clientHeight / 2) / z) }; };
  const setMarking = on => { marking = on; $('mapMark').setAttribute('aria-pressed', String(on)); view.classList.toggle('is-marking', on); };
  $('mapMark').onclick = () => setMarking(!marking);
  view.addEventListener('pointermove', e => {
    const r = view.getBoundingClientRect(), k = view.clientWidth / r.width, z = ZOOMS[zi];
    const x = Math.floor(cx + ((e.clientX - r.left) * k - view.clientWidth / 2) / z), y = Math.floor(cy + ((e.clientY - r.top) * k - view.clientHeight / 2) / z);
    $('mapCoords').textContent = `${gx(x)}:${gy(y)}`;
  });
  view.addEventListener('wheel', e => { e.preventDefault(); zoom(e.deltaY < 0 ? 1 : -1, e); }, { passive: false });
  $('mapZoomIn').onclick = () => zoom(1); $('mapZoomOut').onclick = () => zoom(-1);
  $('mapHome').onclick = () => { home(); draw(); };
  $('mapFilters').onchange = e => { shown[e.target.dataset.kind] = e.target.checked; draw(); };
  function renderPlaces() {
    $('mapPlaces').innerHTML = PLACES().filter(p => p.kind !== 'me').map((p, i) =>
      `<li><button type="button" data-i="${i}"><i class="pin-dot pin-dot--${p.kind}"></i><b>${p.name}</b><small>${gx(p.x)}:${gy(p.y)}</small></button>${p.kind === 'mark' ? '<button type="button" class="icon-btn" data-unmark title="Убрать метку"><svg><use href="#i-close"/></svg></button>' : ''}</li>`).join('')
      + (mark ? '' : '<li class="map-hint">Чтобы поставить метку, нажмите <svg><use href="#i-flag"/></svg> и выберите место на карте</li>');
  }
  $('mapPlaces').onclick = e => { if (e.target.closest('[data-unmark]')) { setMark(null); return; } const b = e.target.closest('button'); if (!b) return; const p = PLACES().filter(q => q.kind !== 'me')[+b.dataset.i]; cx = p.x + .5; cy = p.y + .5; if (zi < 4) zi = 4; draw(); };

  const open = () => { dlg.hidden = false; home(); renderPlaces(); $('mapCoords').textContent = `${gx(window.kernWorld.PX)}:${gy(window.kernWorld.PY)}`; requestAnimationFrame(draw); };
  const close = () => { dlg.hidden = true; };
  window.openMap = open;
  document.querySelector('.minimap').addEventListener('click', open);
  dlg.querySelector('[data-close]').onclick = close;
  addEventListener('resize', () => { if (!dlg.hidden) draw(); });
  document.addEventListener('keydown', e => {
    if (e.target.matches('input, textarea')) return;
    if (e.key === 'Escape' && !dlg.hidden) close();
    else if (dlg.hidden && document.querySelector('.dialog-layer:not([hidden])')) return;
    else if ((e.key === 'm' || e.key === 'M' || e.key === 'ь' || e.key === 'Ь') && document.getElementById('frame').dataset.screen === 'game') dlg.hidden ? open() : close();
  });
  // ?map=1 — снимок: без плавного появления (в безголовом браузере кадры анимации не идут)
  window.kernScene.onReset(() => { close(); setMark(null); });
  window.kernScene.on(1, q => {
    if (q.get('mark')) setMark({ x: window.kernWorld.PX + 70, y: window.kernWorld.PY + 25 });
    if (q.get('map')) { dlg.style.transition = 'none'; open(); draw(); }
  });
})();
