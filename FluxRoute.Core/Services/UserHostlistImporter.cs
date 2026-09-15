using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FluxRoute.Core.Services;

/// <summary>
/// Результат разбора пользовательского hostlist-файла: домены назначения и домены-исключения.
/// </summary>
public sealed record HostlistImportResult(
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Excludes);

/// <summary>
/// Разбор содержимого пользовательских hostlist-файлов движка.
/// Строка с префиксом «!» — исключение (та же семантика, что у legacy-поля UserCustomSitesText),
/// поэтому при сохранении <see cref="TargetFileName"/> помеченный домен обязан попасть в набор
/// исключений, а не в целевые: раньше префикс просто отбрасывался, домен становился целевым,
/// синхронизация переписывала его в файл без пометки и явное исключение превращалось
/// во включение (Codex P2, ревью #76).
/// </summary>
public static class UserHostlistImporter
{
    /// <summary>Файл доменов назначения: префикс «!» отмечает исключение.</summary>
    public const string TargetFileName = "list-general-user.txt";

    /// <summary>Файл исключений: все строки — исключения, даже без префикса «!».</summary>
    public const string ExclusionFileName = "list-exclude-user.txt";

    /// <summary>
    /// Разбирает <paramref name="content"/> в два набора. <paramref name="normalize"/> —
    /// нормализация домена (убирает протокол, «www.», путь), передаётся вызывающей стороной,
    /// чтобы правило нормализации осталось единственным на всё приложение.
    /// </summary>
    public static HostlistImportResult Classify(string fileName, string? content, Func<string, string> normalize)
    {
        ArgumentNullException.ThrowIfNull(normalize);

        var isExclusionFile = string.Equals(fileName, ExclusionFileName, StringComparison.OrdinalIgnoreCase);
        var targets = new List<string>();
        var excludes = new List<string>();

        foreach (var line in EnumerateLines(content))
        {
            var marked = line.StartsWith("!", StringComparison.Ordinal);
            var raw = marked ? line[1..] : line;
            var domain = normalize(raw)?.Trim() ?? string.Empty;
            if (domain.Length == 0)
                continue;

            if (marked || isExclusionFile)
                excludes.Add(domain);
            else
                targets.Add(domain);
        }

        var excludesSet = new HashSet<string>(excludes, StringComparer.OrdinalIgnoreCase);
        var targetsResult = targets
            .Where(domain => !excludesSet.Contains(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HostlistImportResult(targetsResult, excludes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Читает помеченные строки («!domain») из файла доменов. Синхронизация — единственное место,
    /// где <c>list-exclude-user.txt</c> получает актуальный набор исключений, поэтому вклад файла
    /// перечитывается с диска: иначе исключение, помеченное до этой правки или восстановленное из
    /// старого файла, так и не попало бы в <c>--hostlist-exclude</c>.
    /// </summary>
    public static List<string> ReadExclusionsFromFile(string path, Func<string, string> normalize)
    {
        ArgumentNullException.ThrowIfNull(normalize);

        try
        {
            if (!File.Exists(path))
                return [];

            return Classify(TargetFileName, File.ReadAllText(path), normalize).Excludes.ToList();
        }
        catch
        {
            // Файла нет или он занят — исключения просто не добавим.
            return [];
        }
    }

    /// <summary>
    /// Объединяет исключения из двух источников: содержимое <c>list-exclude-user.txt</c> (он же
    /// вкладка «Домены») и помеченные строки <c>list-general-user.txt</c>. Без объединения
    /// сохранение одного файла затирало вклад другого, и помеченный домен переставал попадать
    /// в <c>--hostlist-exclude</c> (Codex P2, ревью #76).
    /// </summary>
    public static List<string> MergeExclusions(IEnumerable<string>? excludeFileDomains, IEnumerable<string>? generalFileExclusions)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in (excludeFileDomains ?? []).Concat(generalFileExclusions ?? []))
        {
            var trimmed = domain?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed))
                continue;

            result.Add(trimmed);
        }

        return result;
    }

