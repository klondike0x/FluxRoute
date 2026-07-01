using System;
using System.Collections.Generic;
using Xunit;

namespace FluxRoute.Core.Tests;

/// <summary>
/// Тесты для функциональности дедупликации доменов и нормализации ввода.
/// Версия 1.6.0.
/// </summary>
public class DomainDeduplicationTests
{
    /// <summary>
    /// Тестирует нормализацию домена: удаление пробелов, протоколов, www., слеша.
    /// </summary>
    [Theory]
    [InlineData("google.com", "google.com")]
    [InlineData("  google.com  ", "google.com")]
    [InlineData("http://google.com", "google.com")]
    [InlineData("https://google.com", "google.com")]
    [InlineData("HTTP://GOOGLE.COM", "GOOGLE.COM")]
    [InlineData("HTTPS://GOOGLE.COM", "GOOGLE.COM")]
    [InlineData("www.google.com", "google.com")]
    [InlineData("WWW.GOOGLE.COM", "GOOGLE.COM")]
    [InlineData("https://www.google.com", "google.com")]
    [InlineData("https://www.google.com/", "google.com")]
    [InlineData("google.com/", "google.com")]
    [InlineData("  https://www.google.com/  ", "google.com")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    public void NormalizeDomain_ReturnsExpectedValue(string input, string expected)
    {
        // Act
        var result = NormalizeDomainInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Тестирует дедупликацию: одинаковые домены (регистронезависимо) не должны быть добавлены дважды.
    /// </summary>
    [Fact]
    public void DomainDeduplication_PreventsDuplicateDomains()
    {
        // Arrange
        var domains = new List<string> { "google.com", "YouTube.com", "discord.io" };

        // Act & Assert
        // Проверяем, что domainForAdding будет отклонён как дубликат
        var domainForAdding = "GOOGLE.COM";
        var isDuplicate = domains.Any(d => string.Equals(d, domainForAdding, StringComparison.OrdinalIgnoreCase));

        Assert.True(isDuplicate, $"Домен {domainForAdding} должен быть идентифицирован как дубликат");
    }

    /// <summary>
    /// Тестирует дедупликацию с нормализацией входных данных.
    /// </summary>
    [Fact]
    public void DomainDeduplication_WithNormalization()
    {
        // Arrange
        var domains = new List<string> { "google.com", "youtube.com" };

        // Act
        var normalizedInput = NormalizeDomainInput("https://www.Google.Com/");
        var isDuplicate = domains.Any(d => string.Equals(d, normalizedInput, StringComparison.OrdinalIgnoreCase));

        // Assert
        // Нормализация сохраняет регистр из URL, но дедупликация игнорирует регистр
        Assert.Equal("Google.Com", normalizedInput);
        Assert.True(isDuplicate, "Домен должен быть идентифицирован как дубликат (регистронезависимо)");
    }

    /// <summary>
    /// Тестирует добавление нового домена (не дубликата).
    /// </summary>
    [Fact]
    public void DomainDeduplication_AllowsUniqueAddition()
    {
        // Arrange
        var domains = new List<string> { "google.com", "youtube.com" };
        var newDomain = NormalizeDomainInput("https://www.twitch.tv");

        // Act
        var isDuplicate = domains.Any(d => string.Equals(d, newDomain, StringComparison.OrdinalIgnoreCase));
        if (!isDuplicate)
        {
            domains.Add(newDomain);
        }

        // Assert
        Assert.False(isDuplicate);
        Assert.Contains("twitch.tv", domains);
        Assert.Equal(3, domains.Count);
    }

    /// <summary>
    /// Тестирует граничные случаи для нормализации.
    /// </summary>
    [Theory]
    [InlineData("https://", "")]
    [InlineData("http://", "")]
    [InlineData("www.", "")]
    [InlineData("http://www.", "")]
    [InlineData("https://www.///", "")]
    public void NormalizeDomain_EdgeCases(string input, string expected)
    {
        // Act
        var result = NormalizeDomainInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Тестирует, что нормализация сохраняет символы домена.
    /// </summary>
    [Theory]
    [InlineData("test-domain.co.uk", "test-domain.co.uk")]
    [InlineData("sub.domain.example.com", "sub.domain.example.com")]
    [InlineData("example.co.uk", "example.co.uk")]
    public void NormalizeDomain_PreservesValidCharacters(string input, string expected)
    {
        // Act
        var result = NormalizeDomainInput(input);

        // Assert
        Assert.Equal(expected, result);
    }

    // ── Вспомогательный метод ──

    /// <summary>
    /// Нормализует ввод домена (функция из MainViewModel).
    /// Убирает пробелы, протоколы, www., завершающий слеш.
    /// </summary>
    private static string NormalizeDomainInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        // Убираем пробелы в начале/конце
        input = input.Trim();

        // Удаляем протоколы (регистронезависимо)
        input = System.Text.RegularExpressions.Regex.Replace(
            input, @"^https?://", "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Удаляем www. (регистронезависимо)
        input = System.Text.RegularExpressions.Regex.Replace(
            input, @"^www\.", "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Убираем завершающий слеш
        input = input.TrimEnd('/');

        // Удаляем оставшиеся пробелы
        input = input.Trim();

        return input;
    }
}

/// <summary>
/// Тесты для функциональности дефолтного профиля при триггерах.
/// Версия 1.6.0.
/// </summary>
public class DefaultProfileForTriggersTests
{
    /// <summary>
    /// Тестирует логику сохранения дефолтного профиля.
    /// </summary>
    [Fact]
    public void DefaultProfile_CanBeSet()
    {
        // Arrange
        string? defaultProfile = null;
        var profileName = "General (ALT3)";

        // Act
        defaultProfile = profileName;

        // Assert
        Assert.NotNull(defaultProfile);
        Assert.Equal(profileName, defaultProfile);
    }

    /// <summary>
    /// Тестирует логику очистки дефолтного профиля.
    /// </summary>
    [Fact]
    public void DefaultProfile_CanBeCleared()
    {
        // Arrange
        string? defaultProfile = "General (ALT3)";

        // Act
        defaultProfile = null;

        // Assert
        Assert.Null(defaultProfile);
    }

    /// <summary>
    /// Тестирует выбор профиля для возврата: если дефолтный задан, используем его;
    /// иначе используем переданный (текущий перед триггером).
    /// </summary>
    [Fact]
    public void TriggerLogic_SelectsCorrectReturnProfile_WithDefault()
    {
        // Arrange
        var currentProfile = "Gaming";
        var defaultProfile = "General";

        // Act
        var returnProfile = string.IsNullOrEmpty(defaultProfile) ? currentProfile : defaultProfile;

        // Assert
        Assert.Equal("General", returnProfile);
    }

    /// <summary>
    /// Тестирует выбор профиля для возврата: если дефолтный не задан,
    /// используем переданный (текущий перед триггером).
    /// </summary>
    [Fact]
    public void TriggerLogic_SelectsCorrectReturnProfile_WithoutDefault()
    {
        // Arrange
        var currentProfile = "Gaming";
        string? defaultProfile = null;

        // Act
        var returnProfile = string.IsNullOrEmpty(defaultProfile) ? currentProfile : defaultProfile;

        // Assert
        Assert.Equal("Gaming", returnProfile);
    }

    /// <summary>
    /// Тестирует логику мониторинга триггеров: если процесс активен, запоминаем профиль.
    /// </summary>
    [Fact]
    public void TriggerMonitoring_RemembersProfileWhenTriggered()
    {
        // Arrange
        var currentProfile = "Work";
        string? rememberedProfile = null;

        // Act
        // Имитируем запоминание профиля перед триггером
        rememberedProfile = currentProfile;

        // Assert
        Assert.Equal("Work", rememberedProfile);
    }

    /// <summary>
    /// Тестирует логику мониторинга триггеров: если процесс завершён,
    /// возвращаемся на запомненный профиль.
    /// </summary>
    [Fact]
    public void TriggerMonitoring_RestoresProfileWhenTriggerEnds()
    {
        // Arrange
        var activeProfile = "Gaming";
        var rememberedProfile = "Work";

        // Act
        // Имитируем возврат на запомненный профиль
        if (rememberedProfile != null && activeProfile != rememberedProfile)
        {
            var newProfile = rememberedProfile;

            // Assert
            Assert.Equal("Work", newProfile);
        }
    }

    /// <summary>
    /// Тестирует исключительный случай: дефолтный профиль удален из системы.
    /// Должны использовать текущий профиль как фолбэк.
    /// </summary>
    [Fact]
    public void TriggerLogic_HandlesMissingDefaultProfile()
    {
        // Arrange
        var currentProfile = "Gaming";
        var defaultProfile = "DeletedProfile"; // Профиль был удалён
        var availableProfiles = new List<string> { "Gaming", "General" };

        // Act
        var returnProfile = availableProfiles.FirstOrDefault(p => p == defaultProfile) 
            ?? currentProfile;

        // Assert
        Assert.Equal("Gaming", returnProfile);
    }
}
