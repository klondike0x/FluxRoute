using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class HostlistsViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public HostlistsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteHostlistsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "lists"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void SaveCommand_UserList_NotifiesOwnerWithUpdatedContent()
    {
        var notifications = new List<(string FileName, string Content)>();
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { },
            onSaved: (fileName, savedContent) => notifications.Add((fileName, savedContent)));

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");
        viewModel.EditorContent = "# комментарий\r\nhttps://example.com:8443/login/?from=mail#top\r\nhttp://example.org///\r\n";

        viewModel.SaveCommand.Execute(null);

        var expectedContent = "# комментарий\r\nexample.com\r\nexample.org\r\n";
        Assert.Equal(expectedContent, viewModel.EditorContent);
        Assert.Equal(
            expectedContent,
            File.ReadAllText(Path.Combine(_tempDir, "lists", "list-general-user.txt")));
        var notification = Assert.Single(notifications);
        Assert.Equal("list-general-user.txt", notification.FileName);
        Assert.Equal(viewModel.EditorContent, notification.Content);
    }

    [Fact]
    public void TryLeave_Stay_KeepsUnsavedChanges()
    {
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { })
        {
            UnsavedChangesPrompt = () => HostlistUnsavedChangesDecision.Stay
        };

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");
        viewModel.EditorContent = "unsaved.example";

        Assert.False(viewModel.TryLeave());
        Assert.True(viewModel.HasChanges);
    }

    [Fact]
    public void SelectingAnotherFile_Stay_PreservesCurrentEdits()
    {
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { })
        {
            UnsavedChangesPrompt = () => HostlistUnsavedChangesDecision.Stay
        };

        viewModel.LoadHostlistFiles();
        var currentFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");
        viewModel.SelectedFile = currentFile;
        viewModel.EditorContent = "unsaved.example";

        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-exclude-user.txt");

        Assert.Same(currentFile, viewModel.SelectedFile);
        Assert.Equal("unsaved.example", viewModel.EditorContent);
        Assert.True(viewModel.HasChanges);
    }

    [Fact]
    public void TryLeave_Save_PersistsChangesAndAllowsLeaving()
    {
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { })
        {
            UnsavedChangesPrompt = () => HostlistUnsavedChangesDecision.Save
        };

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");
        viewModel.EditorContent = "https://saved.example/";

        Assert.True(viewModel.TryLeave());
        Assert.False(viewModel.HasChanges);
        Assert.Equal(
            "saved.example",
            File.ReadAllText(Path.Combine(_tempDir, "lists", "list-general-user.txt")));
    }

    /// <summary>
    /// Программное завершение (путь обновления приложения) не должно терять несохранённый буфер
    /// редактора: перед выходом правки записываются в файл, а владелец получает уведомление
    /// (Codex P2, ревью pullrequestreview-5191769205).
    /// </summary>
    [Fact]
    public void SavePendingEdits_WritesEditorBuffer_AndNotifiesOwner()
    {
        var notifications = new List<(string FileName, string Content)>();
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { },
            onSaved: (fileName, savedContent) => notifications.Add((fileName, savedContent)));

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");
        viewModel.EditorContent = "https://pending.example/";
        Assert.True(viewModel.HasChanges);

        Assert.True(viewModel.SavePendingEdits());

        Assert.False(viewModel.HasChanges);
        Assert.Contains(
            "pending.example",
            File.ReadAllText(Path.Combine(_tempDir, "lists", "list-general-user.txt")));
        Assert.Equal("list-general-user.txt", Assert.Single(notifications).FileName);
    }

    /// <summary>Без несохранённых правок завершение ничего не пишет и владельца не дёргает.</summary>
    [Fact]
    public void SavePendingEdits_WithoutChanges_DoesNothing()
    {
        var notifications = new List<(string FileName, string Content)>();
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { },
            onSaved: (fileName, savedContent) => notifications.Add((fileName, savedContent)));

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");

        Assert.True(viewModel.SavePendingEdits());
        Assert.Empty(notifications);
    }

    /// <summary>
    /// Если записать хостлист не удалось (файл занят или доступен только для чтения), завершение не
    /// должно терять буфер: правки кладутся рядом в файл .unsaved, а метод сообщает о неудаче
    /// (Codex P2, ревью pullrequestreview-5191807645).
    /// </summary>
    [Fact]
    public void SavePendingEdits_WhenFileIsLocked_PreservesBufferBesideFile_AndReportsFailure()
    {
        var logs = new List<string>();
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: logs.Add);

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-general-user.txt");

        var path = Path.Combine(_tempDir, "lists", "list-general-user.txt");
        using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            viewModel.EditorContent = "https://pending.example/";

            Assert.False(viewModel.SavePendingEdits());
        }

        var recoveryPath = path + ".unsaved";
        Assert.True(File.Exists(recoveryPath));
        var recovery = File.ReadAllText(recoveryPath);
        Assert.Contains("pending.example", recovery);
        Assert.Contains("list-general-user.txt", recovery);
        Assert.True(viewModel.HasChanges);
        Assert.Contains(logs, entry => entry.Contains("unsaved"));
    }

    [Fact]
    public void SaveCommand_ExcludeList_NotifiesOwnerWithUpdatedContent()
    {
        var notifications = new List<(string FileName, string Content)>();
        var viewModel = new HostlistsViewModel(
            getEngineDir: () => _tempDir,
            addLog: _ => { },
            onSaved: (fileName, savedContent) => notifications.Add((fileName, savedContent)));

        viewModel.LoadHostlistFiles();
        viewModel.SelectedFile = viewModel.Files
            .Single(file => file.FileName == "list-exclude-user.txt");
        viewModel.EditorContent = "https://exclude.example/\r\n";

        viewModel.SaveCommand.Execute(null);

        var expectedContent = "exclude.example\r\n";
        Assert.Equal(expectedContent, viewModel.EditorContent);
        var notification = Assert.Single(notifications);
        Assert.Equal("list-exclude-user.txt", notification.FileName);
        Assert.Equal(viewModel.EditorContent, notification.Content);
    }
}
