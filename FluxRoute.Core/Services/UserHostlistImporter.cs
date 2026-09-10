using System;
using System.Collections.Generic;
using System.Linq;

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
