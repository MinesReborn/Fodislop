// Токены Project Kern для переноса: читает единственный источник — visual/kern-ui/css/tokens.css —
// и пишет рядом две производные:
//   tokens.json — формат W3C Design Tokens (DTCG): значения разрешены до конца, у каждого — CSS-имя, статус переноса и пояснение из исходника;
//   tokens.js   — то же для страниц книги (открываются и из файла);
//   tokens.uss  — как токены лягут в Unity UI Toolkit: только то, что USS принимает переменной; остальное перечислено в шапке с указанием способа.
// node visual/kern-brand/tokens/export/build-tokens.mjs           — записать
// node visual/kern-brand/tokens/export/build-tokens.mjs --check   — выход 1, если производные разошлись с tokens.css
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repo = join(here, '../../../..');
const source = join(repo, 'visual/kern-ui/css/tokens.css');
const outJson = join(here, 'tokens.json');
const outUss = join(here, 'tokens.uss');
const outJs = join(here, 'tokens.js');
const check = process.argv.includes('--check');

// ── Разбор :root: объявления по порядку, с комментарием в той же строке и заголовком блока ──
function parse(css) {
  // Первый блок :root — до парной скобки: после него в файле идут переопределения (движение выключено)
  const open = css.indexOf(':root {') + 7;
  let close = open;
  for (let depth = 1; depth; close++) depth += css[close] === '{' ? 1 : css[close] === '}' ? -1 : 0;
  const root = css.slice(open, close - 1);
  const decls = [];
  let section = '', block = '', i = 0;
  while (i < root.length) {
    if (root.startsWith('/*', i)) {
      const end = root.indexOf('*/', i) + 2;
      const text = root.slice(i + 2, end - 2).replace(/\s+/g, ' ').trim();
      const m = text.match(/^── (?:\d\. )?([^─:]+?)(?::| ─)/);
      if (m) { section = m[1].trim(); block = ''; }
      else {
        // Комментарий сразу после значения на той же строке — пояснение к объявлению, иначе — заголовок группы
        const before = root.slice(root.lastIndexOf('\n', i) + 1, i);
        if (decls.length && /;\s*$/.test(before)) decls[decls.length - 1].note ||= text;
        else block = text;
      }
      i = end;
      continue;
    }
    if (root.startsWith('--', i)) {
      const colon = root.indexOf(':', i);
      let j = colon + 1, depth = 0, quote = '';
      for (; j < root.length; j++) {
        const c = root[j];
        if (quote) { if (c === quote) quote = ''; continue; }
        if (c === '"' || c === "'") quote = c;
        else if (c === '(') depth++;
        else if (c === ')') depth--;
        else if (c === ';' && depth === 0) break;
      }
      decls.push({ name: root.slice(i, colon).trim(), raw: root.slice(colon + 1, j).replace(/\s+/g, ' ').trim(), section, block });
      i = j + 1;
      continue;
    }
    i++;
  }
  return decls;
}

const decls = parse(readFileSync(source, 'utf8'));
const byName = new Map(decls.map(d => [d.name, d]));
const resolve = (raw, seen = new Set()) => raw.replace(/var\((--[\w-]+)\)/g, (_, n) => {
  if (seen.has(n) || !byName.has(n)) throw new Error(`токен ${n} не найден или ссылается сам на себя`);
  return resolve(byName.get(n).raw, new Set([...seen, n]));
});
for (const d of decls) d.value = resolve(d.raw);
// Шкалы описываются по имени: ступень и шаг понятны из числа, но в таблице переноса графа «назначение» не должна пустовать
for (const d of decls) {
  if (d.note) continue;
  let m;
  if ((m = d.name.match(/^--t-(\d+)$/))) d.note = `ступень ${m[1]} шкалы кеглей`;
  else if ((m = d.name.match(/^--sp-(\d+)$/))) d.note = `${m[1]} × шаг 2 px`;
  else if ((m = d.name.match(/^--fw-(\w+)$/))) d.note = { semi: 'подписи, кнопки', bold: 'заголовки, выбранное', heavy: 'название, крупные заголовки' }[m[1]];
}

