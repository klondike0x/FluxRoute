<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./assets/FluxRoute-white.svg">
  <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-dark.svg">
  <img width="650" alt="FluxRoute AI Logo" src="./assets/FluxRoute-dark.svg" />
</picture>

# FluxRoute AI `v1.6.2`

### ⚡ Интеллектуальный швейцарский нож для обхода DPI на Windows

**Самообучающаяся система, объединяющая Zapret, ByeDPI и Cloudflare Warp в единый адаптивный механизм.**

[🇬🇧 English Version](README.en.md) | [📥 Скачать релиз](https://github.com/mx57/FluxRoute_AI/releases) | [🐛 Сообщить о проблеме](https://github.com/mx57/FluxRoute_AI/issues)

---

[![Stars](https://img.shields.io/github/stars/mx57/FluxRoute_AI?style=for-the-badge&logo=github&color=FFD700)](https://github.com/mx57/FluxRoute_AI)
[![Releases](https://img.shields.io/github/v/release/mx57/FluxRoute_AI?include_prereleases&sort=semver&logo=github&label=версия&style=for-the-badge&color=3FB950)](https://github.com/mx57/FluxRoute_AI/releases)
[![Downloads](https://img.shields.io/github/downloads/mx57/FluxRoute_AI/total?logo=github&label=загрузки&style=for-the-badge&color=4FC3F7)](https://github.com/mx57/FluxRoute_AI/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&style=for-the-badge)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/Лицензия-GPLv3-blue.svg?style=for-the-badge)](./LICENSE)

</div>

---

## 🌟 Почему FluxRoute AI?

Обычные GUI для DPI-инструментов просто запускают процессы. **FluxRoute AI** идёт дальше — он **думает**. Блокировки постоянно меняются, и то, что работало вчера, может перестать работать сегодня. Наша система использует математические модели для автоматической адаптации к вашей сети.

### 🧠 Искусственный Интеллект (Thompson Sampling)
Вместо перебора профилей вручную, ИИ-оркестратор анализирует успешность каждой попытки соединения. Он использует алгоритм **Многоруких бандитов (Multi-armed bandits)** для баланса между:
- **Exploitation:** Использование самой надежной стратегии на данный момент.
- **Exploration:** Периодическая проверка новых или менее изученных профилей, которые могут работать лучше.

### 🧬 Генетическая Эволюция (StrategyEvolver)
Система буквально "выращивает" новые BAT-файлы. Она скрещивает параметры самых успешных стратегий, применяет случайные мутации к параметрам desync, split и fake-tls, и проверяет результат. Выживают только лучшие.
> *К v1.6.2 ИИ освоил мутации DesyncAnyProtocol и FakeResend, что критично для современных методов DPI.*

---

## 🛠 Ключевые возможности

| Категория | Возможности |
| :--- | :--- |
| **Поддержка ядер** | Zapret (`winws.exe`), ByeDPI (`ciadpi.exe`), Cloudflare Warp (`warp-plus.exe`). |
| **Режимы работы** | **Standalone**, **Hybrid**, **Parallel** (движки вместе), **Chained** (цепочка через SOCKS5). |
| **Интеллект** | Thompson Sampling, Wilson Lower Bound, Fast Start (мгновенный прогрев ТОП-стратегий). |
| **Сеть** | Network Fingerprinting (своя политика на каждую сеть), авто-подбор MTU для Warp. |
| **Автоматизация** | Авто-регистрация Warp, авто-обновление бинарников с GitHub, фоновый мониторинг. |

---

## 🚀 Новое в версии 1.6.2

> [!IMPORTANT]
> Это мажорное обновление, сфокусированное на синергии традиционных DPI-инструментов и VPN-технологий.

- **🌐 Cloudflare Warp (WireGuard/AmneziaWG):** Полная нативная интеграция. Используйте Warp как основную защиту или как "туннель в туннеле" для Zapret.
- **⚡ Режим Chaining:** Теперь можно запускать Zapret/ByeDPI, направляя их трафик через Warp. Это позволяет обходить блокировки не только по сигнатурам пакетов, но и по IP-адресам.
- **🎯 Wilson Score 2.0:** Улучшенная математика ранжирования. Теперь ИИ точнее определяет надежность стратегии на основе истории за последние 14 дней.
- **🧪 Глубокие Мутации:** Эволюция теперь затрагивает параметры `DesyncAnyProtocol`, `DesyncFooling` и `FakeResend`.
- **🚀 Fast Start:** Мгновенная проверка 3 лучших стратегий при смене сети или запуске приложения.

---

## 📸 Галерея интерфейса

<div align="center">
  <table border="0">
    <tr>
      <td><img src="https://github.com/user-attachments/assets/70dda58d-cbf3-43f8-b8ae-72b7fad3d88e" width="400" alt="Main UI" /><br/><p align="center"><i>Главный экран управления</i></p></td>
      <td><img src="https://github.com/user-attachments/assets/bf33cffb-6d56-4055-8f8e-8c807f57d9a7" width="400" alt="AI Stats" /><br/><p align="center"><i>Статистика ИИ и Эволюция</i></p></td>
    </tr>
  </table>
</div>

---

## ⚙️ Сравнение режимов

| Режим | Для чего подходит? | Сложность для провайдера |
| :--- | :--- | :--- |
| **Zapret** | YouTube, Discord, базовые обходы. | Низкая (легко детектится) |
| **Warp** | Обход блокировок по IP (Instagram, Twitter). | Средняя (блокируется по портам) |
| **Hybrid** | Когда провайдер нестабильно блокирует разные протоколы. | Высокая |
| **Chained** | Максимальная пробивная способность (DPI Bypass + VPN). | **Экстремальная** |

---

## 📅 Дорожная карта (Future)

- [ ] **Интеграция Sing-Box:** Поддержка VLESS, Reality и других современных протоколов.
- [ ] **Cloud AI Sync:** Возможность получать готовые рабочие "геномы" из облака (анонимно).
- [ ] **Advanced YouTube Probing:** Проверка скорости буферизации видео для выбора быстрейшей стратегии.

---

## ⚠️ Безопасность и WinDivert

Проект использует драйвер **WinDivert** для модификации сетевых пакетов "на лету".
- Это **не вирус**. Это инструмент системного администрирования.
- Антивирусы (особенно Kaspersky, Defender) могут ругаться на `HackTool` или `RiskTool`.
- **Решение:** Добавьте папку с программой в белый список (исключения).

---

## 🙏 Благодарности и Лицензия

Этот проект является свободным ПО и распространяется под лицензией **GNU GPLv3**.

- **[klondike0x/FluxRoute](https://github.com/klondike0x/FluxRoute)** — Оригинальный автор и архитектор. Огромное спасибо за фундамент!
- **[bol-van/zapret](https://github.com/bol-van/zapret)** — Мощнейшее ядро для Windows.
- **[hiddify/warp-plus](https://github.com/hiddify/warp-plus)** — За реализацию Warp.

---

<div align="center">

**Развивается сообществом для свободного интернета.**

[mx57](https://github.com/mx57) © 2026. Лицензия GPLv3.

**[⭐ Ставь звезду, если проект тебе помог!](https://github.com/mx57/FluxRoute_AI)**

</div>
