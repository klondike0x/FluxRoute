# FluxRoute Desktop

<p align="center">
  <b>Language:</b> <a href="#-fluxroute-desktop-ru">🇷🇺 Русский</a> | <a href="#-fluxroute-desktop-en">🇬🇧 English</a>
</p>

---

<a id="-fluxroute-desktop-ru"></a>

## FluxRoute Desktop [RU]

<p align="center">
    <picture>
        <source media="(prefers-color-scheme: dark)" srcset="./assets/FluxRoute-white.svg">
        <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-dark.svg">
        <img width="750" alt="FluxRoute" src="./assets/FluxRoute-dark.svg" />
    </picture>
</p>

<p align="center">
    <a href="https://dotnet.microsoft.com/">
        <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&style=for-the-badge" /></a>
    <a href="https://github.com/klondike0x/FluxRoute/releases">
        <img src="https://img.shields.io/github/downloads/klondike0x/FluxRoute/total?logo=github&label=downloads&style=for-the-badge" /></a>
    <a href="https://github.com/klondike0x/FluxRoute/releases">
        <img src="https://img.shields.io/github/v/release/klondike0x/FluxRoute?include_prereleases&sort=semver&logo=github&label=version&style=for-the-badge" /></a>
    <a href="./LICENSE">
        <img src="https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge" /></a>
</p>

<p align="center">
  <b>Windows GUI для запуска и автоматизации BAT-профилей Flowseal</b><br/>
  Чистый интерфейс, автообновление engine, оркестратор профилей и запуск без ручной возни с BAT-файлами.
</p>

> FluxRoute Desktop — современная GUI-оболочка для управления профилями `Flowseal/zapret-discord-youtube`: удобно запускать, обновлять и переключать профили в одном окне.

---

## ❓ Почему FluxRoute

- **Удобный GUI** вместо ручного запуска BAT-файлов
- **Автообновление `engine/`** из GitHub Releases
- **Оркестратор профилей**, который тестирует соединение и переключает лучший вариант при сбое
- **Скрытый запуск** BAT-файлов и `winws.exe` без лишних консольных окон
- **Диагностика и логи** под рукой, без прыжков между окнами

---

## ✨ Возможности

- **Компактный интерфейс** — одна кнопка Запуск/Стоп, статус и логи всегда на виду
- **Оркестратор** — автоматически тестирует все профили, выставляет рейтинг и переключается на лучший при сбое
- **Автообновление** — при запуске проверяет новые релизы Flowseal на GitHub и обновляет `engine/` в один клик
- **Окно настроек** — выбор профиля, управление оркестратором, сайты для проверки, диагностика
- **Скрытые окна** — BAT-файлы и `winws.exe` запускаются в фоне без лишних консолей

---

## 📸 Скриншоты

| Главное окно | Запущено |
|:---:|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/9bed3707-93ec-4474-b585-d170322a8d76" /> | <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/464705b1-8d2c-4aa7-bd28-6b9c834d34ca" /> |

| Оркестратор | Обновления |
|:---:|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/cd29ba2b-61a3-4837-9c3b-690131cd2940" /> | <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/c5d12051-5810-4b0f-b301-5c2bd9b5c434" /> |

| Сервис |
|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/c239fa73-9cf5-474d-a0bf-e8d649bb62dd" /> |

---

## 🚀 Быстрый старт

### Требования

- **Windows 10/11 x64**
- **Права администратора** для корректной работы `winws.exe`

### Первый запуск

