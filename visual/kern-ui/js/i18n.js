// Язык интерфейса: русский, английский, китайский (упрощённый). Словарь — js/i18n-data.js (tools/i18n-build.py):
// словарь игры и строки макета. Макет пишется по-русски; при другом языке каждый текст и подсказка в кадре
// меняются на перевод, а новый текст, который рисует код, переводится сразу, как появляется.
// Текст сервера и игроков (миссии, письма, чат, имена) не переводится: его присылает сервер.
(() => {
  const $ = id => document.getElementById(id);
  const frame = $('frame'), D = window.I18N;
  const COL = { en: 0, zh: 1 };
  const ATTRS = ['title', 'placeholder', 'aria-label'];
  // Шаблон: {n} — подставляемое значение, остальное — буквально
  const TPL = D.tpl.map(([ru, en, zh]) => [new RegExp('^' + ru.split(/(\{\d+\})/).map(t => /^\{\d+\}$/.test(t) ? '(.+?)' : t.replace(/[.*+?^$()|[\]\\]/g, '\\$&')).join('') + '$'),
    [...ru.matchAll(/\{(\d+)\}/g)].map(m => +m[1]), [en, zh]]);
  let lang = 'ru';

  function translate(src) {
    const core = src.replace(/\s+/g, ' ').trim();
    if (!core || !/[А-Яа-яЁё]/.test(core)) return null;
    let out = D.exact[core]?.[COL[lang]];
    if (out == null) for (const [rx, order, tr] of TPL) {
      const m = core.match(rx); if (!m) continue;
      const val = {}; order.forEach((n, i) => { val[n] = m[i + 1]; });
      out = tr[COL[lang]].replace(/\{(\d+)\}/g, (_, n) => val[n] ?? ''); break;
    }
    if (out == null) return null;
    return src.match(/^\s*/)[0] + out + src.match(/\s*$/)[0];   // пробелы вокруг — как были
  }

  // Узлы и атрибуты помнят свой русский оригинал; своя замена не переводится повторно
  const textOrig = new Map(), mine = new WeakMap(), attrOrig = new Map();
  const htmlOrig = new Map();
  const skip = el => el?.closest('script, style, .stand-bar, [translate="no"]');
  function text(node) {
    if (lang === 'ru' || mine.get(node) === node.data || skip(node.parentElement) || node.parentElement?.closest('[data-i18n-html]')) return;
    const loc = node.parentElement?.dataset.loc;               // только свой текст элемента: подпись метки в клетке — не команда
    const tr = loc && D.keys[loc] ? node.data.replace(/\S.*\S|\S/s, D.keys[loc][COL[lang]]) : translate(node.data);
    if (tr == null) return;
    textOrig.set(node, node.data); node.data = tr; mine.set(node, tr);
  }
  function attrs(el) {
    if (lang === 'ru' || skip(el)) return;
    ATTRS.forEach(a => {
      const v = el.getAttribute(a); if (!v) return;
      const rec = attrOrig.get(el)?.[a];
      if (rec && rec.mine === v) return;
      // Команды программатора — по ключу словаря игры: одна русская строка бывает у разных команд и настроек
      const loc = a === 'title' && el.dataset.loc, tr = loc && D.keys[loc] ? D.keys[loc][COL[lang]] : translate(v);
      if (tr == null) return;
      attrOrig.set(el, { ...attrOrig.get(el), [a]: { src: v, mine: tr } });
      el.setAttribute(a, tr);
    });
  }
  // Текст с разметкой внутри (совет на спуске) переводится целиком, иначе рвётся фраза
  function html(el) {
    if (lang === 'ru') return;
    if (htmlOrig.get(el)?.mine === el.innerHTML) return;
    const src = el.innerHTML, tr = translate(src); if (tr == null) return;
    el.innerHTML = tr; htmlOrig.set(el, { src, mine: el.innerHTML });
  }
  function walk(root) {
    if (root.nodeType === 3) return text(root);
    if (root.nodeType !== 1) return;
    if (root.matches('[data-i18n-html]')) html(root);
    root.querySelectorAll('[data-i18n-html]').forEach(html);
    const it = document.createTreeWalker(root, NodeFilter.SHOW_TEXT); let n;
    while ((n = it.nextNode())) text(n);
    attrs(root); root.querySelectorAll('[title], [placeholder], [aria-label]').forEach(attrs);
  }
  const obs = new MutationObserver(list => {
    for (const m of list) {
      if (m.type === 'characterData') text(m.target);
      else if (m.type === 'attributes') attrs(m.target);
      else {
        const host = m.target.closest?.('[data-i18n-html]');
        if (host) html(host); else m.addedNodes.forEach(walk);
      }
    }
  });

  function set(l) {
    obs.disconnect();
    // Вернуть русский: только то, что ещё стоит нашей заменой
    textOrig.forEach((src, node) => { if (node.isConnected && mine.get(node) === node.data) node.data = src; });
    attrOrig.forEach((rec, el) => Object.entries(rec).forEach(([a, { src, mine: m }]) => { if (el.getAttribute(a) === m) el.setAttribute(a, src); }));
    htmlOrig.forEach(({ src, mine: m }, el) => { if (el.innerHTML === m) el.innerHTML = src; });
    textOrig.clear(); attrOrig.clear(); htmlOrig.clear();
    lang = l in COL ? l : 'ru';
    frame.lang = lang === 'zh' ? 'zh-Hans' : lang; document.documentElement.lang = frame.lang;
    document.querySelectorAll('.lang-pick').forEach(s => { s.value = lang; });
    if (lang !== 'ru') {
      walk(frame);
      obs.observe(frame, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: [...ATTRS, 'data-loc'] });
    }
  }
  window.kernLang = { set, get lang() { return lang; } };
  // Выбор языка: стенд, онбординг, настройки — один язык на всех
  document.querySelectorAll('.lang-pick').forEach(s => s.addEventListener('change', () => {
    if (s.id === 'standLang') window.kernText?.set('ru');      // проверка текста — по русскому, вместе с переводом не работает
    set(s.value);
  }));
  // Язык — последним: переводит уже открытые сцену и окна
  window.kernScene.on(3, p => { if (p.get('lang')) set(p.get('lang')); });
  window.kernScene.onReset(() => { if (lang !== 'ru') set('ru'); });
})();
