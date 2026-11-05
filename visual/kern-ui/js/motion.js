// Движение «Квант» — помощники для данных, которые меняются ступенями (css/motion.css, токены --step, --count, --flash).
// Все три — то, что в Unity 6.7 делает код окна: transition-delay на элементе, schedule.Execute для досчёта,
// класс со вспышкой (USS animation или schedule) — см. kern-brand/book/pages/design-code.html#motion.
(() => {
  const ms = name => {
    const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return v.endsWith('ms') ? parseFloat(v) : (parseFloat(v) || 0) * 1000;
  };

  // Шкала из ячеек: ячейки не пересоздаются, меняется только состояние. Загораются по одной от края к новому значению,
  // гаснут — от нового значения обратно; каждая следующая — на шаг --step позже. cls — класс горящей ячейки
  function kernQuant(box, total, lit, cls = 'on') {
    if (!box) return;
    while (box.children.length < total) box.append(document.createElement('i'));
    while (box.children.length > total) box.lastElementChild.remove();
    const cells = [...box.children], was = cells.filter(c => c.classList.contains(cls)).length;
    const step = ms('--step'), up = lit > was, now = performance.now();
    // Один фронт: если прошлое изменение в ту же сторону ещё идёт, новые ячейки встают за ним, а не начинают второй фронт
    const run = box._kernRun;
    const going = run && run.up === up && now < run.end;
    const t0 = going ? run.t0 : now, from = going ? run.from : was;
    cells.forEach((c, i) => {
      const changes = up ? i >= was && i < lit : i >= lit && i < was;
      const order = up ? i - from : from - 1 - i;
      // Задержку получают только меняющиеся ячейки: у ещё не догоревших с прошлого изменения она сохраняется
      if (changes) c.style.setProperty('--qd', `${Math.max(0, order * step - (now - t0))}ms`);
      c.classList.toggle(cls, i < lit);
    });
    box._kernRun = { up, t0, from, end: t0 + Math.abs(lit - from) * step };
  }

  // Число досчитывается до нового значения за --count, ступенями по кадру; format — как показывать (группы разрядов, K…)
  const counting = new WeakMap();
  function kernCount(el, to, format = String) {
    if (!el) return;
    const from = Number(el.dataset.value ?? to), dur = ms('--count');
    el.dataset.value = to;
    cancelAnimationFrame(counting.get(el));
    if (!dur || from === to || document.documentElement.classList.contains('still')) { el.innerHTML = format(to); return; }
    const t0 = performance.now();
    const tick = now => {
      const k = Math.min(1, (now - t0) / dur);
      el.innerHTML = format(Math.round(from + (to - from) * k));
      if (k < 1) counting.set(el, requestAnimationFrame(tick));
    };
    counting.set(el, requestAnimationFrame(tick));
  }

  // Тревога: две вспышки и ровный цвет. Повтор — только при новом событии
  function kernFlash(el, outline = false) {
    if (!el) return;
    const cls = outline ? 'is-flash-outline' : 'is-flash';
    el.classList.remove(cls); void el.offsetWidth; el.classList.add(cls);
    el.addEventListener('animationend', () => el.classList.remove(cls), { once: true });
  }

  // ?still=1 — снимок: досчёт сразу до значения (переходы стенд гасит сам, screens.js)
  if (new URLSearchParams(location.search).get('still')) document.documentElement.classList.add('still');
  // ?motion=off — проверить вид без движения
  if (new URLSearchParams(location.search).get('motion') === 'off') document.documentElement.classList.add('motion-off');
  Object.assign(window, { kernQuant, kernCount, kernFlash });
})();
