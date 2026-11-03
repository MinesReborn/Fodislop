// Запуск страницы-проверки стенда в headless Chrome и выдача результата сразу по готовности.
// --dump-dom с --virtual-time-budget ждёт весь бюджет, даже когда проверка давно закончилась; здесь итог читается из <title> через DevTools.
// Ошибка скрипта на странице проверки или в игре внутри неё — сразу FAIL с текстом ошибки, а не ожидание итога, который не наступит.
// node tools/run-audit.mjs 'tools/design-audit.html?pool=6' [#log #counts]  — печатает title и текст указанных элементов
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const page = process.argv[2] || 'tools/design-audit.html';
const show = process.argv.slice(3).length ? process.argv.slice(3) : ['#log'];
const url = `http://127.0.0.1:8765/Fodislop/visual/kern-ui/${page}`;
// Стенд не отвечает (сервер не запущен) — сразу сказать, а не ждать итога
if (!(await fetch(url).then(r => r.ok).catch(() => false))) { console.error(`стенд не отвечает: ${url} — запустить python3 -m http.server 8765 --bind 127.0.0.1 из каталога games/`); process.exit(2); }
const port = 9300 + Math.floor(Math.random() * 600);
const dir = mkdtempSync(join(tmpdir(), 'kern-audit-'));
const chrome = spawn('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ['--headless=new', '--disable-gpu', `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, '--window-size=1920,1080', 'about:blank'], { stdio: 'ignore' });
const stop = code => { chrome.kill(); setTimeout(() => { rmSync(dir, { recursive: true, force: true }); process.exit(code); }, 300); };
process.on('SIGTERM', () => stop(143)); process.on('SIGINT', () => stop(130));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const t0 = Date.now();

let target;
for (let i = 0; i < 60 && !target; i++) {
  await sleep(250);
  const list = await fetch(`http://127.0.0.1:${port}/json/list`).then(r => r.json()).catch(() => null);
  target = list?.find(t => t.type === 'page');
}
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r));
let id = 0; const wait = new Map();
const cmd = (method, params = {}) => new Promise(r => { const i = ++id; wait.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
ws.addEventListener('message', e => {
  const m = JSON.parse(e.data);
  if (m.id && wait.has(m.id)) { wait.get(m.id)(m); wait.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') {
    const d = m.params.exceptionDetails;
    console.log(`FAIL: ошибка скрипта (${Math.round((Date.now() - t0) / 1000)} с)\n${d.exception?.description || d.text} @ ${d.url || ''}:${d.lineNumber + 1}`);
    stop(2);
  }
});
await cmd('Runtime.enable');
await cmd('Page.enable');
await cmd('Page.navigate', { url });
const title = async () => (await cmd('Runtime.evaluate', { expression: 'document.title', returnByValue: true })).result?.result?.value || '';
let t;
for (;;) {
  await sleep(250);
  t = await title();
  if (/:\s*(ok|FAIL)/.test(t)) break;
  if (Date.now() - t0 > 5 * 60e3) { console.error(`проверка не закончилась за 5 мин; последний заголовок: ${t}`); stop(2); }
}
const expr = `JSON.stringify(${JSON.stringify(show)}.map(s => document.querySelector(s)?.textContent ?? ''))`;
const res = await cmd('Runtime.evaluate', { expression: expr, returnByValue: true });
console.log(t, `(${Math.round((Date.now() - t0) / 1000)} с)`);
for (const [i, text] of JSON.parse(res.result.result.value).entries()) console.log(`── ${show[i]}\n${text}`);
ws.close();
stop(/FAIL/.test(t) ? 1 : 0);