// ── Цвет: rgb(r g b / a), #hex → #rrggbb или #rrggbbaa ──
const hex2 = n => Math.round(n).toString(16).padStart(2, '0');
function color(v) {
  let m = v.match(/^rgb\((\d+) (\d+) (\d+)(?: \/ ([\d.]+))?\)$/);
  if (m) {
    const a = m[4] === undefined ? 1 : parseFloat(m[4]);
    return { hex: '#' + [m[1], m[2], m[3]].map(x => hex2(+x)).join('') + (a < 1 ? hex2(a * 255) : ''), rgb: [+m[1], +m[2], +m[3]], a };
  }
  m = v.match(/^(\d+) (\d+) (\d+)$/);                 // канал палитры --rgb-*: «r g b»
  if (m) return { hex: '#' + [m[1], m[2], m[3]].map(x => hex2(+x)).join(''), rgb: [+m[1], +m[2], +m[3]], a: 1 };
  m = v.match(/^#([0-9a-f]{6})$/i);
  if (m) return { hex: '#' + m[1].toLowerCase(), rgb: [0, 2, 4].map(k => parseInt(m[1].slice(k, k + 2), 16)), a: 1 };
  return null;
}

// ── Тип и группа по имени; статус переноса — по блоку исходника ──
const kind = d => {
  const n = d.name;
  if (n.startsWith('--rgb-')) return 'channel';
  if (/^--(font|cjk)/.test(n)) return 'fontFamily';
  if (n.startsWith('--fw-')) return 'fontWeight';
  if (n.startsWith('--ls-')) return 'letterSpacing';
  if (/^--(t-|sp-|ctl-|r-|radius|hit|row|gap|margin|col|notch$|tick$)/.test(n)) return 'dimension';
  if (n.startsWith('--z-')) return 'zIndex';
  if (n.startsWith('--dur') || ['--step', '--count', '--flash'].includes(n)) return 'duration';
  if (n === '--ease') return 'cubicBezier';
  if (n.startsWith('--shadow') || n === '--press') return 'shadow';
  if (color(d.value)) return 'color';
  return 'composite';
};
const group = d => {
  if (d.section.startsWith('Стенд')) return 'stand';
  if (d.section.startsWith('Данные')) return 'data';
  return { channel: 'palette', color: 'color', fontFamily: 'font', fontWeight: 'font', letterSpacing: 'font', dimension: 'size', zIndex: 'layer',
    duration: 'motion', cubicBezier: 'motion', shadow: 'shadow', composite: 'micrographics' }[kind(d)];
};
const transfer = d => {
  const g = group(d), k = kind(d);
  if (g === 'stand') return { to: 'none', how: 'рамка стенда, в игре её нет' };
  if (g === 'data') return { to: 'data', how: 'таблица данных игры или цвет из пакета сервера; в USS не переносится' };
  if (k === 'channel') return { to: 'none', how: 'канал палитры: в USS нет rgb(var() / a) — переносятся собранные цвета' };
  if (k === 'zIndex') return { to: 'hierarchy', how: 'в UI Toolkit слой задаёт порядок элементов в дереве и sortingOrder панели' };
  if (k === 'fontFamily') return { to: 'asset', how: 'FontAsset (SDF) с выровненной коробкой строки 1.2 em; запасной CJK — fallback-цепочка ассета' };
  if (k === 'cubicBezier') return { to: 'uss', how: 'transition-timing-function: ease-out' };
  if (k === 'letterSpacing') return { to: 'site', how: 'разрядка в em: letter-spacing в USS — только px или %, переводится в px по кеглю элемента на месте' };
  if (k === 'shadow') return { to: 'border', how: 'box-shadow в USS нет: inset-линия — border-top, отбивка — отдельный элемент-подложка со смещением' };
  if (k === 'composite') return { to: 'texture', how: d.name.includes('clip') ? 'срез угла — VectorImage/9-slice рамки' : 'фоновый узор — 9-slice или повторяемая текстура' };
  return { to: 'uss', how: '1:1' };
};

// ── tokens.json (DTCG) ──
const json = { $description: 'Project Kern — токены интерфейса. Производная от visual/kern-ui/css/tokens.css, руками не править.' };
for (const d of decls) {
  const k = kind(d), g = group(d), t = transfer(d);
  const c = color(d.value);
  let $type = k, $value = d.value;
  if (k === 'channel' || k === 'color') { $type = 'color'; $value = c ? c.hex : d.value; }
  else if (k === 'dimension' && /^-?[\d.]+(px|%)$/.test(d.value)) $value = d.value;
  else if (k === 'duration') $value = `${Math.round(parseFloat(d.value) * 1000)}ms`;
  else if (k === 'fontWeight' || k === 'zIndex') $value = Number(d.value);
  else if (k === 'fontFamily') $value = d.value.split(',').map(s => s.trim().replace(/^"|"$/g, ''));
  else if (k === 'composite' || k === 'shadow' || k === 'letterSpacing' || k === 'cubicBezier') $type = k === 'composite' ? 'other' : k;
  if (k === 'composite' && !c) $type = 'other';
  const key = d.name.slice(2);
  (json[g] ||= {})[key] = { $type, $value, $description: d.note || undefined,
    $extensions: { 'kern.css': d.name, ...(d.block ? { 'kern.block': d.block } : {}), 'kern.transfer': t.to, ...(t.how !== '1:1' ? { 'kern.how': t.how } : {}) } };
}
const jsonText = JSON.stringify(json, null, 2) + '\n';

// ── tokens.uss ──
const ussValue = d => {
  const c = color(d.value);
  if (c) return c.a < 1 ? `rgba(${c.rgb.join(', ')}, ${+c.a.toFixed(3)})` : c.hex;
  if (kind(d) === 'duration') return d.value.replace(/^\./, '0.');
  if (kind(d) === 'cubicBezier') return d.value;
  return d.value;
};
const ussLines = [], skipped = new Map();
let lastSection = null;
for (const d of decls) {
  const t = transfer(d);
  if (t.to !== 'uss') { if (t.to !== 'none' || group(d) !== 'stand') (skipped.get(t.how) || skipped.set(t.how, []).get(t.how)).push(d.name); continue; }
  if (group(d) !== lastSection) { lastSection = group(d); ussLines.push(`\n    /* ${lastSection} */`); }
  ussLines.push(`    ${d.name}: ${ussValue(d)};${d.note ? ` /* ${d.note} */` : ''}`);
}
const ussText = `/* ФАЙЛ МАШИННЫЙ — производная от visual/kern-ui/css/tokens.css (build-tokens.mjs).
   В игру его переносит генератор tools/generators/Kern.DesignSystem emit-uss-tokens (Assets/Resources/UI/Theme/Tokens/Styles/KernTokens.uss).

   Не переносится переменной USS:
${[...skipped].map(([how, names]) => `   · ${how}:\n       ${names.join(' ')}`).join('\n')}
*/
:root {${ussLines.join('\n')}
}
`;

// ── Запись или сверка ──
// tokens.js — то же, что tokens.json, для страниц, открытых из файла (fetch к file:// браузер не пускает)
const jsText = `// ФАЙЛ МАШИННЫЙ — производная от visual/kern-ui/css/tokens.css (build-tokens.mjs)\nwindow.KERN_TOKENS = ${jsonText.trimEnd()};\n`;
const targets = [[outJson, jsonText], [outUss, ussText], [outJs, jsText]];
if (check) {
  const stale = targets.filter(([p, t]) => !existsSync(p) || readFileSync(p, 'utf8') !== t).map(([p]) => relative(repo, p));
  if (stale.length) { console.log('токены разошлись с kern-ui/css/tokens.css:\n  ' + stale.join('\n  ')); process.exit(1); }
  console.log(`tokens: ok (${decls.length} токенов)`);
} else {
  for (const [p, t] of targets) writeFileSync(p, t);
  const counts = decls.reduce((a, d) => (a[transfer(d).to] = (a[transfer(d).to] || 0) + 1, a), {});
  console.log(`tokens: записано ${decls.length} токенов —`, Object.entries(counts).map(([k, v]) => `${k} ${v}`).join(', '));
}