    /// <summary>
    /// Вклад <c>list-exclude-user.txt</c> — отдельно от вклада помеченных строк файла доменов.
    /// Помеченные строки принадлежат ДРУГОМУ файлу, но их копию синхронизация хранит и в файле
    /// исключений (движок читает исключения только отсюда), поэтому «зеркало» надо вычитать в двух
    /// случаях: при переносе объединённого набора настроек прежних версий и при сохранении файла
    /// исключений из редактора — иначе снятая в файле доменов пометка «!domain» оставалась бы в силе
    /// уже через файл исключений (Codex P2, ревью #76 и pullrequestreview-5191366024).
    /// </summary>
    public static List<string> DeriveExcludeFileDomains(
        IEnumerable<string>? mergedDomains,
        IEnumerable<string>? generalFileExclusions)
    {
        var generalMarkers = new HashSet<string>(NormalizeDomains(generalFileExclusions), StringComparer.OrdinalIgnoreCase);

        return NormalizeDomains(mergedDomains)
            .Where(domain => !generalMarkers.Contains(domain))
            .ToList();
    }

    /// <summary>
    /// Вклад <c>list-exclude-user.txt</c> при запуске приложения. Сохранённый список используется
    /// КАК ЕСТЬ, даже если он пуст (например, все исключения приходили из пометок «!domain»):
    /// повторный разбор объединённого набора вернул бы в набор исключений домен из пометки, снятой
    /// пока приложение было закрыто. Выводить вклад разрешено только когда поля в настройках ещё нет
    /// (<c>null</c>) — это настройки прежних версий (Codex P2, ревью pullrequestreview-5191366024).
    /// </summary>
    public static List<string> ResolveExcludeFileDomains(
        IEnumerable<string>? persistedExcludeFileDomains,
        IEnumerable<string>? mergedDomains,
        IEnumerable<string>? generalFileExclusions)
        => persistedExcludeFileDomains is not null
            ? NormalizeDomains(persistedExcludeFileDomains)
            : DeriveExcludeFileDomains(mergedDomains, generalFileExclusions);

    /// <summary>Убирает пустые записи, обрезает пробелы и дедуплицирует набор доменов.</summary>
    private static List<string> NormalizeDomains(IEnumerable<string>? domains)
        => (domains ?? [])
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Убирает из содержимого файла доменов помеченные строки «!domain», для которых
    /// <paramref name="shouldRemove"/> вернул true. Домен перед сравнением нормализуется тем же
    /// правилом, что и при разборе файла (<paramref name="normalize"/>): пометка, записанная вручную
    /// как «!https://www.example.com/path», иначе не совпала бы с нормализованным доменом из UI —
    /// строка осталась бы в файле, а следующая синхронизация вернула бы исключение
    /// (Codex P2, ревью pullrequestreview-5191401080 и pullrequestreview-5191575589).
    /// Остальные строки, включая комментарии, пустые строки и переводы строк, сохраняются как есть.
    /// Возвращает <c>null</c>, если удалять нечего и файл переписывать не нужно.
    ///
    /// Нужно при удалении исключения из вкладки «Домены»: она показывает объединение вкладов, а
    /// синхронизация перечитывает пометки файла доменов с диска — без правки файла удалённое
    /// исключение возвращалось бы обратно.
    /// </summary>
    public static string? RemoveMarkerLines(
        string? content,
        Func<string, string> normalize,
        Func<string, bool> shouldRemove)
    {
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(shouldRemove);

        if (string.IsNullOrEmpty(content))
            return null;

        var result = new StringBuilder();
        var removed = 0;

        foreach (var (line, terminator) in EnumerateLinesWithTerminators(content))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("!", StringComparison.Ordinal))
            {
                var domain = normalize(trimmed[1..].Trim())?.Trim() ?? string.Empty;
                if (domain.Length > 0 && shouldRemove(domain))
                {
                    removed++;
                    continue;
                }
            }

            result.Append(line).Append(terminator);
        }

        return removed > 0 ? result.ToString() : null;
    }

    /// <summary>
    /// Перечисляет строки вместе с их переводом строки (LF, CRLF или CR) — нужно, чтобы
    /// редактирование файла не меняло переводы строк в остальных строках.
    /// </summary>
    private static IEnumerable<(string Line, string Terminator)> EnumerateLinesWithTerminators(string content)
    {
        var start = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != (char)13 && content[i] != (char)10)
                continue;

            var terminatorLength = content[i] == (char)13 && i + 1 < content.Length && content[i + 1] == (char)10 ? 2 : 1;
            yield return (content[start..i], content.Substring(i, terminatorLength));

            i += terminatorLength - 1;
            start = i + 1;
        }

        if (start < content.Length)
            yield return (content[start..], string.Empty);
    }

    private static IEnumerable<string> EnumerateLines(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            yield break;

        foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0
                || line.StartsWith("#", StringComparison.Ordinal)
                || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
        }
    }
}
