namespace FluxRoute.Core.Models;

public sealed class ProfileItem
{
    public string FileName { get; init; } = "";   // например: general (ALT3).bat
    public string DisplayName { get; init; } = ""; // например: general (ALT3)
    public string FullPath { get; init; } = "";    // полный путь

    /// <summary>Признак того, что профиль — это мод (из mods/), а не обычная стратегия.</summary>
    public bool IsMod { get; init; }

    /// <summary>Папка мода в mods/ (для IsMod=true).</summary>
    public string? ModFolder { get; init; }
}
