// Сокращение чисел — одно правило на весь интерфейс (HUD, окна сервера): число пишется целиком, пока помещается
// в своё место (max — длина с пробелами групп: кошелёк, ряд кристаллов и таблицы — 5, «9 999»),
// иначе K, M, B, T, Q и не длиннее четырёх знаков (12K, 340K, 4.7M, 85B); лишнее отбрасывается, а не округляется.
// Полное значение — подсказкой по наведению или фокусу. Подсказка — один слой на кадре, вне панелей:
// контур панели (clip-path со срезом) обрезал бы её
(() => {
  const group = n => String(Math.trunc(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  const UNITS = 'KMBTQ';

  // Текст сокращения; null — число короткое, сокращать нечего
  window.kernShort = (n, max = 5) => {
    if (group(n).length <= max) return null;
    let i = -1, v = n;
    while (v >= 1e3 && i < UNITS.length - 1) { v /= 1e3; i++; }
    return (v < 10 ? String(Math.floor(v * 10) / 10) : String(Math.floor(v))) + UNITS[i];
  };

  // Разметка числа: короткое — целиком с группами, длинное — сокращение с полным значением в подсказке
  window.kernCut = (n, prefix = '', max = 5) => {
    const s = window.kernShort(n, max);
    return s === null ? prefix + group(n) : `<span class="pk-cut" tabindex="0" data-full="${prefix}${group(n)}">${prefix}${s}</span>`;
  };

  // Подсказка: над числом по центру, в пределах кадра; кадр стенда масштабируется — координаты в его пикселях
  let tip = null;
  const show = el => {
    const frame = el.closest('.frame') || document.body;
    if (!tip) { tip = document.createElement('div'); tip.className = 'pk-tip'; tip.setAttribute('role', 'tooltip'); }
    if (tip.parentNode !== frame) frame.appendChild(tip);
    tip.textContent = el.dataset.full;
    tip.hidden = false;
    const f = frame.getBoundingClientRect(), k = f.width / (frame.offsetWidth || f.width), r = el.getBoundingClientRect();
    const w = tip.offsetWidth, h = tip.offsetHeight;
    const x = Math.min(Math.max((r.left + r.width / 2 - f.left) / k - w / 2, 4), frame.offsetWidth - w - 4);
    const above = (r.top - f.top) / k - h - 6;
    tip.style.left = `${x}px`;
    tip.style.top = `${above >= 4 ? above : (r.bottom - f.top) / k + 6}px`;
  };
  const hide = () => { if (tip) tip.hidden = true; };
  document.addEventListener('mouseover', e => { const el = e.target.closest?.('.pk-cut'); if (el) show(el); });
  document.addEventListener('mouseout', e => { if (e.target.closest?.('.pk-cut') && !e.relatedTarget?.closest?.('.pk-cut')) hide(); });
  document.addEventListener('focusin', e => { const el = e.target.closest?.('.pk-cut'); if (el) show(el); });
  document.addEventListener('focusout', e => { if (e.target.closest?.('.pk-cut')) hide(); });
})();
