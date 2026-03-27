# FluxRoute Desktop

<p align="center">
    <picture>
        <img width="750" alt="FluxRoute" src="https://github.com/user-attachments/assets/57b5f5a8-a36e-4fe0-805d-6a2c77955368" />
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

> GUI-оболочка для управления скриптами [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — красиво, быстро и без ручного запуска BAT-файлов.

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



### Требования
- Windows 10/11 x64
- Права администратора (нужны для `winws.exe`)

### Первый запуск

1. Скачай последний релиз: [Releases](https://github.com/klondike0x/FluxRoute/releases)
2. Распакуй ZIP в любую папку
3. Запусти `FluxRoute.exe` **от имени администратора**
4. Перейди на вкладку **Обновления** → нажми **Проверить** → **Обновить**
   - Это скачает актуальную версию Flowseal zapret в папку `engine/`
5. Выбери профиль и нажми **▶ Запустить**

---

## 🤖 Оркестратор

Оркестратор — умная система автоматического управления профилями:

1. **Сканирует** все профили и проверяет доступность YouTube, Discord, Google, Twitch, Instagram
2. **Выставляет рейтинг** — каждый профиль получает оценку от 0 до 100%
3. **Автоматически переключается** на лучший профиль если текущий перестал работать
4. **Проверяет** соединение с заданным интервалом (по умолчанию каждые 20 минут)

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

## 🛠 Сборка из исходников

**Требования:** .NET 10 SDK, Visual Studio 2022

```bash
git clone https://github.com/klondike0x/FluxRoute.git
cd FluxRoute
dotnet build
```

---

## ⚠️ Дисклеймер

Программа является GUI-оболочкой для проекта [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).
Все права на `zapret` и скрипты Flowseal принадлежат их авторам.

---

## 🐛 Нашёл баг?

Если что-то работает не так — открой [Issue](https://github.com/klondike0x/FluxRoute/issues) и опиши:
- Что происходит
- Что ожидал увидеть
- Шаги для воспроизведения

---

## Основа engine

FluxRoute использует экосистему проектов в такой цепочке:

- [WinDivert](https://github.com/basil00/WinDivert) — низкоуровневая основа для Windows
- [bol-van/zapret](https://github.com/bol-van/zapret) — оригинальный проект
- [bol-van/zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle) — Windows-бандл с winws.exe
- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — непосредственная основа engine, используемая в FluxRoute

## Вдохновение

- [medvedeff-true/Zapret-GUI](https://github.com/medvedeff-true/Zapret-GUI)
- [Virenbar/ZapretControl](https://github.com/Virenbar/ZapretControl)
- [youtubediscord/zapret](https://github.com/youtubediscord/zapret)

---

## 📄 Лицензия

GNU General Public License v3.0 — см. [LICENSE](LICENSE)
