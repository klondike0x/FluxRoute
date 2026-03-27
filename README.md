# FluxRoute Desktop

<p align="center">
  <b>Language:</b> <a href="#-fluxroute-desktop-ru">🇷🇺 Русский</a> | <a href="#-fluxroute-desktop-en">🇬🇧 English</a>
</p>

---

<a id="-fluxroute-desktop-ru"></a>

## FluxRoute Desktop [RU]

<p align="center">
    <picture>
        <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-dark.svg">
        <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-white.svg">
        <img width="750" alt="FluxRoute" src="./assets/FluxRoute-white.svg" />
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
| <img width="420" height="520" alt="изображение" src="https://github.com/user-attachments/assets/9d85d881-80dc-4cbc-9c2f-413500d48222" /> | <img width="420" height="520" alt="изображение" src="https://github.com/user-attachments/assets/e129b222-0bbd-4dad-8a10-d0cfb12c0d1c" /> |


| Оркестратор | Обновления |
|:---:|:---:|
| <img width="520" height="600" alt="изображение" src="https://github.com/user-attachments/assets/245ae590-2f2f-43db-a45a-9d55e68b8488" /> | <img width="520" height="600" alt="изображение" src="https://github.com/user-attachments/assets/bfd6dca8-08b4-4f7e-9cb1-547a36759335" /> |

| Сервис |
|:---:|
| <img width="520" height="600" alt="изображение" src="https://github.com/user-attachments/assets/800a256c-5e88-4aa5-93a0-94e8870aee5d" /> |

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
