// Калибровка дисплея по образцу AAA (HGIG): шаг — узор во весь экран и один ползунок.
// Диапазоны и шаг — DisplaySettings клиента: пик 400–2000 нит, белая точка 100–400 нит, шаг 50.
// Браузер выводит SDR, поэтому узор имитирует то, что увидит глаз на дисплее с пиком SIM_PEAK.
(() => {
  const $ = id => document.getElementById(id);
  const calib = $('calib'), range = $('calibRange');
  const SIM_PEAK = 1500;                                          // «наш дисплей» для имитации
  const STEPS = {
    peak: { title: 'Пиковая яркость', text: 'Двигайте ползунок, пока знак не станет едва заметен.',
      min: 400, max: 2000, step: 50, def: 1300, unit: ' нит' },
    black: { title: 'Уровень чёрного', text: 'Добейтесь, чтобы знаки выглядели как подписано под ними.',
      min: 0, max: 100, step: 1, def: 50, unit: '' },
    bright: { title: 'Яркость', text: 'Добейтесь, чтобы знаки выглядели как подписано под ними.',
      min: 0, max: 100, step: 1, def: 50, unit: '', pattern: 'black' },
    paper: { title: 'Белая точка', text: 'Белое поле должно выглядеть как лист бумаги в этой комнате.',
      min: 100, max: 400, step: 50, def: 350, unit: ' нит' },
  };
  const values = Object.fromEntries(Object.entries(STEPS).map(([k, v]) => [k, v.def]));
  let order = [], at = 0;

  function paint() {
    const key = order[at], st = STEPS[key], v = values[key];
    $('calibValue').textContent = v + st.unit;
    const pattern = st.pattern || key;
    calib.querySelectorAll('.calib-pattern').forEach(p => p.hidden = p.dataset.p !== pattern);
    const logo = calib.querySelector(`.calib-pattern[data-p="${pattern}"] .calib-logo`);
    if (pattern === 'peak') {
      // Знак светлее фона на запас до пика дисплея: выше пика дисплей срезает — знак сливается
      const gap = Math.max(0, (SIM_PEAK - v) / SIM_PEAK);
      logo.style.background = `rgb(${Math.round(255 - 70 * gap)} ${Math.round(255 - 70 * gap)} ${Math.round(255 - 70 * gap)})`;
    } else if (pattern === 'black') {
      // Три знака чуть светлее чёрного с разным запасом; ползунок сдвигает нижний край для всех сразу
      const lift = (v - 50) * 0.6;
      calib.querySelectorAll('[data-p="black"] .calib-logo').forEach((l, i) => {
        const g = Math.max(0, Math.round([-4, 8, 22][i] + lift));
        l.style.background = `rgb(${g} ${g} ${g})`;
      });
    } else {
      $('calibScene').style.filter = `brightness(${(0.55 + v / 400 * 0.6).toFixed(3)})`;
    }
    const pct = (v - st.min) / (st.max - st.min) * 100;
    range.style.setProperty('--fill', pct + '%');
  }
  function show() {
    const key = order[at], st = STEPS[key];
    $('calibTitle').textContent = st.title;
    $('calibText').textContent = st.text;
    Object.assign(range, { min: st.min, max: st.max, step: st.step });
    range.value = values[key];
    $('calibSteps').innerHTML = order.map((k, i) => `<li class="${i < at ? 'is-done' : i === at ? 'is-on' : ''}">${i + 1}</li>`).join('');
    $('calibSteps').hidden = order.length < 2;
    $('calibBack').hidden = at === 0;
    $('calibNext').textContent = at === order.length - 1 ? 'Готово' : 'Далее';
    paint();
  }
  const nudge = d => { const st = STEPS[order[at]]; values[order[at]] = Math.min(st.max, Math.max(st.min, values[order[at]] + d * st.step)); range.value = values[order[at]]; paint(); };
  range.oninput = () => { values[order[at]] = Number(range.value); paint(); };
  $('calibMinus').onclick = () => nudge(-1);
  $('calibPlus').onclick = () => nudge(1);
  $('calibReset').onclick = () => { values[order[at]] = STEPS[order[at]].def; range.value = values[order[at]]; paint(); };
  $('calibBack').onclick = () => { if (at > 0) { at--; show(); } };
  const close = () => { calib.hidden = true; };
  $('calibNext').onclick = () => { if (at < order.length - 1) { at++; show(); } else close(); };
  $('calibCancel').onclick = close;
  document.addEventListener('keydown', e => {
    if (calib.hidden) return;
    if (e.key === 'ArrowLeft') nudge(-1);
    else if (e.key === 'ArrowRight') nudge(1);
    else if (e.key === 'Escape') { e.stopPropagation(); close(); return; }
    else return;
    e.preventDefault();
  }, true);

  window.openCalibration = (hdr = $('setHdr').checked, step = 0) => {
    order = hdr ? ['peak', 'black', 'paper'] : ['bright'];
    at = Math.min(step, order.length - 1);
    calib.hidden = false; show();
  };
  $('calibOpen').onclick = () => window.openCalibration();
  $('onbCalib').onclick = () => window.openCalibration($('onbHdr').checked);

  // ?calib=hdr|sdr&calibstep=N — снимки
  window.kernScene.on(0, q => { if (q.get('calib')) window.openCalibration(q.get('calib') !== 'sdr', Number(q.get('calibstep')) || 0); });
  window.kernScene.onReset(() => { $('calib').hidden = true; });
})();
