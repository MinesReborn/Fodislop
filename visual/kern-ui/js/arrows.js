// Шевроны главного действия «›››» — значок спрайта i-arrows у правого края клавиши (systems.css .btn-arrows).
// Значок — дочерний svg, как .btn__arrows в игре; клавиши, которые скрипты создают или переписывают позже, получают его тоже
(() => {
  const SEL = '.btn--primary:not(.btn-ico-only), .btn--update';
  const NS = 'http://www.w3.org/2000/svg';
  const add = btn => {
    if (btn.querySelector(':scope > .btn-arrows')) return;
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('class', 'btn-arrows');
    svg.setAttribute('aria-hidden', 'true');
    const use = document.createElementNS(NS, 'use');
    use.setAttribute('href', '#i-arrows');
    svg.append(use);
    btn.append(svg);
  };
  const scan = root => {
    if (root.nodeType !== 1) return;
    if (root.matches(SEL)) add(root);
    root.querySelectorAll(SEL).forEach(add);
  };
  scan(document.body);
  new MutationObserver(list => list.forEach(m => {
    if (m.type === 'attributes') scan(m.target);
    else { m.addedNodes.forEach(scan); if (m.target.nodeType === 1 && m.target.matches(SEL)) add(m.target); }
  })).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
})();
