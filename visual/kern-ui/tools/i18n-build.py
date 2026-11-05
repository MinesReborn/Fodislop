# Словарь языков макета: js/i18n-data.js. Руками не правится.
# Источники: словарь игры Assets/Resources/Localization/Locales/{ru,en,zh}.json — главный;
# i18n/mockup.json — строки макета, которых в игре нет. Из словаря игры берутся только строки,
# которые макет показывает: их собирает tools/i18n-collect.html в i18n/strings.json, плюс русский текст из js/*.js.
# Запуск из корня репозитория: python3 -I visual/kern-ui/tools/i18n-build.py
import json, pathlib, re

root = pathlib.Path(__file__).resolve().parents[3]
ui = root / 'visual/kern-ui'
loc = root / 'Assets/Resources/Localization/Locales'
ru, en, zh = (json.loads((loc / f'{l}.json').read_text('utf-8')) for l in ('ru', 'en', 'zh'))
norm = lambda s: re.sub(r'\s+', ' ', s).strip()
digits = lambda s: re.sub(r'\d+', '#', s)

# Что макет показывает: собранные строки (числа уже «#») и русские куски строк из кода
shown = set(json.loads((ui / 'i18n/strings.json').read_text('utf-8')))
for f in (ui / 'js').glob('*.js'):
    if f.name.startswith(('prog-data', 'i18n-data')):
        continue
    src = re.sub(r'//[^\n]*', '', f.read_text('utf-8'))
    for m in re.finditer(r"'([^'\n]*[А-Яа-яЁё][^'\n]*)'|`([^`]*[А-Яа-яЁё][^`]*)`", src):
        for part in re.split(r'\$\{[^}]*\}|<[^>]+>', m.group(1) or m.group(2)):
            if re.search('[А-Яа-яЁё]', part) and not re.search(r'=>|;|\$\(|\{|\}|\.join|document\.', part) and len(part) < 160:
                shown.add(digits(norm(part)))

def pattern(text):
    """Регулярное выражение строки словаря: {n} — любое значение, числа — любые числа."""
    out = ''
    for tok in re.split(r'(\{\d+\})', text):
        out += '(.+?)' if re.fullmatch(r'\{\d+\}', tok) else re.escape(digits(tok)).replace('\\#', '#').replace('#', '(?:#|\\d+)')
    return re.compile('^' + out + '$')

# Одна русская строка бывает у нескольких ключей с разным смыслом («Интерфейс» — вкладка и громкость):
# сначала вкладки и разделы настроек, команды программатора — по ключу (см. keys ниже)
RANK = ('pause.tab.', 'settings.', 'menu.', 'gateway.', 'mainmenu.', 'common.')
rank = lambda k: next((i for i, p in enumerate(RANK) if k.startswith(p)), len(RANK) + (k.startswith('programmator.') * 2))
exact, tpl, used_game = {}, [], 0
for key, v in sorted(ru.items(), key=lambda kv: rank(kv[0])):
    if not isinstance(v, str) or not isinstance(en.get(key), str) or not isinstance(zh.get(key), str):
        continue
    v = norm(v)
    if not re.search('[А-Яа-яЁё]', v):
        continue
    if '{' in v:
        rx = pattern(v)
        if any(rx.match(s) for s in shown):
            tpl.append([v, en[key], zh[key]]); used_game += 1
    elif (digits(v) in shown or key.startswith('programmator.')) and v not in exact:   # команды программатора — все: палитра рисует раздел по выбору
        exact[v] = [en[key], zh[key]]; used_game += 1

# Команды программатора — по ключу: «Звук» в палитре — команда, а не раздел настроек
keys = {k: [en[k], zh[k]] for k in ru if k.startswith(('programmator.op.', 'programmator.cat.')) and isinstance(en.get(k), str) and isinstance(zh.get(k), str)}

mock = json.loads((ui / 'i18n/mockup.json').read_text('utf-8'))
mock.pop('_', None)
dupes = []
for k, (e, z) in mock.items():
    k = norm(k)
    if k in exact or any(t[0] == k for t in tpl):
        dupes.append(k)                       # есть в словаре игры — берётся оттуда
    elif '{' in k:
        tpl.append([k, e, z])
    else:
        exact[k] = [e, z]

# Строки, которые макет показывает, а перевода нет ни там, ни там
rx_all = [pattern(t[0]) for t in tpl]
missing = sorted(s for s in shown if s not in {digits(k) for k in exact} and not any(r.match(s) for r in rx_all))
(ui / 'i18n/untranslated.json').write_text(json.dumps(missing, ensure_ascii=False, indent=0) + '\n', 'utf-8')
(ui / 'js/i18n-data.js').write_text('// Создано tools/i18n-build.py из словаря игры и i18n/mockup.json — не править руками\nwindow.I18N = '
                                    + json.dumps({'exact': exact, 'tpl': tpl, 'keys': keys}, ensure_ascii=False, separators=(',', ':')) + ';\n', 'utf-8')
print(f'из словаря игры {used_game}, из макета {len(mock) - len(dupes)}, совпало с игрой {len(dupes)}, без перевода {len(missing)}')
for d in dupes:
    print('  в игре уже есть:', d)
