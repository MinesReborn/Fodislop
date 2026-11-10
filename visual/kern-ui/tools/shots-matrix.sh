#!/bin/sh
# Визуальная матрица: каждый экран и окно × каждый класс устройства. Ввод следует устройству (тач — телефоны и планшеты,
# геймпад — ТВ и один монитор), язык чередуется ru → en → zh, чтобы каждое сочетание попалось хоть раз.
# Снимки — shots/matrix/<сцена>@<устройство>.png, список — shots/matrix/list.txt; смотреть листами: tools/mosaic.html
# Сервер: python3 -m http.server 8765 из каталога games/. Запуск: sh tools/shots-matrix.sh [фильтр сцены]
cd "$(dirname "$0")/.." || exit 1
OUT=shots/matrix; mkdir -p "$OUT"; : > "$OUT/list.txt"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
SCENES="state=busy state=calm state=busy&chat=1 clan=1 clan=1&clanrole=head mail=1 profile=1 profile=settings menu=1 buildings=1 inv=1 inv=0
settings=graphics settings=audio settings=interface settings=controls map=1 prog=1 prog=debug pause=1 pm=DVM_LEADER event=modal event=lost
event=reconnect event=link srvwin=market srvwin=resp srvwin=gun srvwin=clan_list srvwin=clan_info screen=boot screen=auth screen=auth&auth=register
screen=onboarding screen=menu screen=menu&skills=2 screen=menu&updwin=1 screen=menu&health=1 screen=descent"
DEVICES="d768:mouse d1080:mouse d1440:ps d219:mouse d4k200:mouse tv:xbox promax:touch pixel8:touch ipadmini:touch ipadpro:touch"
i=0
for sc in $SCENES; do
  case "$sc" in *"$1"*) ;; *) continue ;; esac
  for dv in $DEVICES; do
    dev=${dv%%:*}; inp=${dv#*:}
    case $((i % 3)) in 0) lang=ru ;; 1) lang=en ;; 2) lang=zh ;; esac
    i=$((i + 1))
    name="$(echo "$sc" | tr '=&' '-_')@$dev-$inp-$lang"
    "$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=2200,1300 --virtual-time-budget=9000 \
      --screenshot="$PWD/$OUT/$name.png" "http://127.0.0.1:8765/Fodislop/visual/kern-ui/index.html?$sc&device=$dev&input=$inp&lang=$lang&still=1" >/dev/null 2>&1
    echo "$name" >> "$OUT/list.txt"
  done
done
echo "$i снимков → $OUT"
