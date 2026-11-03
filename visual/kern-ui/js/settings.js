// Окно настроек: одно на меню и игру. Вкладки слева, строки «название — значение» справа; всё применяется сразу
(() => {
  const $ = id => document.getElementById(id);
  const dlg = $('settingsDialog');
  const tabs = [...dlg.querySelectorAll('.settings-tabs [data-tab]')], pages = [...dlg.querySelectorAll('.set-page')];
  function setTab(name) {
    tabs.forEach(t => { t.classList.toggle('is-on', t.dataset.tab === name); t.setAttribute('aria-selected', t.dataset.tab === name); });
    pages.forEach(p => p.classList.toggle('is-on', p.dataset.tab === name));
    dlg.querySelector('.settings-body').scrollTop = 0;
  }
  tabs.forEach(t => t.onclick = () => setTab(t.dataset.tab));
  const open = (tab = 'graphics') => { setTab(tab); dlg.hidden = false; };
  const close = () => { dlg.hidden = true; stopListen(); };
  window.openSettings = open;
  dlg.querySelector('[data-close]').onclick = close;
  dlg.addEventListener('click', e => { if (e.target === dlg) close(); });

  // Сегменты: выбран один
  dlg.querySelectorAll('.seg').forEach(g => g.onclick = e => {
    const b = e.target.closest('button'); if (!b) return;
    g.querySelectorAll('button').forEach(x => x.setAttribute('aria-checked', x === b));
  });

  // Ползунки: число рядом
  dlg.querySelectorAll('.range input').forEach(r => r.oninput = () => { r.nextElementSibling.textContent = r.value + '%'; });

  // Переназначение клавиш: нажать на клавишу — «Нажмите клавишу…», Esc — отмена
  let listening = null;
  const keyName = e => e.code === 'Space' ? 'Space' : e.key.length === 1 ? e.key.toUpperCase() : e.key;
  function stopListen() { if (listening) { listening.textContent = listening.dataset.key; listening.classList.remove('is-listening'); listening = null; } }
  dlg.querySelectorAll('button.keycap:not(.padcap)').forEach(k => k.onclick = () => {
    stopListen(); listening = k; k.textContent = 'Нажмите клавишу…'; k.classList.add('is-listening');
  });
  document.addEventListener('keydown', e => {
    if (dlg.hidden) return;
    if (listening) {
      e.preventDefault(); e.stopPropagation();
      if (e.key !== 'Escape') listening.dataset.key = keyName(e);
      stopListen();
    } else if (e.key === 'Escape') close();
  }, true);
  const DEFAULTS = ['Space', 'E', 'G', 'V', 'Y', 'H', 'F', 'J'];
  $('keysReset').onclick = () => { stopListen(); dlg.querySelectorAll('button.keycap:not(.padcap)').forEach((k, i) => { k.dataset.key = k.textContent = DEFAULTS[i]; }); };

  // «Управление»: клавиатура или геймпад
  $('ctlSeg').addEventListener('click', e => {
    const b = e.target.closest('button'); if (!b) return;
    dlg.querySelectorAll('.ctl-view').forEach(v => { v.hidden = v.dataset.view !== b.dataset.view; });
  });

  // Входы: шестерёнка HUD (на пальце — в ☰), «Настройки» в паузе и в меню
  document.querySelectorAll('[title="Настройки"]').forEach(b => b.addEventListener('click', () => open()));

  window.kernScene.on(0, p => { const q = p.get('settings'); if (q) open(q === '1' ? 'graphics' : q); });
})();
