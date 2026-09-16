using System.IO;

namespace FluxRoute.Core.Services;

/// <summary>
/// Решение о записи пользовательского hostlist-файла (list-general-user.txt, list-exclude-user.txt).
/// v1.7.1: синхронизация из интерфейса идемпотентна — если эффективный набор доменов в файле уже
/// совпадает с набором из UI, файл не трогаем. Иначе при каждом старте защиты файл переписывался бы
/// из коллекции UI и терял комментарии и пустые строки, которые пользователь только что сохранил
/// в редакторе (issue #77; Codex P2, ревью релизного PR #76 — для списка исключений).
/// </summary>
public static class HostlistSyncPolicy
{
    /// <summary>
    /// true — файл нужно перезаписать (или создать при непустом наборе), false — содержимое совпадает.
    /// Комментарии (#, ;) при сравнении игнорируются: важно только эффективное множество доменов.
    ///
    /// <paramref name="markedLinesAreExclusions"/> — для <c>list-exclude-user.txt</c>, где строка
    /// «!domain» тоже исключение (<see cref="UserHostlistImporter.Classify"/> принимает любую её форму).
    /// В файле доменов такие строки — вклад другого набора, поэтому там они отбрасываются: иначе
    /// исключение сравнивалось бы с набором целей и файл переписывался бы на каждом старте защиты
    /// (Codex P2, ревью pullrequestreview-5191401080).
    /// </summary>
    public static bool NeedsWrite(
        string path,
        IReadOnlyList<string> domains,
        bool isEmpty,
        bool markedLinesAreExclusions = false)
    {
        try
        {
            if (!File.Exists(path))
                return !isEmpty;

            var existing = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line)
                    && !line.StartsWith("#", StringComparison.Ordinal)
                    && !line.StartsWith(";", StringComparison.Ordinal))
                .Select(line => markedLinesAreExclusions && line.StartsWith("!", StringComparison.Ordinal)
                    ? line[1..].Trim()
                    : line)
                .Where(line => line.Length > 0
                    && (markedLinesAreExclusions || !line.StartsWith("!", StringComparison.Ordinal)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wanted = domains.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return !existing.SetEquals(wanted);
        }
        catch
        {
            return true;
        }
    }
}
