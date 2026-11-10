// Выпадающий список — свой, вместо системного меню ОС: плашка «колодца» с волосяной рамкой, прямые углы,
// выбранный пункт — квадратная метка и кость, пункт под курсором — светлее поле. Ширина — по полю, раскрытие вниз,
// при нехватке места — вверх. Мышь, стрелки, Home/End, Enter/Пробел, Esc; клик мимо и прокрутка закрывают.
// Работает для всех select внутри кадра игры; панель стенда остаётся системной
(() => {
  let menu = null, owner = null, active = -1;

  const items = () => [...menu.querySelectorAll('.kmenu-item')];
  const mark = i => {
    active = i;
    items().forEach((el, k) => el.classList.toggle('is-active', k === i));
    items()[i]?.scrollIntoView({ block: 'nearest' });
  };

  function close(focus = true) {
    if (!menu) return;
    menu.remove(); menu = null;
    owner?.classList.remove('is-open');
    if (focus) owner?.focus({ preventScroll: true, focusVisible: false });
    owner = null;
  }

  function choose(i) {
    const sel = owner;
    if (sel && i >= 0 && sel.selectedIndex !== i) {
      sel.selectedIndex = i;
      sel.dispatchEvent(new Event('input', { bubbles: true }));
      sel.dispatchEvent(new Event('change', { bubbles: true }));
    }
    close();
  }

  function open(sel) {
    close(false);
    const frame = sel.closest('.frame');
    owner = sel;
    sel.classList.add('is-open');
    menu = document.createElement('div');
    menu.className = 'kmenu';
    menu.style.fontSize = getComputedStyle(sel).fontSize;   // кегль — как у поля: список в окне сервера мельче, чем в настройках
    menu.setAttribute('role', 'listbox');
    [...sel.options].forEach((o, i) => {
      const el = document.createElement('div');
      el.className = 'kmenu-item' + (i === sel.selectedIndex ? ' is-on' : '');
      el.setAttribute('role', 'option');
      el.setAttribute('aria-selected', String(i === sel.selectedIndex));
      el.textContent = o.textContent;
      if (o.disabled) el.classList.add('is-off');
      el.addEventListener('mousemove', () => { if (active !== i) mark(i); });
      el.addEventListener('mousedown', e => e.preventDefault());
      el.addEventListener('click', () => { if (!o.disabled) choose(i); });
      menu.append(el);
    });
    frame.append(menu);
    // Кадр стенда масштабируется: координаты — в его пикселях
    const f = frame.getBoundingClientRect(), k = f.width / frame.offsetWidth || 1, r = sel.getBoundingClientRect();
    const x = (r.left - f.left) / k, top = (r.top - f.top) / k, bottom = (r.bottom - f.top) / k, w = r.width / k;
    menu.style.minWidth = `${w}px`;
    menu.style.left = `${Math.min(x, frame.offsetWidth - Math.max(w, menu.offsetWidth) - 4)}px`;
    const below = frame.offsetHeight - bottom - 8, above = top - 8, h = menu.offsetHeight;
    const down = h <= below || below >= above;
    menu.style.maxHeight = `${Math.max(120, down ? below : above)}px`;
    menu.style.top = down ? `${bottom - 1}px` : `${top - Math.min(h, above) + 1}px`;
    menu.classList.toggle('is-up', !down);
    mark(Math.max(0, sel.selectedIndex));
  }

  const ours = el => el instanceof HTMLSelectElement && el.closest('.frame') && !el.multiple && !el.disabled;

  // Системное меню не открывается: нажатие на поле открывает своё
  document.addEventListener('mousedown', e => {
    if (menu && !menu.contains(e.target) && e.target !== owner) close(false);
    const sel = e.target.closest?.('select');
    if (!sel || !ours(sel) || e.button !== 0) return;
    e.preventDefault();
    sel.focus({ preventScroll: true, focusVisible: false });   // фокус мышью — без рамки клавиатурного фокуса
    if (owner === sel) close(); else open(sel);
  }, true);

  document.addEventListener('keydown', e => {
    if (menu) {
      const n = items().length;
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
      else if (e.key === 'ArrowDown') { e.preventDefault(); mark(Math.min(n - 1, active + 1)); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); mark(Math.max(0, active - 1)); }
      else if (e.key === 'Home') { e.preventDefault(); mark(0); }
      else if (e.key === 'End') { e.preventDefault(); mark(n - 1); }
      else if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); choose(active); }
      else if (e.key === 'Tab') close(false);
      return;
    }
    // Клавиши, которыми ОС раскрывает список, раскрывают свой
    const sel = document.activeElement;
    if (ours(sel) && (e.key === 'Enter' || e.key === ' ' || (e.altKey && (e.key === 'ArrowDown' || e.key === 'ArrowUp')))) { e.preventDefault(); open(sel); }
  }, true);

  document.addEventListener('scroll', e => { if (menu && !menu.contains(e.target)) close(false); }, true);
  window.addEventListener('resize', () => close(false));
  window.kernMenuClose = close;
})();
