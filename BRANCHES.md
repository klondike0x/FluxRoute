# 🌿 Структура веток FluxRoute v1.7.0

> **Базовая ветка разработки:** `v2-dev` (создана от `develop` @ `67a8282`)
> **Целевой релиз:** v1.7.0 (MINOR по SemVer)
> **Дата создания:** 2026-07-22

---

## 📐 Схема ветвления

```
master                    ← только стабильные релизы (теги v1.6.0, v1.7.0)
 └── develop              ← интеграция стабильных фичей, поддержка v1.6.x
      └── v2-dev          ← ИНТЕГРАЦИОННАЯ ветка v1.7.0 (все фичи сливаются сюда)
           ├── feature/interface        ← новый дизайн (Kudu)
           ├── feature/core             ← сетевое ядро (WireGuard/AWG/VLESS/Reality/Hysteria2)
           ├── feature/engine-zapret2   ← интеграция Zapret2
           ├── feature/orchestrator     ← расширение оркестратора
           ├── feature/mods             ← система модификаций
           ├── feature/marketplace      ← клиент API модов (Goshkow)
           ├── feature/settings         ← новая система настроек
           ├── feature/updater          ← автообновления программы и модов
           └── feature/doh              ← DNS-over-HTTPS
```

---

## 📋 Описание веток

| Ветка | Назначение | Ключевые сервисы | Приоритет |
|---|---|---|---|
| `v2-dev` | Интеграционная ветка v1.7.0. Все feature-ветки сливаются **только** сюда. Прямые коммиты запрещены (кроме инфраструктурных: BRANCHES.md, CI, общие контракты). | — | 🔴 Критический |
| `feature/interface` | Новый дизайн в стиле Kudu: круглая кнопка запуска, дашборд, анимации, тёмная/светлая тема. | `IThemeService`, `IAnimationService`, `IDashboardService` | 🟡 Средний |
| `feature/core` | Сетевое ядро: поддержка протоколов WireGuard, AWG, VLESS, Reality, Hysteria2. Абстракция транспорта. | `IVpnProtocolService`, `IWireGuardService`, `IVlessService`, `IHysteriaService`, `IRealityService` | 🔴 Критический |
| `feature/engine-zapret2` | Интеграция с Zapret2: управление процессами, передача параметров, мониторинг состояния. | `IZapret2EngineService`, `IProcessManager`, `IEngineParameterBuilder` | 🔴 Критический |
| `feature/orchestrator` | Расширение оркестратора: перебор листов, динамическое обучение, адаптация под Zapret2. | `IOrchestratorService`, `IListRotationService`, `IAdaptiveLearningService` | 🟡 Средний |
| `feature/mods` | Система модификаций: хранение, активация/деактивация, интеграция с маркетплейсом. | `IModService`, `IModStorageService`, `IModActivationService` | 🟡 Средний |
| `feature/marketplace` | Клиент для API модов от Goshkow: каталог, установка, обновления, рейтинги. | `IMarketplaceService`, `IModCatalogService`, `IModInstallerService` | 🟢 Низкий |
| `feature/settings` | Новая система настроек: DoH, выбор DNS, источники engine, миграция с v1.6.x. | `ISettingsService` (расширение), `IDnsSettingsService`, `ISettingsMigrationService` | 🔴 Критический |
| `feature/updater` | Автоматические обновления программы и модов: проверка, скачивание, применение. | `IAppUpdaterService`, `IModUpdaterService`, `IUpdateChannelService` | 🟡 Средний |
| `feature/doh` | Поддержка DNS-over-HTTPS: встроенный клиент, выбор серверов (Cloudflare, Google, Quad9), fallback на системный DNS. | `IDohService`, `IDnsResolverService`, `IDohServerProvider` | 🟡 Средний |

---

## 🔄 Правила работы с ветками

### 1. Создание feature-ветки (если нужна новая)

```bash
git checkout v2-dev
git pull origin v2-dev
git checkout -b feature/<имя-фичи>
```

### 2. Работа в feature-ветке

```bash
# Регулярно подтягивать изменения из v2-dev (минимум раз в день)
git checkout feature/<имя-фичи>
git fetch origin
git rebase origin/v2-dev        # предпочтительно — линейная история
# или: git merge origin/v2-dev  # если rebase вызывает конфликты
```

### 3. Слияние feature → v2-dev

```bash
# 1. Убедиться, что ветка актуальна
git checkout feature/<имя-фичи>
git rebase origin/v2-dev

# 2. Прогнать проверки
dotnet build FluxRoute.slnx     # 0 ошибок
dotnet test FluxRoute.slnx      # все тесты зелёные

# 3. Создать Pull Request: feature/<имя> → v2-dev
#    (через GitHub UI или: gh pr create --base v2-dev --head feature/<имя>)

# 4. После аппрува — squash merge (чистая история в v2-dev)
# 5. Удалить feature-ветку после мержа
git branch -d feature/<имя-фичи>
git push origin --delete feature/<имя-фичи>
```

