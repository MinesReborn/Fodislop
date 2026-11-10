// Снимки сцен стенда для взгляда: одна headless Chrome, вкладки параллельно, готовность — мир, шрифты и два кадра (как в design-audit),
// снимается только кадр устройства (#frame), JPEG — чтобы матрица не занимала гигабайты.
// node tools/shots.mjs [фильтр сцены] [устройства через запятую] — сцены и устройства берутся из tools/design-audit.html, язык чередуется ru → en → zh.
// Результат — shots/matrix/*.jpg и shots/matrix/list.txt; листы — tools/mosaic.html?page=N
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = readFileSync(join(root, 'tools/design-audit.html'), 'utf8');
const list = name => JSON.parse(src.match(new RegExp(`const ${name} = (\\[[\\s\\S]*?\\]);`))[1].replace(/'/g, '"').replace(/,\s*\]/, ']'));
const filter = process.argv[2] || '';
const only = (process.argv[3] || '').split(',').filter(Boolean);
const scenes = list('SCENES_ALL').filter(s => s.includes(filter)), devices = list('DEVICES_ALL').filter(d => !only.length || only.includes(d));
const out = join(root, 'shots/matrix'); mkdirSync(out, { recursive: true });
const POOL = 6, port = 9300 + Math.floor(Math.random() * 600), dir = mkdtempSync(join(tmpdir(), 'kern-shots-'));
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, 'about:blank'], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let ver; for (let i = 0; i < 60 && !ver; i++) { await sleep(250); ver = await fetch(`http://127.0.0.1:${port}/json/version`).then(r => r.json()).catch(() => null); }
const ws = new WebSocket(ver.webSocketDebuggerUrl); await new Promise(r => ws.addEventListener('open', r));
let id = 0; const wait = new Map();
ws.addEventListener('message', e => { const m = JSON.parse(e.data); if (m.id && wait.has(m.id)) { wait.get(m.id)(m); wait.delete(m.id); } });
const cmd = (method, params = {}, sessionId) => new Promise(r => { const i = ++id; wait.set(i, r); ws.send(JSON.stringify({ id: i, method, params, sessionId })); });
const ev = async (s, expression) => (await cmd('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, s)).result?.result?.value;

const READY = `(async () => { const f = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  await document.fonts.ready; await window.kernWorld?.ready; await f(); await new Promise(r => setTimeout(r, 450)); await f(); return 1; })()`;
const jobs = []; let n = 0;
for (const sc of scenes) for (const dev of devices) jobs.push([sc, dev, ['ru', 'en', 'zh'][n++ % 3]]);
const names = [];
const run = async ([sc, dev, lang]) => {
  const [addr, clicks] = sc.split('|');
  const { result: { targetId } } = await cmd('Target.createTarget', { url: 'about:blank' });
  const { result: { sessionId: s } } = await cmd('Target.attachToTarget', { targetId, flatten: true });
  await cmd('Emulation.setDeviceMetricsOverride', { width: 2200, height: 1300, deviceScaleFactor: 1, mobile: false }, s);
  await cmd('Page.enable', {}, s);
  await cmd('Page.navigate', { url: `http://127.0.0.1:8765/Fodislop/visual/kern-ui/index.html?${addr}&${dev}&lang=${lang}&still=1` }, s);
  await sleep(300); await ev(s, `new Promise(r => document.readyState === 'complete' ? r() : addEventListener('load', r))`);
  await ev(s, READY);
  if (clicks) { await ev(s, `${JSON.stringify(clicks.split(';'))}.forEach(c => document.querySelector(c)?.click())`); await ev(s, READY); }
  const box = await ev(s, `(() => { const r = document.getElementById('frame').getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height }; })()`);
  const shot = await cmd('Page.captureScreenshot', { format: 'jpeg', quality: 72, clip: { ...box, scale: Math.min(1, 1280 / box.width) } }, s);
  const name = `${sc.replace(/[=&|#\[\]\s.:()]+/g, '_')}@${dev.replace(/device=|&input=|&padhud=1/g, m => m === '&padhud=1' ? '-padhud' : m === 'device=' ? '' : '-')}-${lang}`;
  writeFileSync(join(out, name + '.jpg'), Buffer.from(shot.result.data, 'base64'));
  names.push([jobs.indexOf(jobs.find(j => j[0] === sc && j[1] === dev)), name]);
  await cmd('Target.closeTarget', { targetId });
};
const t0 = Date.now(), queue = [...jobs];
await Promise.all(Array.from({ length: POOL }, async () => { while (queue.length) await run(queue.shift()); }));
writeFileSync(join(out, 'list.txt'), names.sort((a, b) => a[0] - b[0]).map(x => x[1]).join('\n') + '\n');
console.log(`${names.length} снимков за ${Math.round((Date.now() - t0) / 1000)} с → shots/matrix`);
ws.close(); chrome.kill(); setTimeout(() => rmSync(dir, { recursive: true, force: true }), 300);
