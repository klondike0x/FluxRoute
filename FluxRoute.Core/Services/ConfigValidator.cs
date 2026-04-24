using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FluxRoute.Core.Services;

public sealed class ConfigValidationResult
{
    public string Name { get; init; } = "";
    public string Status { get; init; } = "ok"; // ok, warning, error
    public string Message { get; init; } = "";
    public Dictionary<string, object?> Details { get; init; } = new();
}

public static class ConfigValidator
{
    /// <summary>
    /// Выполняет полную проверку конфигурации FluxRoute
    /// </summary>
    public static List<ConfigValidationResult> ValidateAll(
        string engineDir,
        string winwsPath,
        string winDivertDllPath,
        string winDivertSysPath,
        string winDivertSys64Path,
        Func<IReadOnlyList<Core.Models.ProfileItem>> getProfiles)
    {
        var results = new List<ConfigValidationResult>
        {
            CheckEngineDirectory(engineDir),
            CheckWinwsExecutable(winwsPath),
            CheckWinDivertDll(winDivertDllPath),
            CheckWinDivertDriver(winDivertSysPath, winDivertSys64Path),
            CheckProfiles(getProfiles()),
            CheckProfileSyntax(getProfiles(), engineDir)
        };

        return results;
    }

    private static ConfigValidationResult CheckEngineDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ConfigValidationResult
            {
                Name = "Директория engine",
                Status = "error",
                Message = "Путь к директории engine не указан"
            };

        if (!Directory.Exists(path))
            return new ConfigValidationResult
            {
                Name = "Директория engine",
                Status = "error",
                Message = "Директория engine не существует",
                Details = { ["path"] = path }
            };

        var requiredFiles = new[] { "winws.exe", "WinDivert.dll" };
        var missing = requiredFiles.Where(f => !File.Exists(Path.Combine(path, f))).ToList();
        if (missing.Count > 0)
            return new ConfigValidationResult
            {
                Name = "Директория engine",
                Status = "warning",
                Message = $"Отсутствуют файлы: {string.Join(", ", missing)}",
                Details = { ["missing"] = missing }
            };

