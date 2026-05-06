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
  ⚡ Автоматизация zapret-профилей с умным переключением<br/><br/>
  <b>FluxRoute Desktop — Windows GUI и оркестратор для Flowseal zapret-discord-youtube</b><br/>
  Управляет BAT-профилями через удобный интерфейс и автоматически подбирает рабочие конфигурации.
</p>

> FluxRoute Desktop — GUI-оболочка для Flowseal `zapret-discord-youtube`: запуск и обновление профилей в одном окне.

---

## ⭐ Поддержать проект

Если FluxRoute оказался полезным, пожалуйста, поддержи проект:

- поставь **Star** на GitHub;
- расскажи о проекте друзьям или в тематических чатах;
- открой **Issue** с идеями и обратной связью;
- предложи улучшения через **Pull Request**.

Твоя поддержка помогает развивать FluxRoute быстрее и делать его лучше для всех 💙

---

## ❓ Почему FluxRoute

- **Удобный GUI** вместо ручного запуска BAT-файлов
- **Автообновление `engine/`** из GitHub Releases
- **Оркестратор профилей** — автоматически тестирует соединение и переключает лучший вариант при сбое
- **Скрытый запуск** BAT-файлов и `winws.exe` без лишних консольных окон
- **Диагностика и логи** под рукой, без прыжков между окнами

---

## ✨ Возможности

- **Компактный интерфейс** — одна кнопка Запуск/Стоп, статус и логи всегда на виду
- **Оркестратор** — автоматически тестирует все профили, выставляет рейтинг и переключается на лучший при сбоях (подробнее см. раздел **Оркестратор** ниже)
- **TG WS Proxy (новое в 1.4.0)** — дополнительный прокси-канал, интегрированный в общий сценарий запуска
- **Автообновление** — при запуске проверяет новые релизы Flowseal на GitHub и обновляет `engine/` в один клик
- **Окно настроек** — выбор профиля, управление оркестратором, сайты для проверки, диагностика
- **Скрытые окна** — BAT-файлы и `winws.exe` запускаются в фоне без лишних консолей

---

## 🤖 Оркестратор

Оркестратор — это автоматическое управление профилями без ручного перебора.

В версии **1.4.0** он также работает в связке с **TG WS Proxy** как дополнительным прокси-каналом (при использовании соответствующего сценария).

Как он работает:

1. **Сканирует** доступные профили
2. **Проверяет** доступность выбранных сайтов
3. **Оценивает** каждый профиль по рейтингу от `0` до `100%`
4. **Переключается** на лучший профиль, если текущий перестал работать
5. **Повторно проверяет** соединение через заданный интервал  
   По умолчанию — **каждые 20 минут**

Это позволяет держать рабочий профиль активным почти без ручного вмешательства.

---

## 📸 Скриншоты

| Главное окно | Запущено |
|:---:|:---:|
| <img width="760" height="530" alt="FluxRoute_2TjfigppDJ" src="https://github.com/user-attachments/assets/2766d05f-e455-46c7-9517-1c284625d12c" /> | <img width="760" height="530" alt="FluxRoute_PtjD6uwyNa" src="https://github.com/user-attachments/assets/7e9971c5-049e-4409-9400-bd27ad97f032" /> |

| Оркестратор | Обновления |
|:---:|:---:|
| <img width="760" height="530" alt="FluxRoute_ydu1FOkwjG" src="https://github.com/user-attachments/assets/65e23cc4-bbd2-4134-87be-d161f86715b8" /> | <img width="760" height="530" alt="FluxRoute_tEVm5fefzY" src="https://github.com/user-attachments/assets/f3cac7f0-3f8a-4615-8264-75c9c02a7a7a" /> |

| Сервис |
|:---:|
| <img width="760" height="530" alt="FluxRoute_qTDTBLMl7M" src="https://github.com/user-attachments/assets/73af680b-d21c-467e-b550-ca38f9ee5a87" /> |

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
  ⚡ Smart automation for zapret profile switching<br/><br/>
  <b>FluxRoute Desktop — Windows GUI and orchestrator for Flowseal zapret-discord-youtube</b><br/>
  Manages BAT profiles through a clean interface and automatically selects working configurations.
</p>

> FluxRoute Desktop is a GUI wrapper for Flowseal `zapret-discord-youtube`, allowing users to launch and update profiles from a single window.

---

## ⭐ Support the Project

If FluxRoute is useful for you, please support the project:

- leave a **Star** on GitHub;
- share the project with friends or community chats;
- open an **Issue** with feedback and ideas;
- contribute improvements via **Pull Request**.

Your support helps FluxRoute grow faster and become better for everyone 💙

---

## ❓ Why FluxRoute

- **Convenient GUI** instead of manually launching BAT files
- **Automatic `engine/` updates** directly from GitHub Releases
- **Profile orchestrator** — automatically tests connectivity and switches to the best option on failure
- **Hidden launch** of BAT files and `winws.exe` without extra console windows
- **Diagnostics and logs** always available without jumping between windows

---

## ✨ Features

- **Compact interface** — a single Start/Stop button, status, and logs always in view
- **Orchestrator** — automatically tests all profiles, assigns ratings, and switches to the best one on failures (see the **Orchestrator** section below for details)
- **TG WS Proxy (new in 1.4.0)** — an additional proxy channel integrated into the overall launch workflow
- **Auto-update** — checks new Flowseal releases on GitHub and updates `engine/` in one click
- **Settings window** — profile selection, orchestrator control, test websites, and diagnostics
- **Hidden windows** — BAT files and `winws.exe` run in the background without unnecessary consoles

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

## 📸 Screenshots

| Main Window | Running |
|:---:|:---:|
| <img width="760" height="530" alt="FluxRoute_2TjfigppDJ" src="https://github.com/user-attachments/assets/2766d05f-e455-46c7-9517-1c284625d12c" /> | <img width="760" height="530" alt="FluxRoute_PtjD6uwyNa" src="https://github.com/user-attachments/assets/7e9971c5-049e-4409-9400-bd27ad97f032" /> |

| Orchestrator | Updates |
|:---:|:---:|
| <img width="760" height="530" alt="FluxRoute_ydu1FOkwjG" src="https://github.com/user-attachments/assets/65e23cc4-bbd2-4134-87be-d161f86715b8" /> | <img width="760" height="530" alt="FluxRoute_tEVm5fefzY" src="https://github.com/user-attachments/assets/f3cac7f0-3f8a-4615-8264-75c9c02a7a7a" /> |

| Service |
|:---:|
| <img width="760" height="530" alt="FluxRoute_qTDTBLMl7M" src="https://github.com/user-attachments/assets/73af680b-d21c-467e-b550-ca38f9ee5a87" /> |

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