1. Скачай последний релиз в разделе [Releases](https://github.com/klondike0x/FluxRoute/releases)
2. Распакуй ZIP в любую удобную папку
3. Запусти `FluxRoute.exe` **от имени администратора**
4. Открой вкладку **Обновления** и нажми **Проверить** → **Обновить**
5. После загрузки актуального `engine/` выбери профиль и нажми **▶ Запустить**

---

## 🤖 Оркестратор

Оркестратор — это автоматическое управление профилями без ручного перебора.

Как он работает:

1. **Сканирует** доступные профили
2. **Проверяет** доступность выбранных сайтов
3. **Оценивает** каждый профиль по рейтингу от `0` до `100%`
4. **Переключается** на лучший профиль, если текущий перестал работать
5. **Повторно проверяет** соединение через заданный интервал  
   По умолчанию — **каждые 20 минут**

Это позволяет держать рабочий профиль активным почти без ручного вмешательства.

---

## 📁 Структура проекта

```
FluxRoute/
├── FluxRoute/           — UI (WPF, Views, ViewModels)
├── FluxRoute.Core/      — Логика (Оркестратор, Проверка связи, Модели)
├── FluxRoute.Updater/   — Автообновление engine/ с GitHub
└── engine/              — Скрипты Flowseal (скачиваются автоматически)
```

---

## 🛠️ Сборка из исходников

**Требования:**
- .NET 10 SDK
- Visual Studio 2026

```bash
git clone https://github.com/klondike0x/FluxRoute.git
cd FluxRoute
dotnet build
```

---

## ⚠️ Дисклеймер

FluxRoute Desktop является **GUI-оболочкой** для проекта [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).

Все права на `zapret`, `winws.exe` и связанные с ними скрипты принадлежат их авторам.  
Этот репозиторий не претендует на авторство оригинальной низкоуровневой сетевой части.

---

## 🐞 Нашёл баг?

Если что-то работает не так, открой [Issue](https://github.com/klondike0x/FluxRoute/issues) и по возможности укажи:

- что произошло;
- что ты ожидал увидеть;
- как это воспроизвести;
- какой профиль был выбран;
- что написано в логах или диагностике.

Чем точнее описание, тем быстрее получится разобраться.

---

## 🧩 Основа engine

FluxRoute использует следующую экосистему проектов:

- [**WinDivert**](https://github.com/basil00/WinDivert) — низкоуровневая Windows-основа
- [**bol-van/zapret**](https://github.com/bol-van/zapret) — оригинальный проект
- [**bol-van/zapret-win-bundle**](https://github.com/bol-van/zapret-win-bundle) — Windows-бандл с `winws.exe`
- [**Flowseal/zapret-discord-youtube**](https://github.com/Flowseal/zapret-discord-youtube) — непосредственная основа `engine/`, используемая в FluxRoute

---

## 💡 Вдохновение

Проекты, которые вдохновили на создание FluxRoute Desktop:

- [**Zapret-GUI**](https://github.com/medvedeff-true/Zapret-GUI) — от `medvedeff-true`
- [**ZapretControl**](https://github.com/Virenbar/ZapretControl) — от `Virenbar`
- [**zapret**](https://github.com/youtubediscord/zapret) — от `youtubediscord`

---

## 📄 Лицензия

Проект распространяется по лицензии **GNU General Public License v3.0**.  
Подробности — в файле [LICENSE](./LICENSE).

---

<a id="-fluxroute-desktop-en"></a>

## FluxRoute Desktop [EN]

<p align="center">
    <picture>
        <source media="(prefers-color-scheme: dark)" srcset="./assets/FluxRoute-white.svg">
        <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-dark.svg">
        <img width="750" alt="FluxRoute" src="./assets/FluxRoute-dark.svg" />
    </picture>
</p>

<p align="center">
    <a href="https://dotnet.microsoft.com/">
        <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&style=for-the-badge" /></a>
    <a href="https://github.com/klondike0x/FluxRoute/releases">
        <img src="https://img.shields.io/github/downloads/klondike0x/FluxRoute/total?logo=github&label=downloads&style=for-the-badge" /></a>
    <a href="https://github.com/klondike0x/FluxRoute/releases">
        <img src="https://img.shields.io/github/v/release/klondike0x/FluxRoute?include_prereleases&sort=semver&logo=github&label=version&style=for-the-badge" /></a>
    <a href="./LICENSE">
        <img src="https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge" /></a>
</p>

<p align="center">
  <b>Windows GUI for launching and automating Flowseal BAT profiles</b><br/>
  Clean interface, automatic engine updates, profile orchestrator, and hassle-free launching without manually handling BAT files.
</p>

> FluxRoute Desktop is a modern GUI wrapper for managing `Flowseal/zapret-discord-youtube` profiles: launch, update, and switch profiles conveniently from a single window.

---

## ❓ Why FluxRoute

- **Convenient GUI** instead of manually launching BAT files
- **Automatic `engine/` updates** directly from GitHub Releases
- **Profile orchestrator** that tests connectivity and switches to the best working option if the current one fails
- **Hidden launch** of BAT files and `winws.exe` without extra console windows
- **Diagnostics and logs** always available without jumping between windows

---

## ✨ Features

- **Compact interface** — a single Start/Stop button, status, and logs always in view
- **Orchestrator** — automatically tests all profiles, assigns a rating, and switches to the best one when needed
- **Auto-update** — checks new Flowseal releases on GitHub and updates `engine/` in one click
- **Settings window** — profile selection, orchestrator control, test websites, and diagnostics
- **Hidden windows** — BAT files and `winws.exe` run in the background without unnecessary consoles

---

## 📸 Screenshots

| Main Window | Running |
|:---:|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/9bed3707-93ec-4474-b585-d170322a8d76" /> | <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/464705b1-8d2c-4aa7-bd28-6b9c834d34ca" /> |

| Orchestrator | Updates |
|:---:|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/cd29ba2b-61a3-4837-9c3b-690131cd2940" /> | <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/c5d12051-5810-4b0f-b301-5c2bd9b5c434" /> |

| Service |
|:---:|
| <img width="760" height="530" alt="изображение" src="https://github.com/user-attachments/assets/c239fa73-9cf5-474d-a0bf-e8d649bb62dd" /> |

---

## 🚀 Quick Start

### Requirements

- **Windows 10/11 x64**
- **Administrator privileges** required for proper `winws.exe` operation

### First Launch

1. Download the latest release from the [Releases](https://github.com/klondike0x/FluxRoute/releases) section
2. Extract the ZIP archive to any convenient folder
3. Run `FluxRoute.exe` **as Administrator**
4. Open the **Updates** tab and click **Check** → **Update**
5. After the latest `engine/` is downloaded, choose a profile and click **▶ Start**

---

## 🤖 Orchestrator

The orchestrator is an automatic profile management system that removes the need for manual switching.

How it works:

1. **Scans** available profiles
2. **Checks** the availability of selected websites
3. **Scores** each profile with a rating from `0` to `100%`
4. **Switches** to the best profile if the current one stops working
5. **Re-checks** connectivity at a specified interval  
   By default — **every 20 minutes**

This helps keep a working profile active with minimal manual intervention.

---

## 📁 Project Structure

```text
FluxRoute/
├── FluxRoute/           — UI (WPF, Views, ViewModels)
├── FluxRoute.Core/      — Logic (Orchestrator, connectivity checks, models)
├── FluxRoute.Updater/   — Automatic engine updates from GitHub
└── engine/              — Flowseal scripts (downloaded automatically)
```

---

## 🛠️ Build from Source

**Requirements:**
- .NET 10 SDK
- Visual Studio 2026

```bash
git clone https://github.com/klondike0x/FluxRoute.git
cd FluxRoute
dotnet build
```

---

## ⚠️ Disclaimer

FluxRoute Desktop is a **GUI wrapper** for the [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) project.

All rights to `zapret`, `winws.exe`, and related scripts belong to their respective authors.  
This repository does not claim authorship of the original low-level networking components.

---

## 🐞 Found a Bug?

If something is not working as expected, open an [Issue](https://github.com/klondike0x/FluxRoute/issues) and, if possible, include:

- what happened;
- what you expected to happen;
- how to reproduce it;
- which profile was selected;
- what the logs or diagnostics say.

The more accurate the report, the easier it will be to investigate.

---

## 🧩 Engine Base

FluxRoute uses the following project ecosystem:

- [**WinDivert**](https://github.com/basil00/WinDivert) — low-level Windows foundation
- [**bol-van/zapret**](https://github.com/bol-van/zapret) — original project
- [**bol-van/zapret-win-bundle**](https://github.com/bol-van/zapret-win-bundle) — Windows bundle with `winws.exe`
- [**Flowseal/zapret-discord-youtube**](https://github.com/Flowseal/zapret-discord-youtube) — the direct `engine/` base used in FluxRoute

---

## 💡 Inspiration

Projects that inspired the creation of FluxRoute Desktop:

- [**Zapret-GUI**](https://github.com/medvedeff-true/Zapret-GUI) — by `medvedeff-true`
- [**ZapretControl**](https://github.com/Virenbar/ZapretControl) — by `Virenbar`
- [**zapret**](https://github.com/youtubediscord/zapret) — by `youtubediscord`

---

## 📄 License

This project is distributed under the **GNU General Public License v3.0**.  
See the [LICENSE](./LICENSE) file for details.