        return new ConfigValidationResult
        {
            Name = "Директория engine",
            Status = "ok",
            Message = "Директория engine существует и содержит необходимые файлы"
        };
    }

    private static ConfigValidationResult CheckWinwsExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ConfigValidationResult
            {
                Name = "winws.exe",
                Status = "error",
                Message = "Путь к winws.exe не указан"
            };

        if (!File.Exists(path))
            return new ConfigValidationResult
            {
                Name = "winws.exe",
                Status = "error",
                Message = "winws.exe не найден",
                Details = { ["path"] = path }
            };

        return new ConfigValidationResult
        {
            Name = "winws.exe",
            Status = "ok",
            Message = "winws.exe найден"
        };
    }

    private static ConfigValidationResult CheckWinDivertDll(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ConfigValidationResult
            {
                Name = "WinDivert.dll",
                Status = "error",
                Message = "Путь к WinDivert.dll не указан"
            };

        if (!File.Exists(path))
            return new ConfigValidationResult
            {
                Name = "WinDivert.dll",
                Status = "error",
                Message = "WinDivert.dll не найден",
                Details = { ["path"] = path }
            };

        return new ConfigValidationResult
        {
            Name = "WinDivert.dll",
            Status = "ok",
            Message = "WinDivert.dll найден"
        };
    }

    private static ConfigValidationResult CheckWinDivertDriver(string sysPath, string sys64Path)
    {
        var exists = File.Exists(sysPath) || File.Exists(sys64Path);

        if (!exists)
            return new ConfigValidationResult
            {
                Name = "WinDivert.sys",
                Status = "error",
                Message = "Драйвер WinDivert.sys не найден",
                Details = { ["paths"] = new[] { sysPath, sys64Path } }
            };

        return new ConfigValidationResult
        {
            Name = "WinDivert.sys",
            Status = "ok",
            Message = "Драйвер WinDivert.sys найден"
        };
    }

    private static ConfigValidationResult CheckProfiles(IReadOnlyList<Core.Models.ProfileItem> profiles)
    {
        if (profiles.Count == 0)
            return new ConfigValidationResult
            {
                Name = "Профили",
                Status = "warning",
                Message = "Профили не найдены"
            };

        var invalidProfiles = profiles.Where(p => 
            string.IsNullOrWhiteSpace(p.FileName) || 
            string.IsNullOrWhiteSpace(p.FullPath) ||
            !File.Exists(p.FullPath)).ToList();

        if (invalidProfiles.Count > 0)
            return new ConfigValidationResult
            {
                Name = "Профили",
                Status = "warning",
                Message = $"Найдено {invalidProfiles.Count} проблемных профилей",
                Details = { ["invalid"] = invalidProfiles.Select(p => p.FileName).ToList() }
            };

        return new ConfigValidationResult
        {
            Name = "Профили",
            Status = "ok",
            Message = $"Найдено {profiles.Count} профилей",
            Details = { ["count"] = profiles.Count }
        };
    }

    private static ConfigValidationResult CheckProfileSyntax(
        IReadOnlyList<Core.Models.ProfileItem> profiles, 
        string engineDir)
    {
        if (profiles.Count == 0)
            return new ConfigValidationResult
            {
                Name = "Синтаксис профилей",
                Status = "ok",
                Message = "Нет профилей для проверки"
            };

        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.FullPath) || !File.Exists(profile.FullPath))
                continue;

            try
            {
                var content = File.ReadAllText(profile.FullPath);
                
                // Проверка на наличие базовых команд zapret
                if (!content.Contains("winws") && !content.Contains("--dpi-desync"))
                    warnings.Add($"{profile.FileName}: не найдены команды winws");

                // Проверка на синтаксические ошибки BAT
                if (content.Contains("@echo off") || content.Contains("echo"))
                {
                    // Проверка на незакрытые кавычки
                    var quoteCount = content.Count(c => c == '"');
                    if (quoteCount % 2 != 0)
                        errors.Add($"{profile.FileName}: незакрытые кавычки");

                    // Проверка на неправильные переменные окружения
                    if (System.Text.RegularExpressions.Regex.IsMatch(content, @"%\w+[^%]"))
                        warnings.Add($"{profile.FileName}: возможна ошибка в переменной окружения");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{profile.FileName}: ошибка чтения ({ex.Message})");
            }
        }

        if (errors.Count > 0)
            return new ConfigValidationResult
            {
                Name = "Синтаксис профилей",
                Status = "error",
                Message = $"Найдено ошибок: {errors.Count}",
                Details = { ["errors"] = errors }
            };

        if (warnings.Count > 0)
            return new ConfigValidationResult
            {
                Name = "Синтаксис профилей",
                Status = "warning",
                Message = $"Предупреждений: {warnings.Count}",
                Details = { ["warnings"] = warnings }
            };

        return new ConfigValidationResult
        {
            Name = "Синтаксис профилей",
            Status = "ok",
            Message = "Синтаксис профилей корректен"
        };
    }

    /// <summary>
    /// Форматирует результаты проверки в читаемый текст
    /// </summary>
    public static string FormatResults(IEnumerable<ConfigValidationResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ Проверка конфигурации ═══");
        sb.AppendLine();

        foreach (var result in results)
        {
            var icon = result.Status switch
            {
                "ok" => "✅",
                "warning" => "⚠️",
                "error" => "❌",
                _ => "•"
            };

            sb.AppendLine($"{icon} {result.Name}: {result.Message}");

            if (result.Details.Count > 0)
            {
                foreach (var detail in result.Details)
                {
                    sb.AppendLine($"   • {detail.Key}: {FormatValue(detail.Value)}");
                }
            }
        }

        var hasErrors = results.Any(r => r.Status == "error");
        var hasWarnings = results.Any(r => r.Status == "warning");

        sb.AppendLine();
        if (hasErrors)
            sb.AppendLine("❌ Обнаружены критические ошибки!");
        else if (hasWarnings)
            sb.AppendLine("⚠️ Обнаружены предупреждения");
        else
            sb.AppendLine("✅ Конфигурация в порядке");

        return sb.ToString();
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            IEnumerable<string> list => string.Join(", ", list),
            _ => value.ToString() ?? "null"
        };
    }
}
