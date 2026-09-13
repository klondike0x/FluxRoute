using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxRoute.Core.Services;

/// <summary>
/// Общий набор исключений движка (<c>--hostlist-exclude</c>), который собирается из двух файлов:
/// <c>list-exclude-user.txt</c> (он же вкладка «Домены») и помеченных строк «!domain»
/// в <c>list-general-user.txt</c>.
///
/// Класс держит вклады файлов раздельно: сохранение одного файла не должно затирать вклад другого.
/// Раньше коллекция исключений пересобиралась из содержимого сохраняемого файла целиком, поэтому
/// сохранение <c>list-exclude-user.txt</c> выкидывало помеченный домен из общего файла — он
/// оставался в файле, но перестал попадать в <c>--hostlist-exclude</c>, то есть исключение
/// переставало действовать (Codex P2, ревью #76).
///
/// Вклад <c>list-exclude-user.txt</c> хранится в самом классе и НЕ пересобирается из объединённого
/// набора: объединение уже включает помеченные строки файла доменов, поэтому снятая в
/// <c>list-general-user.txt</c> пометка возвращалась бы из собственной объединённой копии и
/// следующая синхронизация переносила бы домен в <c>list-exclude-user.txt</c> (Codex P2, ревью #76).
/// </summary>
public sealed class UserHostlistExclusionSet
{
    private readonly List<string> _generalFileExclusions = [];
    private List<string> _excludeFileDomains = [];

    /// <summary>Помеченные строки («!domain») файла доменов назначения; прошлый вклад заменяется.</summary>
    public IReadOnlyList<string> GeneralFileExclusions => _generalFileExclusions;

    /// <summary>Запоминает вклад <c>list-general-user.txt</c> (заменяет предыдущий).</summary>
    public void SetGeneralFileExclusions(IEnumerable<string>? exclusions)
    {
        _generalFileExclusions.Clear();
        _generalFileExclusions.AddRange(
            (exclusions ?? []).Where(domain => !string.IsNullOrWhiteSpace(domain)).Select(domain => domain.Trim()));
    }

    /// <summary>Запоминает вклад <c>list-exclude-user.txt</c>: этот файл — источник истины для своего набора.</summary>
    public void SetExcludeFileDomains(IEnumerable<string>? domains)
    {
        _excludeFileDomains = (domains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToList();
    }

    /// <summary>
    /// Текущий вклад <c>list-exclude-user.txt</c> — набор вкладки «Домены» → «Исключения».
    /// Именно он (а не объединение) должен храниться между синхронизациями: объединение уже
    /// содержит помеченные строки файла доменов, и домен из снятой пометки «воскресал» бы
    /// в наборе исключений (Codex P2, ревью #76).
    /// </summary>
    public IReadOnlyList<string> ExcludeFileDomains => _excludeFileDomains;

    /// <summary>Добавляет домены во вклад файла исключений (правки вкладки «Домены»).</summary>
    public void AddExcludeFileDomains(IEnumerable<string>? domains)
    {
        foreach (var domain in (domains ?? []).Where(domain => !string.IsNullOrWhiteSpace(domain)))
        {
            var trimmed = domain.Trim();
            if (_excludeFileDomains.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                continue;

            _excludeFileDomains.Add(trimmed);
        }
    }

    /// <summary>Убирает домен из вклада файла исключений; возвращает, был ли он там.</summary>
    public bool RemoveExcludeFileDomain(string? domain)
    {
        var trimmed = domain?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        return _excludeFileDomains.RemoveAll(
            existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>Очищает вклад файла исключений (кнопка «Очистить список»).</summary>
    public void ClearExcludeFileDomains() => _excludeFileDomains.Clear();

    /// <summary>
    /// Обновляет вклад файла исключений и отдаёт объединение вкладов для UI и движка (вызывается при
    /// пересборке набора исключений). <paramref name="savedExcludeFileDomains"/> передаётся только
    /// тогда, когда файл исключений сохранён в редакторе — его содержимое и есть новый вклад. Без
    /// аргумента вклад НЕ пересобирается из объединения: объединение уже содержит помеченные строки
    /// файла доменов, поэтому домен из снятой пометки «!domain» оставался бы в наборе исключений и
    /// переезжал бы в <c>list-exclude-user.txt</c> (Codex P2, ревью #76).
    /// </summary>
    public List<string> UpdateExclusions(IEnumerable<string>? savedExcludeFileDomains = null)
    {
        if (savedExcludeFileDomains is not null)
            SetExcludeFileDomains(savedExcludeFileDomains);

        return Build();
    }

    /// <summary>Объединение обоих вкладов — актуальный набор исключений для движка и UI.</summary>
    public List<string> Build() => UserHostlistImporter.MergeExclusions(_excludeFileDomains, _generalFileExclusions);
}