### 4. Релиз v1.7.0

```bash
# Когда все фичи влиты в v2-dev и стабилизированы:
git checkout -b release/1.7.0 v2-dev
# ... финальные правки, бамп версии, CHANGELOG ...
git checkout master
git merge release/1.7.0
git tag -a v1.7.0 -m "Release v1.7.0"
git checkout develop
git merge release/1.7.0        # синхронизация обратно в develop
git branch -d release/1.7.0
```

---

## 📊 Рекомендуемый порядок разработки и слияния

Порядок учитывает зависимости между фичами (фича выше в списке может потребоваться фиче ниже).

### Фаза 1 — Фундамент (слиять первыми, минимум взаимозависимостей)

| # | Ветка | Почему первой | Зависимости |
|---|---|---|---|
| 1 | `feature/settings` | Новая система настроек нужна почти всем остальным (DoH, DNS, источники engine). Включает миграцию с v1.6.x. | — |
| 2 | `feature/core` | Сетевое ядро — база для orchestrator и engine. | settings |
| 3 | `feature/doh` | DoH-клиент относительно изолирован, но использует settings для выбора серверов. | settings |

### Фаза 2 — Движок и оркестрация

| # | Ветка | Почему | Зависимости |
|---|---|---|---|
| 4 | `feature/engine-zapret2` | Интеграция Zapret2 опирается на core (протоколы) и settings (источники). | core, settings |
| 5 | `feature/orchestrator` | Оркестратор адаптируется под Zapret2, использует core. | engine-zapret2, core |

### Фаза 3 — Пользовательский уровень

| # | Ветка | Почему | Зависимости |
|---|---|---|---|
| 6 | `feature/mods` | Система модов — хранение и активация. Независима от UI. | settings |
| 7 | `feature/marketplace` | Маркетплейс использует mods (установка) и updater (обновления модов). | mods |
| 8 | `feature/updater` | Обновления программы и модов. Зависит от marketplace (моды) и settings (каналы). | marketplace, settings |

### Фаза 4 — Интерфейс (слиять последним)

| # | Ветка | Почему последней | Зависимости |
|---|---|---|---|
| 9 | `feature/interface` | Новый UI собирает всё воедино: дашборд показывает состояние core/orchestrator, настройки — новую систему settings, маркетплейс — каталог модов. Слияние последним минимизирует конфликты в XAML. | все предыдущие |

> ⚠️ **Важно:** это рекомендуемый порядок *слияния*, а не *старта*. Разработку можно вести параллельно, но мержить в `v2-dev` желательно в указанной последовательности, чтобы минимизировать конфликты.

---

## 🏗️ Архитектурные требования (для всех веток)

Эти правила обязательны для каждой feature-ветки (см. `AGENTS/00-GOLDEN-RULES.md`).

### DI и интерфейсы
- **Каждый новый сервис** — с интерфейсом (`IXxxService`), реализация в `FluxRoute.Core/Services/`.
- **Регистрация в DI** — в `App.xaml.cs` через `services.AddSingleton<IXxxService, XxxService>()` (или `AddTransient`/`AddScoped` по жизненному циклу, см. `AGENTS/06-DI-LIFECYCLE.md`).
- **Запрещено** `new XxxService()` вне DI-контейнера.

### Слои
- **Бизнес-логика** — только в `FluxRoute.Core/Services/`. Никакой логики в ViewModels кроме биндинга.
- **ViewModels** — CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- **Views** — только XAML + code-behind для чисто визуальных вещей (анимации).

### Асинхронность и I/O
- Все I/O-операции — `async Task` / `async Task<Result<T>>`.
- `IHttpClientFactory` для всех HTTP-вызовов (никогда `new HttpClient()`).
- **Polly** — retry-политики на named HTTP-клиентах (регистрация в `App.xaml.cs`).
- `CancellationToken` — на всех публичных async-методах.
- `ConfigureAwait(false)` — в `FluxRoute.Core`, `FluxRoute.Updater`, `FluxRoute.AI`.
- **Запрещено** `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` (кроме `OnExit`).

### Обработка ошибок
- Публичные методы сервисов возвращают `Result<T>` (см. `AGENTS/04-ERROR-HANDLING.md`).
- `ILogger<T>` в каждом классе (через конструктор).
- Graceful degradation — при недоступности сервиса приложение не падает.

### Безопасность
- `IOptions<T>` для конфигурации.
- **Никаких секретов в коде** — API-ключи маркетплейса, токены DoH и т.п. только через настройки/переменные окружения.

### Комментарии
- Все комментарии — **на русском языке**. Идентификаторы — на английском.

### Коммиты
- Conventional Commits: `<type>(<scope>): <описание на русском>` (см. `AGENTS/11-GIT-COMMITS.md`).
- Scope соответствует ветке: `feat(core):`, `fix(interface):`, `test(orchestrator):` и т.д.

