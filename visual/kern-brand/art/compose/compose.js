// Компоновщик шаблонов (иконки, витрины, соцсети). Страница шаблонов объявляет список кадров и рисовальщик:
//   KernCompose.run(ASSETS, render)
//   ASSETS: [{ id, w, h, group, label, out, alpha?, note?, gallery? }] — out: путь PNG внутри export/renders (нет — кадр только для галереи);
//   alpha — прозрачный фон; gallery: false — кадр только в экспорт (повтор того же в другой плотности)
//   render(root, asset) → Promise: заполняет root размером w×h
// Без параметров — галерея всех кадров уменьшенно, с подписью размера. ?asset=<id> — один кадр в натуральную величину
// для экспорта (export/tools/export.mjs): готовность — document.title === 'ready', ошибка — 'error: …'.
(() => {
  const q = new URLSearchParams(location.search);
  async function fontsReady() { await document.fonts.ready; await Promise.all([...document.fonts].map(f => f.status === 'unloaded' ? f.load().catch(() => {}) : 0)); }
  async function imagesReady(root) {
    await Promise.all([...root.querySelectorAll('img')].map(i => i.complete ? 0 : new Promise(r => { i.onload = i.onerror = r; })));
  }
  function frame(a) {
    const root = document.createElement('div');
    root.className = 'k-asset';
    root.dataset.asset = a.id;
    root.style.width = a.w + 'px'; root.style.height = a.h + 'px';
    root.style.setProperty('--w', a.w); root.style.setProperty('--h', a.h);
    root.style.setProperty('--u', Math.min(a.w, a.h) / 100 + 'px');   // единица раскладки — сотая короткой стороны
    if (a.alpha) root.classList.add('is-alpha');
    return root;
  }
  window.KernCompose = {
    asset: q.get('asset'),
    async run(assets, render) {
      window.KERN_ASSETS = assets;
      try {
        await fontsReady();
        if (this.asset) {
          const a = assets.find(x => x.id === this.asset);
          if (!a) throw new Error(`нет кадра ${this.asset}`);
          document.documentElement.classList.add('k-single');
          const root = frame(a);
          document.body.replaceChildren(root);
          await render(root, a);
          await imagesReady(root);
          await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
          document.title = 'ready';
          return;
        }
        // Галерея: по группам, каждый кадр уменьшен до ширины колонки
        const shown = assets.filter(a => a.gallery !== false);
        const groups = [...new Set(shown.map(a => a.group))];
        const page = document.querySelector('[data-gallery]') || document.body;
        for (const g of groups) {
          const sec = document.createElement('section');
          sec.className = 'k-group';
          sec.innerHTML = `<p class="w-kicker"><b>${g}</b></p><div class="k-cards"></div>`;
          page.append(sec);
          for (const a of shown.filter(x => x.group === g)) {
            const card = document.createElement('figure');
            card.className = 'k-card' + (a.wide ? ' is-wide' : '');
            const box = document.createElement('div');
            box.className = 'k-box' + (a.alpha ? ' is-alpha' : '');
            const root = frame(a);
            box.append(root);
            card.append(box);
            card.insertAdjacentHTML('beforeend', `<figcaption><b>${a.label}</b><span>${a.sheet ? 'натуральная величина' : `${a.w}×${a.h}${a.alpha ? ' · прозрачный' : ''}`}</span>${a.note ? `<small>${a.note}</small>` : ''}${a.out ? `<a class="w-link" href="?asset=${a.id}">кадр</a>` : ''}</figcaption>`);
            sec.querySelector('.k-cards').append(card);
            await render(root, a);
            // Кадр вписан в ячейку высотой не меньше 200: мелкие иконки — в натуральную величину по центру, а не полоской
            // Лист размеров не масштабируется (натуральная величина) — на узком экране ячейка прокручивается
            if (a.sheet) box.classList.add('is-scroll');
            const fit = () => { const s = a.sheet ? 1 : Math.min(1, box.clientWidth / a.w, 420 / a.h), H = Math.max(a.sheet ? 0 : 200, a.h * s);
              box.style.height = H + 'px';
              Object.assign(root.style, { position: 'absolute', left: Math.max(0, (box.clientWidth - a.w * s) / 2) + 'px', top: (H - a.h * s) / 2 + 'px', transform: `scale(${s})` }); };
            fit(); new ResizeObserver(fit).observe(box);
          }
        }
        document.title = document.title.replace(/^…\s*/, '');
      } catch (e) {
        document.title = 'error: ' + e.message;
        throw e;
      }
    },
    // Общие куски кадров
    lockup: (size, opts = {}) => `<div class="k-lockup" style="--s:${size}px">${opts.noMark ? '' : `<img src="${MARK}" alt="">`}<b>Project Kern</b></div>`,
    // Ключевой кадр: игровой момент из клеток (scene.js, moment), затемнение со стороны текста, знак с названием, слоган, строка тем.
    // Сначала кладётся текст, по его правому краю ставится точка внимания сцены — робот в забое всегда на открытом месте.
    // o: slogan (строка или false), kicker, kickerText, fs — кегль слогана в --u, lock — кегль названия, width — доля ширины под текст, text: false — только фон
    keyArt: async (root, a, o = {}) => {
      const C = window.KernCompose;
      const tall = a.w / a.h < .95;
      let box = null;
      if (o.text !== false) {
        box = document.createElement('div');
        // Две иерархии. Альбом и капсулы: ведёт название (магазин требует читаемый логотип), слоган — второй строкой текстом.
        // Портрет: ведёт слоган крупным заголовком, название — подписью над ним
        if (tall) {
          const lock = o.lock ?? Math.round(a.w * .05);
          box.style.cssText = `left:${a.w * .08}px;right:${a.w * .08}px;bottom:${a.h * .07}px;display:flex;flex-direction:column;gap:${lock * .7}px`;
          box.innerHTML = C.lockup(lock)
            + (o.slogan ? `<p class="k-slogan" style="--fs:${o.fs ?? 10}">${o.slogan}</p>` : '')
            + (o.kicker ? `<p class="k-kicker" style="font-size:${Math.max(11, Math.round(lock * .42))}px"><i></i>${o.kickerText}</p>` : '');
        } else {
          const lock = o.lock ?? Math.round(Math.min(a.h * .12, a.w * .058));
          box.style.cssText = `left:${a.w * .065}px;top:0;bottom:0;width:${a.w * (o.width ?? .62)}px;display:flex;flex-direction:column;align-items:flex-start;justify-content:center;gap:${lock * .36}px`;
          box.innerHTML = C.lockup(lock)
            + (o.slogan ? `<p class="k-sub" style="font-size:${Math.round(lock * .44)}px;padding-left:${lock * 1.5}px">${o.slogan}</p>` : '')
            + (o.kicker ? `<p class="k-kicker" style="font-size:${Math.max(11, Math.round(lock * .2))}px;padding-left:${lock * 1.5}px;margin-top:${lock * .2}px"><i></i>${o.kickerText}</p>` : '');
        }
        root.append(box);
      }
      const ext = C.textExtent(box);
      if (tall) {
        // Портрет: момент вверху, над текстом; затемнение снизу
        const cell = C.momentCell(a.w, 13);   // 13 клеток сцены — во всю ширину
        const top = ext ? ext.top : a.h;
        await C.scene(root, { moment: true, cell, seed: o.seed ?? 11, depth: o.depth ?? 84, fx: .5, fy: Math.min(.42, Math.max(.2, (top - cell * 3) / a.h)) });
        C.scrim(root, `linear-gradient(0deg, rgb(var(--rgb-void) / .96) 0, rgb(var(--rgb-void) / .9) ${a.h - top + cell * .5}px, rgb(var(--rgb-void) / .15) ${a.h - top + cell * 2.5}px, rgb(var(--rgb-void) / .1) 100%)`);
      } else {
        const cell = C.momentCell(a.h, 16);
        const right = ext ? ext.right : a.w * .3;
        await C.scene(root, { moment: true, cell, seed: o.seed ?? 11, depth: o.depth ?? 84, fx: Math.min(a.w - cell * 4, right + cell * 7.5) / a.w, fy: .56 });
        C.scrim(root, `linear-gradient(90deg, rgb(var(--rgb-void) / .96) 0, rgb(var(--rgb-void) / .9) ${right}px, rgb(var(--rgb-void) / .15) ${right + cell * 1.5}px, rgb(var(--rgb-void) / .1) 100%)`);
      }
      if (box && o.ticks !== false) root.insertAdjacentHTML('beforeend', '<div class="k-ticks"></div>');
    },
    // Клетка момента: короткая сторона делится на n клеток, размер кратен 16 — текстура 32 px масштабируется ровно
    momentCell: (side, n) => 16 * Math.max(2, Math.round(side / n / 16)),
    // Затемнение кладётся сразу над сценой, под текстом
    scrim: (root, bg) => { const s = document.createElement('div'); s.className = 'k-scrim'; s.style.background = bg; root.querySelector('.k-scene').after(s); },
    // Границы текста в пикселях кадра — по содержимому, а не по коробке (до масштабирования галереи, offset-координаты)
    textExtent: box => {
      if (!box) return null;
      let right = 0, top = Infinity;
      for (const el of box.children) { right = Math.max(right, box.offsetLeft + el.offsetLeft + el.offsetWidth); top = Math.min(top, box.offsetTop + el.offsetTop); }
      return { right, top };
    },
    scene: async (root, opts = {}) => {
      const c = document.createElement('canvas');
      c.className = 'k-scene';
      root.prepend(c);
      const a = { w: +root.style.getPropertyValue('--w'), h: +root.style.getPropertyValue('--h') };
      const cell = opts.cell ?? 32 * Math.max(1, Math.round(Math.min(a.w, a.h) / 420));
      await KernScene.paint(c, { width: a.w, height: a.h, cell, ratio: 1, keepStyle: true, ...opts });
      return c;
    },
  };
  const MARK = new URL('../../../kern-ui/img/brand/mark.svg', document.currentScript.src).href;
  window.KernCompose.MARK = MARK;
  window.KernCompose.MARKS = new URL('../../marks/svg/', document.currentScript.src).href;
})();
