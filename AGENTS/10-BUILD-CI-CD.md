# 10 — Сборка, тесты, релиз

> **Навигация:** [AGENTS/README.md](README.md)  
> **Связанные файлы:** [11-GIT-COMMITS.md](11-GIT-COMMITS.md)

---

## 🛠️ Локально

```bash
# Восстановление
dotnet restore FluxRoute.slnx

# Сборка
dotnet build FluxRoute.slnx

# Тесты (ВСЕГДА перед коммитом)
dotnet test FluxRoute.slnx --verbosity normal

# Форматирование
dotnet format FluxRoute.slnx

# Публикация релиза
dotnet publish FluxRoute/FluxRoute.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish
```

---

## ⚙️ CI/CD (GitHub Actions)

**Триггеры:**

- `workflow_dispatch` — ручной
- `push v*` — автоматический релиз

**Критическое правило:** тег `v1.2.3` ↔ `<Version>1.2.3</Version>` в csproj. Иначе билд падает.

**Основные шаги:**

1. Проверка тега.
2. Восстановление.
3. Тесты.
4. Публикация.
5. ZIP архив.
6. Создание релиза.

---

**Следующий файл:** [11-GIT-COMMITS.md](https://11-GIT-COMMITS.md)