> 📝 **Замечание о кроссплатформенности:** текущий UI-слой — WPF (Windows-only). Для будущей кроссплатформенности (Avalonia UI) **вся** платформенно-зависимая логика (реестр, TaskScheduler, WPF-специфика) должна изолироваться за интерфейсами (`IPlatformService`, `IAutorunService`), чтобы `FluxRoute.Core` оставался платформенно-нейтральным. При написании нового кода в v1.7.0 избегайте прямых вызовов Windows API в Core — выносите за абстракцию.

---

## 🧪 Требования к тестированию (по веткам)

Фреймворки: **xUnit + Moq**. Проект тестов: `FluxRoute.Core.Tests`.
Паттерн: **Arrange-Act-Assert**. Покрытие ключевых компонентов — **≥80%** (см. `AGENTS/08-TESTING.md`).

Перед каждым слиянием в `v2-dev`:
```bash
dotnet build FluxRoute.slnx   # 0 ошибок, 0 предупреждений (warning as error)
dotnet test FluxRoute.slnx    # все тесты зелёные
```

| Ветка | Что тестировать | Минимум тестов |
|---|---|---|
| `feature/settings` | Миграция настроек v1.6→v1.7, сериализация/десериализация, валидация DNS-серверов, значения по умолчанию | 20 |
| `feature/core` | Построение конфигов WireGuard/VLESS/Hysteria2, парсинг ключей, валидация параметров протоколов, обработка некорректных конфигов | 25 |
| `feature/doh` | Резолв через DoH (мок HttpClient), выбор сервера, fallback на системный DNS при недоступности, таймауты и retry (Polly) | 15 |
| `feature/engine-zapret2` | Запуск/остановка процесса Zapret2, построение командной строки параметров, обработка падения процесса, мониторинг состояния | 20 |
| `feature/orchestrator` | Перебор листов, динамическое обучение (мок AI), адаптация под Zapret2, выбор оптимальной стратегии | 20 |
| `feature/mods` | Установка/удаление/активация мода, целостность хранилища, конфликты версий | 15 |
| `feature/marketplace` | Получение каталога (мок HttpClient + Polly), поиск, установка мода, обработка ошибок API (404, 500, timeout) | 15 |
| `feature/updater` | Проверка обновлений, скачивание (мок HttpClient), верификация контрольных сумм, откат при ошибке | 15 |
| `feature/interface` | ViewModel-логика: команды, состояния дашборда, привязки (без тестирования XAML) | 10 |

### Шаблон теста

```csharp
public class DohServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();
    private readonly Mock<ILogger<DohService>> _loggerMock = new();

    [Fact]
    public async Task ResolveAsync_WhenDohServerUnavailable_FallsBackToSystemDns()
    {
        // Arrange — настраиваем мок HttpClient на ошибку
        // Act — вызываем ResolveAsync
        // Assert — проверяем fallback на системный DNS
    }
}
```

---

## ✅ Чек-лист перед слиянием в v2-dev

- [ ] Ветка отребейжена на актуальный `origin/v2-dev`
- [ ] `dotnet build FluxRoute.slnx` — 0 ошибок
- [ ] `dotnet test FluxRoute.slnx` — все тесты зелёные, покрытие новых сервисов ≥80%
- [ ] Новые сервисы имеют интерфейсы и зарегистрированы в DI (`App.xaml.cs`)
- [ ] Публичные async-методы возвращают `Result<T>`, принимают `CancellationToken`
- [ ] HTTP через `IHttpClientFactory` + Polly
- [ ] Комментарии на русском, коммиты в Conventional Commits
- [ ] Нет секретов в коде
- [ ] PR создан: `feature/<имя>` → `v2-dev`, прошёл ревью

---

## 🚀 Дальнейшие шаги после создания веток

1. **Настроить защиту ветки `v2-dev` на GitHub** — запрет force-push, обязательные PR-ревью, обязательный проход CI (build + test) перед мержем.
2. **Создать GitHub Issues** под каждую фичу и связать с milestone `v1.7.0`.
3. **Настроить CI** (GitHub Actions) для прогона `dotnet build` + `dotnet test` на каждый PR в `v2-dev`.
4. **Создать Project Board** (GitHub Projects) для отслеживания прогресса по фазам.
5. **Определить общие контракты раньше кодинга** — интерфейсы сервисов (`IDohService`, `IModService` и т.д.) согласовать и влить в `v2-dev` первым коммитом, чтобы параллельная разработка шла против стабильных контрактов, а не меняющихся реализаций.
6. **Начать с Фазы 1** — `feature/settings` (фундамент для остальных).

---

> 📖 См. также: `AGENTS/00-GOLDEN-RULES.md`, `AGENTS/01-ARCHITECTURE.md`, `AGENTS/08-TESTING.md`, `AGENTS/11-GIT-COMMITS.md`
