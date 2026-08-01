// ═══ v1.7.0: Модели Flowseal и Zapret2 ═══
namespace FluxRoute.Core.Models;

public sealed record FlowsealVersion(string Version, DateTimeOffset? ReleaseDate = null, string? DownloadUrl = null, string? Source = null, long? SizeBytes = null, string? ReleaseNotes = null);
public sealed record FlowsealInstallResult(bool Success, string Version, string? Error = null, bool RolledBack = false);
public sealed record Zapret2Capability(bool CanRun, bool WinDivertAvailable = false, string? WinDivertVersion = null, bool HasAdminPrivileges = false, string? OsArchitecture = null, string? OsVersion = null, IReadOnlyList<string>? Warnings = null, IReadOnlyList<string>? Errors = null);
