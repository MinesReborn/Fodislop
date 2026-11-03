# Kern UI — новый проход, макет на согласование

Только макет: в Unity ничего не переносится. Источники и решения по каждому
элементу — [inventory.md](inventory.md). Старые `kern-ui-lab/proposal/` и
`.superdesign/` не используются.

## Как открыть

Страницы берут ассеты из `Fodislop/Assets` и `OpenMines/client/Assets`,
поэтому сервер поднимается из общего каталога `games/`:

```sh
cd ~/Projects/games && python3 -m http.server 8765 --bind 127.0.0.1
# http://127.0.0.1:8765/Fodislop/visual/kern-ui-rebuild/game.html
```

- `game.html?state=` `default` · `busy` (миссия, геология, перегруз
  128 %, строки состояния, запущенная программа, подсказка предмета) · `chat` · `chatlist` · `skills` ·
  `skills-selected` · `skills-empty-slot`
- `menu.html?state=` `bootstrap` · `auth` · `onboarding` · `menu` · `update` ·
  `loading`
- `&device=` `se` · `pixel8` · `promax` · `ipadmini` · `ipadpro` — тач-вариант
  (альбом, 1 px = 1 dp, безопасная зона из `MobileDeviceMatrix`); окно задать
  размером устройства в dp.
- `&overlay=hud|chat|window` — поверх макета прямоугольники Legacy, решённые
  из якорей `m1client.unity` (`data/legacy-rects.json`) без участия CSS
  макета. У окна навыков дочерние узлы двигает LayoutGroup — для него
  сверяются только ширина и центр окна.

Снимки: `tools/shoot.sh <страница> <состояние> <ШxВ> [overlay]`,
`DEVICE=se tools/shoot.sh …`; результат — `shots/`.

## Что проверено

Десктоп: 1440×900, 1280×720, 1920×1080, 2560×1080, 1024×768.
Тач: iPhone SE 3 655×368, Pixel 8 897×404, iPhone 15 Pro Max 972×449,
iPad mini 6 1112×730, iPad Pro 13 1668×1251 dp.
Сверка HUD с Legacy — наложением `overlay=hud/chat`: миникарта, программатор,
автокопание, кнопка чата, мини-чат, ряд кнопок, онлайн, FPS, инвентарь,
мини-навыки совпадают с прямоугольниками сцены.

Свободный текст сервера (миссия, строки состояния, подсказка предмета, имя
программы, заголовок окна навыков, описание обновления) показан пунктирными
местами — текст не выдуман.

Не проверено: вид Unity-клиента Kern (рендер не запускался — сверка Kern-стороны
по UXML/USS/коду) и сам Legacy в работе (только сцена и код).
