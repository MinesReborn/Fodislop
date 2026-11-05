// Экспорт бренда Project Kern в файлы: снимки стенда, все кадры шаблонов в PNG, упаковка иконок по платформам.
// Нужен стенд: python3 -m http.server 8765 --bind 127.0.0.1 из каталога games/ (как у проверок kern-ui).
//   node visual/kern-brand/export/tools/export.mjs            — всё по порядку
//   node visual/kern-brand/export/tools/export.mjs screens    — только снимки стенда (нужны витринам)
//   node visual/kern-brand/export/tools/export.mjs assets [icons|stores|social]  — кадры шаблонов
//   node visual/kern-brand/export/tools/export.mjs pack       — .icns, .ico, Android XML, Linux .desktop, проверка текстов
// Результат — visual/kern-brand/export/renders/ (в git не идёт). Одна headless Chrome, кадры по одному.
import { spawn, execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, mkdirSync, writeFileSync, readFileSync, copyFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const tools = dirname(fileURLToPath(import.meta.url));
const brand = join(tools, '../..');
const out = join(brand, 'export/renders');
const BASE = 'http://127.0.0.1:8765/Fodislop/visual';
const [step = 'all', only] = process.argv.slice(2);
if (!(await fetch(`${BASE}/kern-ui/index.html`).then(r => r.ok).catch(() => false))) {
  console.error(`стенд не отвечает: ${BASE} — запустить python3 -m http.server 8765 --bind 127.0.0.1 из каталога games/`); process.exit(2);
}

// ── Chrome по DevTools ──
const port = 9300 + Math.floor(Math.random() * 600), dir = mkdtempSync(join(tmpdir(), 'kern-export-'));
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-color-profile=srgb', `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, 'about:blank'], { stdio: 'ignore' });
const stop = code => { chrome.kill(); setTimeout(() => { rmSync(dir, { recursive: true, force: true }); process.exit(code); }, 300); };
process.on('SIGINT', () => stop(130));
const sleep = ms => new Promise(r => setTimeout(r, ms));
let target; for (let i = 0; i < 60 && !target; i++) { await sleep(250); target = (await fetch(`http://127.0.0.1:${port}/json/list`).then(r => r.json()).catch(() => []))?.find(t => t.type === 'page'); }
const ws = new WebSocket(target.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
let id = 0; const wait = new Map(), errors = [];
ws.addEventListener('message', e => { const m = JSON.parse(e.data); if (m.id && wait.has(m.id)) { wait.get(m.id)(m); wait.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text); });
const cmd = (method, params = {}) => new Promise((res, rej) => { const i = ++id; wait.set(i, m => m.error ? rej(new Error(`${method}: ${m.error.message}`)) : res(m.result)); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async expression => { const r = await cmd('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
await cmd('Page.enable'); await cmd('Runtime.enable');
// Без кэша: снимки стенда перезаписываются по ходу экспорта, витрины должны брать свежие
await cmd('Network.enable'); await cmd('Network.setCacheDisabled', { cacheDisabled: true });
async function open(url, w, h, alpha = false) {
  await cmd('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
  await cmd('Emulation.setDefaultBackgroundColorOverride', alpha ? { color: { r: 0, g: 0, b: 0, a: 0 } } : {});
  errors.length = 0;
  await cmd('Page.navigate', { url });
  await sleep(150);
  for (let i = 0; i < 200; i++) { if (await ev('document.readyState').catch(() => '') === 'complete') break; await sleep(50); }
}
const save = (file, b64) => { mkdirSync(dirname(file), { recursive: true }); writeFileSync(file, Buffer.from(b64, 'base64')); };

// ── 1. Снимки стенда для витрин: сцена × устройство × язык, только кадр (#frame) ──
async function screens() {
  const { scenes, devices, langs } = await (async () => {
    // Список сцен — без галереи: галерея грузит старые снимки, и Chrome отдал бы их из кэша при сборке витрин
    await open(`${BASE}/kern-brand/distribution/stores/index.html?asset=__list`, 1400, 900);
    for (let i = 0; i < 100 && !(await ev('!!window.KERN_SCREENS')); i++) await sleep(100);
    return ev('window.KERN_SCREENS');
  })();
  // Масштаб снимка: ширина результата — с запасом под витрину (App Store 2868, iPad 2752)
  const WIDTH = { pc: 1920, phone: 2400, tablet: 2502 };
  for (const [dev, dq] of Object.entries(devices)) for (const lang of langs) for (const [name, q] of Object.entries(scenes)) {
    await open(`${BASE}/kern-ui/index.html?${[q, dq, `lang=${lang}`, 'still=1'].filter(Boolean).join('&')}`, 2200, 1400);
    await ev(`(async () => { const f = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
      for (let i = 0; i < 200 && document.documentElement.hasAttribute('data-kern-booting'); i++) await new Promise(r => setTimeout(r, 50));
      await document.fonts.ready; await window.kernWorld?.ready; await f(); await new Promise(r => setTimeout(r, 500)); await f();
      // Витрине нужен экран игры, а не стенд: без выреза камеры и скругления корпуса.
      // Без диагностики: строка версии и FPS, строка состояния (онлайн, ассеты) и догрузка ресурсов есть только в сборке для разработки
      const s = document.createElement('style'); s.textContent = '.cutout,.diag,.status-bar,#assetLoad{display:none!important}.frame.bezel{border-radius:0!important;box-shadow:none!important}'; document.head.append(s); await f(); return 1; })()`);
    const r = await ev(`(() => { const b = document.getElementById('frame').getBoundingClientRect(); return { x: b.left, y: b.top, w: b.width, h: b.height }; })()`);
    const shot = await cmd('Page.captureScreenshot', { format: 'png', clip: { x: r.x, y: r.y, width: r.w, height: r.h, scale: WIDTH[dev] / r.w } });
    const file = join(out, 'screens', dev, lang, `${name}.png`);
    save(file, shot.data);
    if (errors.length) throw new Error(`${file}: ошибка на странице — ${errors[0]}`);
    console.log('снимок', dev, lang, name);
  }
}

// ── 2. Кадры шаблонов: страница отдаёт список (window.KERN_ASSETS), каждый кадр — ?asset=id в натуральную величину ──
const PAGES = { icons: 'distribution/icons/index.html', stores: 'distribution/stores/index.html', social: 'social/templates/index.html' };
async function assets() {
  for (const [name, page] of Object.entries(PAGES)) {
    if (only && only !== name) continue;
    await open(`${BASE}/kern-brand/${page}?asset=__list`, 800, 600);
    for (let i = 0; i < 100 && !(await ev('!!window.KERN_ASSETS')); i++) await sleep(100);
    // Кадры без out — только для галереи (листы размеров, маски), в файлы не идут
    const list = await ev('window.KERN_ASSETS.filter(a => a.out).map(({ id, w, h, out, alpha }) => ({ id, w, h, out, alpha: !!alpha }))');
    for (const a of list) {
      await open(`${BASE}/kern-brand/${page}?asset=${a.id}`, a.w, a.h, a.alpha);
      let title = '';
      for (let i = 0; i < 400; i++) { title = await ev('document.title'); if (title === 'ready' || title.startsWith('error')) break; await sleep(50); }
      if (title !== 'ready') throw new Error(`${name}/${a.id}: ${title || 'нет готовности'} ${errors[0] || ''}`);
      const shot = await cmd('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: a.w, height: a.h, scale: 1 }, captureBeyondViewport: false });
      save(join(out, a.out), shot.data);
      console.log(name, a.id, `${a.w}×${a.h}`);
    }
  }
}

// ── 3. Упаковка по платформам ──
function pack() {
  const ic = join(out, 'icons');
  // iOS: без альфа-канала
  execFileSync('python3', ['-I', join(tools, 'strip-alpha.py'), join(ic, 'ios/AppIcon-1024.png')], { stdio: 'inherit' });
  // macOS: iconset → .icns
  const set = join(ic, 'macos/ProjectKern.iconset');
  mkdirSync(set, { recursive: true });
  for (const s of [16, 32, 128, 256, 512]) {
    copyFileSync(join(ic, `macos/${s}.png`), join(set, `icon_${s}x${s}.png`));
    copyFileSync(join(ic, `macos/${s * 2}.png`), join(set, `icon_${s}x${s}@2x.png`));
  }
  execFileSync('iconutil', ['-c', 'icns', set, '-o', join(ic, 'macos/ProjectKern.icns')], { stdio: 'inherit' });
  console.log('macOS', join(ic, 'macos/ProjectKern.icns'));
  // Windows: .ico
  execFileSync('python3', ['-I', join(tools, 'pack-ico.py'), join(ic, 'windows/ProjectKern.ico'), ...[16, 24, 32, 48, 64, 128, 256].map(s => join(ic, `windows/${s}.png`))], { stdio: 'inherit' });
  // Android: адаптивная иконка (8.0+) поверх запасных PNG
  const xml = `<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@mipmap/ic_launcher_background"/>
    <foreground android:drawable="@mipmap/ic_launcher_foreground"/>
    <monochrome android:drawable="@mipmap/ic_launcher_monochrome"/>
</adaptive-icon>
`;
  mkdirSync(join(ic, 'android/res/mipmap-anydpi-v26'), { recursive: true });
  writeFileSync(join(ic, 'android/res/mipmap-anydpi-v26/ic_launcher.xml'), xml);
  writeFileSync(join(ic, 'android/res/mipmap-anydpi-v26/ic_launcher_round.xml'), xml);
  console.log('Android', join(ic, 'android/res'));
  // Linux: ярлык
  writeFileSync(join(ic, 'linux/project-kern.desktop'), `[Desktop Entry]
Type=Application
Name=Project Kern
Comment=Под землёй тоже есть политика.
Comment[en]=Politics runs deep.
Exec=ProjectKern
Icon=project-kern
Terminal=false
Categories=Game;Simulation;
`);
  console.log('Linux', join(ic, 'linux/project-kern.desktop'));
  // Тексты карточек: длины в пределах площадок
  const src = readFileSync(join(brand, 'distribution/stores/store-copy.js'), 'utf8');
  const at = src.indexOf('window.KERN_STORE_COPY =');
  const copy = JSON.parse(src.slice(src.indexOf('=', at) + 1, src.lastIndexOf(';')));
  const over = [];
  for (const lang of ['ru', 'en']) for (const [k, max] of Object.entries(copy.limits)) {
    const v = k === 'playFull' ? copy[lang].full.join('\n\n') : copy[lang][k];
    if (v.length > max) over.push(`${lang}.${k}: ${v.length} > ${max}`);
  }
  if (over.length) throw new Error('тексты длиннее правил площадки: ' + over.join(', '));
  console.log('тексты карточек: в пределах');
}

try {
  if (step === 'all' || step === 'screens') await screens();
  if (step === 'all' || step === 'assets') await assets();
  if (step === 'all' || step === 'pack') pack();
  console.log('export: ok →', out);
  stop(0);
} catch (e) {
  console.error('export: FAIL —', e.message);
  stop(1);
}
