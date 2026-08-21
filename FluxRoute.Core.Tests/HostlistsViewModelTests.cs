using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class HostlistsViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public HostlistsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FluxRouteHostlistsTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
        viewModel.EditorContent = "# комментарий\r\nexample.com\r\nexample.org\r\n";

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(
            viewModel.EditorContent,
            File.ReadAllText(Path.Combine(_tempDir, "lists", "list-general-user.txt")));
        var notification = Assert.Single(notifications);
        Assert.Equal("list-general-user.txt", notification.FileName);
        Assert.Equal(viewModel.EditorContent, notification.Content);
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
        viewModel.EditorContent = "exclude.example\r\n";

        viewModel.SaveCommand.Execute(null);

        var notification = Assert.Single(notifications);
        Assert.Equal("list-exclude-user.txt", notification.FileName);
        Assert.Equal(viewModel.EditorContent, notification.Content);
    }
}
