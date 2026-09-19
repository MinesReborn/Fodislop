---
name: fmod-sync
description: Синхронизация и компиляция звуковых банков FMOD Studio в StreamingAssets
---

# FMOD Studio Bank Build & Audio Backend Pipeline

Навык взаимодействия с аудиосистемой FMOD Studio (`KernAudio`), бинарными банками и CLI-компиляцией.

## 1. Автоматическая компиляция банков через FmodBankBuilder

Редакторский скрипт `FmodBankBuilder.cs` компилирует проект `KernAudio/KernAudio.fspro` в бинарники `.bank`:

* **macOS CLI**: `/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl`
* **Windows CLI**: `C:\Program Files (x86)\FMOD SoundSystem\FMOD Studio\fmodstudiocl.exe`

Вызов компилятора:
```bash
"/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl" build "/path/to/KernAudio/KernAudio.fspro"
```

## 2. Результат сборки и целевые пути

Скомпилированные банки должны быть перенесены в `Assets/StreamingAssets/Audio/`:
- `Master.bank` — топовые шины вывода и лимитеры
- `Master.strings.bank` — таблица GUID и имён FMOD событий
- `SFX.bank` — пространственные 3D-звуки, эмбиенс и UI эффекты

## 3. Обработка ошибок бэкенда
При вызове `AudioSystem.cs`:
* Для смены устройства вывода слушайте системные события FMOD и вызывайте `AudioSystem.Instance.ResetBackend()`.
* Шины вывода: `SFXDefault`, `UIDefault`, `MusicDefault`.
