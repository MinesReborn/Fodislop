# Kern

2D-клиент для [Kern](https://github.com/MinesReborn) — реворк клиента давно почившей MMORPG Сергея Мячина.

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/Kern.git
```

Открой через **Unity Hub** → `Open` → выбери папку. Unity сам подтянет зависимости. Открой `Assets/Scenes/Bootstrap.unity` и жми **Play**: Bootstrap (build index 0) грузит `MainMenu`, а тот — `MainGame` аддитивно.
реальное подключение через Darkar25 `TcpConnection` (MinesServerNetworking) к `ServerHost:ServerPort` (по умолчанию `127.0.0.1:7777`).

## Технологии

**Unity 6** (6000.6.0f1), URP 2D, UI Toolkit, FMOD Studio, UniTask, Effekseer.  
Сеть: Git-пакеты [MinesServerNetworking](https://github.com/MinesReborn/MinesServerNetworking).  

Подробнее для разработчиков — в [**`AGENTS.md`**](AGENTS.md).

## Лицензия

[MIT](LICENSE)
