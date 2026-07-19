# 🤖 AGENTS — Структурированное руководство для AI-агентов

> **Проект:** FluxRoute (C# 13, .NET 10, WPF, CommunityToolkit.Mvvm)  
> **Лицензия:** GPLv3  
> **Версионирование:** SemVer  
> **Обновлено:** 2026-07-03  
> **Версия:** 2.0

---

## 📋 Быстрая навигация

Выбери релевантный файл в зависимости от твоей задачи:

### 🚀 Начни отсюда

| Файл | Описание | Время |
| --- | --- | --- |
| [**00-GOLDEN-RULES.md**](00-GOLDEN-RULES.md) | 🚨 10 критических правил (ОБЯЗАТЕЛЬНО!) | 2 мин |
| [**14-QUICK-START.md**](14-QUICK-START.md) | 🎯 Быстрый старт для новых агентов | 5 мин |

### 🧱 Архитектура и дизайн

| Файл | Описание | Когда открывать |
| --- | --- | --- |
| [**01-ARCHITECTURE.md**](01-ARCHITECTURE.md) | 🧱 MVVM + DI, структура проектов | Первый раз в проекте |
| [**06-DI-LIFECYCLE.md**](06-DI-LIFECYCLE.md) | ⚙️ Dependency Injection lifecycle | Нужно настроить сервисы |

### 💻 Написание кода

| Файл | Описание | Когда открывать |
| --- | --- | --- |
| [**02-ASYNC.md**](02-ASYNC.md) | ⚡ async/await, CancellationToken, UI threading | Работаешь с async кодом |
| [**03-SECURITY.md**](03-SECURITY.md) | 🛡️ IOptions<T>, секреты, логирование | Работаешь с конфигом |
| [**04-ERROR-HANDLING.md**](04-ERROR-HANDLING.md) | 🧩 Result<T> паттерн, graceful degradation | Обрабатываешь ошибки |
| [**07-WPF-GUIDELINES.md**](07-WPF-GUIDELINES.md) | 🖥️ CommunityToolkit.Mvvm, Converters, Bindings | Работаешь с WPF UI |

### 🚀 Оптимизация

| Файл | Описание | Когда открывать |
| --- | --- | --- |
| [**05-PERFORMANCE.md**](05-PERFORMANCE.md) | 🚀 Virtualization, memory leaks, optimization | Проблемы с производительностью |

### 🧪 Тестирование и качество

| Файл | Описание | Когда открывать |
| --- | --- | --- |
| [**08-TESTING.md**](08-TESTING.md) | 🧪 xUnit + Moq, примеры тестов | Пишешь тесты |
| [**09-COMMON-MISTAKES.md**](09-COMMON-MISTAKES.md) | 🐛 7 типичных ошибок AI с решениями | Нужно избежать ошибок |
| [**12-CHECKLIST.md**](12-CHECKLIST.md) | ✅ Чеклист перед коммитом | Готовишь к отправке |

### 📚 Документация и процессы

| Файл | Описание | Когда открывать |
| --- | --- | --- |
| [**10-BUILD-CI-CD.md**](10-BUILD-CI-CD.md) | 🛠️ Сборка, тесты, GitHub Actions | Работаешь с CI/CD |
| [**11-GIT-COMMITS.md**](11-GIT-COMMITS.md) | 📝 Conventional Commits, примеры | Делаешь коммит |
| [**13-TROUBLESHOOTING.md**](13-TROUBLESHOOTING.md) | 🆘 Диагностика проблем, решения | Есть баг или зависание |

---

## 🎯 По типам задач

### Новая фича

1. Прочитай [01-ARCHITECTURE.md](01-ARCHITECTURE.md) — где это разместить?
2. Открой [02-ASYNC.md](02-ASYNC.md) — как сделать асинхронно?
3. Посмотри [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) — как обрабатывать ошибки?
4. Напиши тесты: [08-TESTING.md](08-TESTING.md)
5. Перед коммитом: [12-CHECKLIST.md](12-CHECKLIST.md)

### Исправление бага

1. Посмотри [13-TROUBLESHOOTING.md](13-TROUBLESHOOTING.md) — диагностика
2. Проверь [09-COMMON-MISTAKES.md](09-COMMON-MISTAKES.md) — может быть похожая ошибка?
3. Добавь тесты: [08-TESTING.md](08-TESTING.md)
4. Чеклист: [12-CHECKLIST.md](12-CHECKLIST.md)

### Оптимизация

1. Чита [05-PERFORMANCE.md](05-PERFORMANCE.md) — что оптимизировать?
2. Профилируй и тестируй: [08-TESTING.md](08-TESTING.md)
3. Документируй изменения: [11-GIT-COMMITS.md](11-GIT-COMMITS.md)

---

## ⚡ Экспресс-версия (5 минут)

**Если ты спешишь:**

1. [00-GOLDEN-RULES.md](00-GOLDEN-RULES.md) — **2 мин**
   - 10 главных правил

2. [09-COMMON-MISTAKES.md](09-COMMON-MISTAKES.md) — **2 мин**
   - 7 типичных ошибок

3. [12-CHECKLIST.md](12-CHECKLIST.md) — **1 мин**
   - Чеклист перед отправкой

**Итого: 5 минут. Потом открывай нужные разделы по мере необходимости.**

---

## 📖 Полное прочтение (1 час)

Порядок для новичков:

1. [00-GOLDEN-RULES.md](00-GOLDEN-RULES.md) — 2 мин
2. [01-ARCHITECTURE.md](01-ARCHITECTURE.md) — 5 мин
3. [02-ASYNC.md](02-ASYNC.md) — 8 мин
4. [03-SECURITY.md](03-SECURITY.md) — 5 мин
5. [04-ERROR-HANDLING.md](04-ERROR-HANDLING.md) — 5 мин
6. [06-DI-LIFECYCLE.md](06-DI-LIFECYCLE.md) — 5 мин
7. [07-WPF-GUIDELINES.md](07-WPF-GUIDELINES.md) — 8 мин
8. [08-TESTING.md](08-TESTING.md) — 8 мин
9. [09-COMMON-MISTAKES.md](09-COMMON-MISTAKES.md) — 5 мин
10. [12-CHECKLIST.md](12-CHECKLIST.md) — 2 мин

**Остальные файлы открывай по мере необходимости.**

---

## 🚨 Абсолютный минимум

**Перед ЛЮБОЙ кодировкой прочитай:**

- ✅ [00-GOLDEN-RULES.md](00-GOLDEN-RULES.md) — золотые правила
- ✅ [09-COMMON-MISTAKES.md](09-COMMON-MISTAKES.md) — типичные ошибки
- ✅ [12-CHECKLIST.md](12-CHECKLIST.md) — чеклист перед коммитом

---

## 💡 Советы для AI-агентов

### Как использовать эту папку

```
1. Прочитай описание задачи
2. Определи категорию (новая фича / баг / оптимизация)
3. Открой соответствующие файлы из таблицы выше
4. Реализуй, тестируй, коммитируй
```

### Если что-то не ясно

```
1. Ищи в таблице выше
2. Если не нашёл → открой 09-COMMON-MISTAKES.md
3. Если всё ещё не ясно → 13-TROUBLESHOOTING.md
4. Если совсем потерян → 14-QUICK-START.md
```

---

## 📞 Контакты

- **Репозиторий:** https://github.com/klondike0x/FluxRoute
- **Лицензия:** GPLv3

---

**Версия:** 2.0 | **Обновлено:** 2026-07-03