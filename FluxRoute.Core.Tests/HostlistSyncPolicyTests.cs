using System.IO;
using FluxRoute.Core.Services;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Идемпотентность синхронизации пользовательских hostlist-файлов (issue #77; Codex P2, ревью PR #76).
/// UI-набор доменов — источник истины, но если эффективный набор в файле уже совпадает, файл
/// перезаписывать нельзя: иначе при каждом старте защиты терялись бы комментарии и пустые строки,
/// которые пользователь сохранил в редакторе.
/// </summary>
public sealed class HostlistSyncPolicyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "fluxroute-hostlist-" + Guid.NewGuid().ToString("N"));

    public HostlistSyncPolicyTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временная папка не критична для результата теста.
        }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void NeedsWrite_FileWithCommentsAndSameDomains_ReturnsFalse()
    {
        // Файл, сохранённый редактором: комментарии, пустые строки и те же домены.
        var path = Write("list-exclude-user.txt", "# мои исключения\n\napi.example.com\n; старая заметка\ncdn.example.com\n");

        Assert.False(HostlistSyncPolicy.NeedsWrite(
            path,
            new[] { "api.example.com", "cdn.example.com" },
            isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_AddedDomain_ReturnsTrue()
    {
        var path = Write("list-exclude-user.txt", "# мои исключения\napi.example.com\n");

        Assert.True(HostlistSyncPolicy.NeedsWrite(
            path,
            new[] { "api.example.com", "cdn.example.com" },
            isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_RemovedDomain_ReturnsTrue()
    {
        var path = Write("list-exclude-user.txt", "api.example.com\ncdn.example.com\n");

        Assert.True(HostlistSyncPolicy.NeedsWrite(path, new[] { "api.example.com" }, isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_IgnoresCaseAndWhitespace()
    {
        var path = Write("list-general-user.txt", "  API.Example.COM  \n\n");

        Assert.False(HostlistSyncPolicy.NeedsWrite(path, new[] { "api.example.com" }, isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_ExclamationPrefix_IsNotADomain()
    {
        // В общем списке «!домен» — это исключение, а не цель: сравнивать его с набором целей нельзя.
        var path = Write("list-general-user.txt", "api.example.com\n!cdn.example.com\n");

        Assert.False(HostlistSyncPolicy.NeedsWrite(path, new[] { "api.example.com" }, isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_MissingFile_WithDomains_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "list-general-user.txt");

        Assert.True(HostlistSyncPolicy.NeedsWrite(path, new[] { "api.example.com" }, isEmpty: false));
    }

    [Fact]
    public void NeedsWrite_MissingFile_AndEmptySet_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "list-general-user.txt");

        Assert.False(HostlistSyncPolicy.NeedsWrite(path, Array.Empty<string>(), isEmpty: true));
    }

    [Fact]
    public void NeedsWrite_ExistingFile_AndEmptySet_ReturnsTrue()
    {
        // Набор в UI очищен — файл (в том числе комментарии) должен быть убран/очищен.
        var path = Write("list-general-user.txt", "api.example.com\n");

        Assert.True(HostlistSyncPolicy.NeedsWrite(path, Array.Empty<string>(), isEmpty: true));
    }

    [Fact]
    public void NeedsWrite_FileWithOnlyComments_AndEmptySet_ReturnsFalse()
    {
        var path = Write("list-general-user.txt", "# пусто\n\n");

        Assert.False(HostlistSyncPolicy.NeedsWrite(path, Array.Empty<string>(), isEmpty: true));
    }
}
