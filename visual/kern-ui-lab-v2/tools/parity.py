# Сверка «ничего не потеряно»: оригинал kern-ui-lab против v2.
# Запуск: python3 -I tools/parity.py ../kern-ui-lab/index.html index.html
import re, sys
from html.parser import HTMLParser

class P(HTMLParser):
    def __init__(s):
        super().__init__(); s.ids=set(); s.i18n=set(); s.calls=set(); s.modals=set(); s.views=set()
    def handle_starttag(s, tag, attrs):
        a=dict(attrs)
        if 'id' in a: s.ids.add(a['id'])
        if 'data-i18n' in a: s.i18n.add(a['data-i18n'])
        for k,v in a.items():
            if k.startswith('on') and v:
                s.calls.update(re.findall(r'([A-Za-z_][\w.]*)\s*\(', v))
        c=a.get('class','') or ''
        if 'modal-overlay' in c.split(): s.modals.add(a.get('id'))
        if re.search(r'-view\b|ingame-viewport', c) and 'id' in a: s.views.add(a['id'])

def load(p):
    x=P(); x.feed(open(p, encoding='utf-8').read()); return x
o, n = load(sys.argv[1]), load(sys.argv[2])
# Намеренно убрано по заданию (UI_REDESIGN_HANDOFF: планета, профиль в шапке,
# отдельная кнопка/баннер обновления) и прежний HUD, заменённый новым.
INTENDED = {
  'planetSystem': 'планета — убрана по заданию',
  'updateAlertBanner': 'баннер обновления — обновление заменяет кнопку спуска',
}
# Куда ушли подписи прежнего HUD (ключи i18n): элемент сохранён в новом виде.
MOVED = {
  'hud.hud_miner_nick': 'ник — #hudMinerNick (данные, не перевод)',
  'hud.sci_fi_badge_mono': 'уровень — #hudLevel «ур.N»',
  'hud.hud_clan_btn': 'клан — кнопка .g-clan → clanModal',
  'hud.hud_bar_header': 'прочность — полоса #hpFill + «NHP» #hpText',
  'hud.hud_bar_header_2': 'РЕАКТОР/EN — в игре нет такого показателя (нет в Kern и Legacy) — снят',
  'hud.hud_bar_header_3': 'РЕАКТОР/EN — то же',
  'hud.hud_title_row': 'корзина — панель «Груз N%» #hudCargo',
  'hud.eyebrow_tag': 'миссия — #hudMission (текст от сервера)', 'hud.hud_title_row_2': 'награда миссии — текст сервера в #hudMission',
  'hud.sci_fi_panel': 'текст миссии — место текста сервера', 'hud.sci_fi_panel_2': 'подпись прогресса — #missionMetric', 'hud.mission_metric': 'прогресс — #missionMetric «N / M»',
  'mock.hud.minimap': 'миникарта — .g-minimap', 'hud.hud_title_row_4': 'глубина на миникарте → координаты x:y #hudCoords',
  'hud.hud_action_label': 'автокопка — #btnAutoDig «Автокопание»',
  'hud.hud_action_label_3': 'грузовой отсек — хотбар #hotbarSlotsWrap, TAB → inventoryModal', 'hud.hud_action_key': 'подсказка TAB — клавиша работает',
  'hud.chat': 'чат — колонка/мини-чат, кнопка .g-chat-toggle', 'hud.hud_action_key_2': 'подсказка ENTER — Enter открывает колонку чата',
  'hud.hud_action_label_4': 'торговец — traderModal с дев-панели', 'hud.hud_action_label_5': 'бонус шахты — кнопка «Бонусы»',
  'mock.hud.chat': 'превью чата — мини-чат #chatDownLines', 'mock.hud.chat_line_1': 'строки чата — #chatDownLines', 'mock.hud.chat_line_2': 'строки чата — #chatDownLines', 'mock.hud.chat_open': 'открыть чат — клик по мини-чату / кнопка',
  'mock.touch.auto': 'тач АВТО — флаг «Автокопание»', 'mock.touch.inventory': 'тач СУМКА — хотбар', 'mock.touch.heal': 'тач ЛЕЧИТЬ — «Лечение» в доке',
  'mock.touch.dig': 'тач КОПАТЬ — «Копать» в доке', 'mock.touch.chat': 'тач ЧАТ — кнопка чата в системном ряду', 'mock.touch.menu': 'тач МЕНЮ — ☰ в системном ряду',
  'common.beacon_label': 'маяк — часть планеты (убрана по заданию)', 'mainmenu.sector': 'метка сектора — часть планеты', 'mainmenu.coords': 'координаты планеты — часть планеты',
  'mainmenu.user_name': 'плашка профиля в шапке — убрана; profileModal с дев-панели', 'mainmenu.user_rank': 'то же',
  'mainmenu.tooltip.update': 'боковая кнопка обновления — обновление на месте кнопки спуска', 'mainmenu.update_banner_title': 'баннер обновления — то же',
}
for i in range(1, 9): MOVED['hud.basket_slot_mini' + ('' if i == 1 else f'_{i}')] = 'ячейки корзины — строки кристаллов #basketMiniGrid'
INTENDED.update(MOVED)
ok = True
for name, a, b in [('id', o.ids, n.ids), ('data-i18n', o.i18n, n.i18n), ('обработчики', o.calls, n.calls), ('окна', o.modals, n.modals), ('экраны', o.views, n.views)]:
    lost = sorted(a - b); added = sorted(b - a)
    print(f'\n== {name}: оригинал {len(a)}, v2 {len(b)}, пропало {len(lost)}, добавлено {len(added)}')
    for x in lost:
        why = INTENDED.get(x)
        print(f'  - {x}' + (f'   → {why}' if why else '   [ПОТЕРЯ]'))
        ok &= bool(why)
    if added: print('  + ' + ', '.join(added))
print('\nИТОГ:', 'потерь нет (кроме намеренных)' if ok else 'ЕСТЬ ПОТЕРИ')
