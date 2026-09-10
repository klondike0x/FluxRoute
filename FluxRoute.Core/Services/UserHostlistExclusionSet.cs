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

    /// <summary>Объединение обоих вкладов — актуальный набор исключений для движка и UI.</summary>
    public List<string> Build() => UserHostlistImporter.MergeExclusions(_excludeFileDomains, _generalFileExclusions);
}
