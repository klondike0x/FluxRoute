namespace FluxRoute.ViewModels;

/// <summary>
/// Сравнивает версии компонентов независимо от префикса v и регистра.
/// </summary>
public static class UpdateVersionComparer
{
    public static bool AreEqual(string? local, string? latest)
    {
        var normalizedLocal = Normalize(local);
        var normalizedLatest = Normalize(latest);
        return normalizedLocal.Length > 0
            && normalizedLatest.Length > 0
            && string.Equals(normalizedLocal, normalizedLatest, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Trim() == "—")
            return string.Empty;

        return version.Trim().TrimStart('v', 'V').Trim();
    }
}
