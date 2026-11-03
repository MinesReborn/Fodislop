// Сцена стенда — параметры адреса (?screen=game&state=busy&settings=audio…). Каждый модуль регистрирует свой обработчик,
// а не читает адрес сам: при загрузке шина применяет параметры адреса, проверки (tools/*-check.html) — любую сцену на месте:
// kernScene.reset() возвращает исходное состояние, kernScene.apply('…') применяет параметры тем же кодом, что и при загрузке.
// Порядок обработчиков — как раньше по времени: 0 — при разборе страницы, 1 — по готовности мира, 2 — HUD, 3 — события и язык
(() => {
  const handlers = [], resets = [];
  const parsed = new Promise(r => document.readyState === 'loading' ? addEventListener('DOMContentLoaded', r) : r());
  window.kernScene = {
    on: (order, fn) => handlers.push({ order, fn }),
    onReset: fn => resets.push(fn),
    apply(qs, from = 0, to = 9) {
      const q = new URLSearchParams(qs);
      [...handlers].sort((a, b) => a.order - b.order).filter(h => h.order >= from && h.order <= to).forEach(h => h.fn(q));
      dispatchEvent(new Event('kern-scene'));               // раскладка, зависящая от текста (ширина левой колонки), — сразу, без ожидания кадра
    },
    reset() {
      resets.forEach(fn => fn());
      document.querySelectorAll('.dialog-layer').forEach(l => { l.hidden = true; });
    },
  };
  // При загрузке — один проход всех обработчиков, когда готовы разбор страницы, шрифты и картинки клеток. Раньше экран
  // (порядок 0) показывался сразу, а размер кадра, HUD и язык (порядки 1–3) применялись после загрузки картинок: кадр
  // менял размер и наполнялся на глазах, а ширина левой колонки мерилась запасным шрифтом. Пока проход не прошёл,
  // кадр скрыт (html[data-kern-booting], ставится в head).
  const settle = p => Promise.race([p, new Promise(r => setTimeout(r, 4000))]);   // сломанный шрифт не должен держать стенд скрытым
  parsed
    .then(() => Promise.all([settle(document.fonts?.ready), settle(window.kernWorld.ready)]))
    .then(() => window.kernScene.apply(location.search))
    .finally(() => document.documentElement.removeAttribute('data-kern-booting'));
})();
