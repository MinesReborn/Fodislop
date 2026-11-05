// Данные страницы загрузки. Скрипт, а не JSON: страница работает и открытая из файла (fetch к file:// браузер не пускает).
// Внутри — чистый JSON после «=»: его читают инструменты (export/tools/export.mjs).
window.KERN_RELEASE = {
  "$comment": "Данные страницы загрузки. Заполняет выпуск: размер и SHA-256 — по собранному файлу, null — показывается как «—». Требования — из ProjectSettings (Android minSdk 26 / ARM64, iOS 16.0, macOS 12.0) и требований Unity 6 к Windows и Linux.",
  "version": "0.9.0",
  "date": null,
  "notes": null,
  "platforms": [
    {
      "id": "windows",
      "name": "Windows",
      "kind": "Установщик",
      "file": "ProjectKern-0.9.0-windows-x64.exe",
      "size": null,
      "sha256": null,
      "requires": "Windows 10 и новее, 64 бит",
      "url": null
    },
    {
      "id": "macos",
      "name": "macOS",
      "kind": "Образ диска",
      "file": "ProjectKern-0.9.0-macos.dmg",
      "size": null,
      "sha256": null,
      "requires": "macOS 12 и новее",
      "url": null
    },
    {
      "id": "linux",
      "name": "Linux",
      "kind": "Архив",
      "file": "ProjectKern-0.9.0-linux-x86_64.tar.gz",
      "size": null,
      "sha256": null,
      "requires": "Ubuntu 22.04 и новее или совместимый, 64 бит",
      "url": null
    },
    {
      "id": "android",
      "name": "Android",
      "kind": "APK",
      "file": "ProjectKern-0.9.0-android-arm64.apk",
      "size": null,
      "sha256": null,
      "requires": "Android 8.0 и новее, ARM64",
      "url": null,
      "store": "Google Play"
    },
    {
      "id": "ios",
      "name": "iOS и iPadOS",
      "kind": "App Store",
      "file": null,
      "size": null,
      "sha256": null,
      "requires": "iOS 16 и новее",
      "url": null,
      "store": "App Store"
    }
  ]
};
