// Режимы просмотра карты и миникарты — как MapViewer легаси-клиента (OpenMines, выпадающий список modeDropdown):
//   0 «Обычный» — цвет клетки на карте;
//   (1 «Мелкий» легаси — охват вдвое больше — у нас не нужен: у карты есть масштаб; MAPMODE1 от сервера пропускается)
//   2 «Только живки» — живые клетки ярко, остальное приглушено, чанки 32×32 шахматкой;
//   3 «Грани и дороги» — пустота чёрная, порода серая, сыпучая порода темнее, дороги и опоры светлые;
//   4 «Пользовательский» — цвета клеток задаёт сервер.
// Команды сервера (ClientConfig легаси): CMAP — сбросить таблицу сервера и открыть карту в режиме 4; CMAPRESET — сбросить;
//   CMAPCHOOSE и MAPMODE0, 2, 3, 4 — выбрать режим; cmap<клетка>=RGB — цвет клетки по hex-цифре на канал, «!N» — мигание
//   с частотой 2.5·1.3^N. Сброшенная таблица — серый по яркости клетки, пустота и дороги почти чёрные.
// Давность разведки — наложение поверх любого режима: чем дольше сервер не присылал чанк, тем бледнее он
//   (время хранит контейнер карты, js/world.js); неполученное не рисуется.
// Миникарта показывает режим карты мира; его название — слева в строке над миникартой, если режим не «Обычный»
(() => {
  const $ = id => document.getElementById(id);
  const W = window.kernWorld, T = W.TYPES;
  // Номер режима — как в легаси (номер команды MAPMODE); 1 нет
  const MODES = { 0: 'Обычный', 2: 'Только живки', 3: 'Грани и дороги', 4: 'Пользовательский' };
  const HINTS = {
    0: 'Цвет породы и кристаллов.',
    2: 'Живые клетки ярко, остальное приглушено. Квадраты — участки 32×32.',
    3: 'Пустота чёрная, порода серая, песок темнее, дороги и опоры светлые.',
    4: 'Цвета клеток задаёт сервер.',
  };
  const rgb = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
  const css = c => `rgb(${c[0] | 0} ${c[1] | 0} ${c[2] | 0})`;
  const mix = (a, b, k) => [0, 1, 2].map(i => a[i] + (b[i] - a[i]) * k);
  const BG = rgb(getComputedStyle(document.documentElement).getPropertyValue('--map-background').trim() || '#05070a');
  // Живые клетки в режиме 2 — цвета таблицы живок легаси (aliveColorTable)
  const ALIVE = { c: [101, 255, 255], r: [255, 51, 51], v: [255, 101, 255], b: [161, 162, 255] };
  const state = { mode: 0, age: true, custom: {}, blink: {} };
  const still = !!new URLSearchParams(location.search).get('still');

  const base = (t, x, y, time, mode) => {
    const c = T[t];
    switch (mode) {
      case 2: {
        const v = c.alive ? ALIVE[c.alive] : mix(rgb(c.mm), BG, .82);
        return ((x >> 5) + (y >> 5)) % 2 === 0 ? [v[0], v[1], Math.min(255, v[2] + 26)] : v;
      }
      case 3:
        return c.pass && !c.road ? [0, 0, 0] : c.road ? [196, 194, 186] : c.sand ? [77, 89, 89] : [102, 115, 115];
      case 4: {
        const own = state.custom[t];
        let v = own || (c.pass || c.road ? [10, 11, 11] : (() => { const m = rgb(c.mm), l = (m[0] * .3 + m[1] * .59 + m[2] * .11) / 255; return [1, 1, 1].map(() => 70 + l * 150); })());
        const rate = state.blink[t];
        if (own && rate && !still) v = v.map(k => k * (.5 + .5 * Math.sin(time * rate)));
        return v;
      }
      default:
        return rgb(c.mm);
    }
  };
  // Давность: до 5 минут — как есть, до часа — четверть к фону, до суток — почти половина, дольше — 60 %
  const fade = at => { const ago = Date.now() - at; return ago < 3e5 ? 0 : ago < 36e5 ? .25 : ago < 864e5 ? .45 : .6; };

  // Цвет клетки x, y типа t в текущем режиме; null — чанк не приходил, клетку не рисовать
  const color = (t, x, y) => {
    const at = W.seenAt(x, y);
    if (!at) return null;
    const v = base(t, x, y, performance.now() / 1000, state.mode);
    return css(state.age ? mix(v, BG, fade(at)) : v);
  };

  const apply = () => {
    $('mapMode').querySelectorAll('input').forEach(r => { r.checked = +r.value === state.mode; });
    $('mapModeHint').textContent = HINTS[state.mode];
    const tag = $('mmMode');
    tag.textContent = MODES[state.mode];
    tag.hidden = state.mode === 0;
    W.drawMinimap();
    window.kernMapRedraw?.();
    loop();
  };
  const setMode = m => { if (MODES[m]) state.mode = +m; apply(); };

  // Мигание таблицы сервера: перерисовка, пока есть мигающие клетки и выбран режим 4
  let raf = 0, last = 0;
  function loop() {
    cancelAnimationFrame(raf);
    if (still || state.mode !== 4 || !Object.keys(state.blink).length) return;
    raf = requestAnimationFrame(function tick(ts) {
      if (ts - last > 90) { last = ts; W.drawMinimap(); window.kernMapRedraw?.(); }
      raf = requestAnimationFrame(tick);
    });
  }

  // Команды сервера — строками, как в конфиге легаси
  const resetCustom = () => { state.custom = {}; state.blink = {}; };
  function command(line) {
    line.split(/\s+/).filter(Boolean).forEach(cmd => {
      const eq = cmd.match(/^cmap(\w+)=([0-9a-f]{3})(?:!([0-9a-f]))?$/i);
      if (eq) {
        const t = eq[1]; if (!T[t]) return;
        state.custom[t] = [...eq[2]].map(h => parseInt(h, 16) / 16 * 255);
        if (eq[3]) state.blink[t] = 2.5 * Math.pow(1.3, parseInt(eq[3], 16)); else delete state.blink[t];
        return;
      }
      const c = cmd.toUpperCase();
      if (c === 'CMAP') { resetCustom(); state.mode = 4; if ($('mapDialog').hidden) window.openMap(); }
      else if (c === 'CMR' || c === 'CMAPRESET') resetCustom();
      else if (c === 'CMC' || c === 'CMAPCHOOSE') state.mode = 4;
      else if (/^MAPMODE[0234]$/.test(c)) state.mode = +c.slice(7);
    });
    apply();
  }

  window.kernMapModes = {
    color, background: () => css(BG),
    command, setMode,
  };

  // Радио-список: метка — как у галок «Показывать» (квадрат, .check)
  $('mapMode').innerHTML = Object.entries(MODES).map(([i, n]) => `<label class="check"><input type="radio" name="mapMode" value="${i}">${n}</label>`).join('');
  $('mapMode').onchange = e => setMode(+e.target.value);
  $('mapAge').onchange = e => { state.age = e.target.checked; W.drawMinimap(); window.kernMapRedraw?.(); };

  // Стенд: команды, которые мог бы прислать сервер
  const SAMPLES = {
    cmap: 'CMAP cmapb=22f cmapc=0ff!8 cmapaliveC=0aa!4 cmapg=0f0',
    reset: 'CMAPRESET',
    m0: 'MAPMODE0', m2: 'MAPMODE2', m3: 'MAPMODE3', m4: 'MAPMODE4',
  };
  $('standMapCmd').onchange = e => { if (SAMPLES[e.target.value]) command(SAMPLES[e.target.value]); e.target.value = ''; };
  window.kernScene.on(2, q => {
    if (q.get('mapcmd') && SAMPLES[q.get('mapcmd')]) command(SAMPLES[q.get('mapcmd')]);
    else if (q.get('mapmode')) setMode(+q.get('mapmode'));
  });
  window.kernScene.onReset(() => { resetCustom(); state.age = true; $('mapAge').checked = true; setMode(0); });
  apply();
})();
