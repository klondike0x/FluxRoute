using System.Net;
using System.Net.NetworkInformation;
using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class WindowsDohConfigurationServiceTests
{
    [Fact]
    public async Task ApplyAsync_PersistsCompleteJournalBeforeFirstMutation_AndClearsItAfterSuccess()
    {
        var order = new List<string>();
        var runner = CreateSuccessfulRunner();
        runner.Setup(x => x.RunAsync("netsh.exe",
                It.Is<IReadOnlyList<string>>(a => a.Contains("set")), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("mutation"))
            .ReturnsAsync(new ProcessRunResult(0, "OK", ""));
        var snapshot = new DnsAdapterSnapshot("Ethernet", ["192.0.2.53", "2001:db8::53"], false,
            "{2E61C098-C635-4D56-AB43-E41ED7033B99}",
            new DnsFamilySnapshot(false, ["192.0.2.53"]),
            new DnsFamilySnapshot(true, ["2001:db8::53"]));
        var network = CreateNetworkService(snapshot);
        var system = CreateSystemConfiguration();
        system.Setup(x => x.CaptureSystemStateAsync(snapshot.InterfaceId,
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohSystemStateSnapshot(DohGlobalMode.Disabled,
                [new("1.1.1.1", true, "https://old.test/dns-query", true, false)],
                [new("1.1.1.1", false, true, "https://old.test/dns-query", 17)]));
        var settings = CreateSettingsService(order);
        var service = CreateService(runner, network, system, settings);

        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        Assert.True(result.IsSuccess);
        Assert.Equal("journal", order.First());
        var saves = settings.Invocations.Where(i => i.Method.Name == nameof(ISettingsService.Save))
            .Select(i => (AppSettings)i.Arguments[0]).ToArray();
        var journal = Assert.IsType<DohOperationJournal>(saves.First().Doh.OperationJournal);
        Assert.Equal(snapshot.Ipv4State, journal.Ipv4);
        Assert.Equal(snapshot.Ipv6State, journal.Ipv6);
        Assert.Equal(DohGlobalMode.Disabled, journal.GlobalDohMode);
        Assert.Single(journal.ResolverMappings);
        Assert.Single(journal.InterfaceMappings);
        Assert.Null(saves.Last().Doh.OperationJournal);
    }

    [Fact]
    public async Task ApplyAsync_WhenMutationFails_RestoresEntireJournalWithNonCancelledToken()
    {
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], false));
        var system = CreateSystemConfiguration();
        system.Setup(x => x.ConfigureAsync(It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "boom"));
        var settings = CreateSettingsService();
        var service = CreateService(runner, network, system, settings);

        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        Assert.False(result.IsSuccess);
        system.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.Addresses.Contains("192.0.2.53")),
            CancellationToken.None), Times.Once);
        Assert.Null(settings.Object.Load().Doh.OperationJournal);
    }

    [Fact]
    public async Task ApplyAsync_WhenEarlyFailureRollbackFails_KeepsJournalAndReportsFailure()
    {
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], false));
        var system = CreateSystemConfiguration();
        system.Setup(x => x.ConfigureAsync(It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "apply failed"));
        system.Setup(x => x.RestoreSystemStateAsync(It.IsAny<DohOperationJournal>(), CancellationToken.None))
            .ReturnsAsync(new DohApplyResult(false, "rollback failed"));
        var settings = CreateSettingsService();
        var service = CreateService(runner, network, system, settings);

        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        Assert.False(result.IsSuccess);
        Assert.Contains("rollback failed", result.Message);
        Assert.NotNull(settings.Object.Load().Doh.OperationJournal);
    }

    [Fact]
    public async Task ApplyAsync_WhenExceptionRollbackSucceeds_ClearsJournalBeforeRethrow()
    {
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], false));
        var system = CreateSystemConfiguration();
        system.Setup(x => x.ConfigureAsync(It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var settings = CreateSettingsService();
        var service = CreateService(runner, network, system, settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync("Ethernet", CreateProvider()));

        Assert.Null(settings.Object.Load().Doh.OperationJournal);
    }

    [Fact]
    public async Task ApplyAsync_WithPendingJournal_RecoversItBeforeStartingNewTransaction()
    {
        var pending = new DohOperationJournal { InterfaceName = "Ethernet", InterfaceId = "old" };
        var settings = CreateSettingsService(initialJournal: pending);
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], false));
        var system = CreateSystemConfiguration();
        var service = CreateService(runner, network, system, settings);

        Assert.True((await service.ApplyAsync("Ethernet", CreateProvider())).IsSuccess);

        system.Verify(x => x.RestoreSystemStateAsync(pending, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DisableAsync_WithPendingJournal_RecoversItBeforeStartingNewTransaction()
    {
        var pending = new DohOperationJournal { InterfaceName = "Ethernet", InterfaceId = "old" };
        var settings = CreateSettingsService(initialJournal: pending);
        var system = CreateSystemConfiguration();
        var service = CreateService(CreateSuccessfulRunner(),
            CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["1.1.1.1"], false)), system, settings);

        Assert.True((await service.DisableAsync("Ethernet")).IsSuccess);
        system.Verify(x => x.RestoreSystemStateAsync(pending, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RecoverPendingOperationAsync_RestoresJournalAndClearsIt()
    {
        var pending = new DohOperationJournal { InterfaceName = "Ethernet", InterfaceId = "old" };
        var settings = CreateSettingsService(initialJournal: pending);
        var system = CreateSystemConfiguration();
        var service = CreateService(CreateSuccessfulRunner(),
            CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["1.1.1.1"], false)), system, settings);

        Assert.True((await service.RecoverPendingOperationAsync()).IsSuccess);
        Assert.Null(settings.Object.Load().Doh.OperationJournal);
    }
    [Fact]
    public async Task ApplyAsync_UnencryptedMode_DisablesAutomaticUpgradeThroughNetsh()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);

        // Act
        var result = await service.ApplyAsync("Ethernet", CreateProvider(), DohEncryptionMode.Unencrypted);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("autoupgrade=no")
                && args.Contains("udpfallback=yes")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenEncryptedMode_EnablesGlobalDohAndVerifiesEncryptionMapping()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        runner.Setup(x => x.RunAsync(
                "netsh.exe",
                It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
                    new[] { "dnsclient", "show", "encryption", "server=1.1.1.1" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "https://cloudflare-dns.com/dns-query\nАвтоматическое обновление : yes\nРезерв UDP : no",
                ""));
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);

        // Act
        var result = await service.ApplyAsync(
            "Ethernet", CreateProvider(), DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
                new[] { "dnsclient", "set", "global", "doh=auto" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisableAsync_AlwaysReturnsAdapterDnsToDhcp()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["192.0.2.53"], IsDhcp: false));
        var system = CreateSystemConfiguration();
        var service = CreateService(runner, network, system);

        // Act
        var result = await service.DisableAsync("Wi-Fi");

        // Assert
        Assert.True(result.IsSuccess);
        system.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.IsDhcp && j.Ipv6.IsDhcp
                && j.GlobalDohMode == DohGlobalMode.Disabled),
            It.IsAny<CancellationToken>()), Times.Once);
        system.Verify(x => x.VerifySystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.IsDhcp && j.Ipv6.IsDhcp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenStructuredVerificationFails_RollsBackDns()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var systemConfiguration = CreateSystemConfiguration();
        systemConfiguration.Setup(x => x.VerifyAsync(
                It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(runner, network, systemConfiguration);

        // Act
        var result = await service.ApplyAsync(
            "Ethernet", CreateProvider(), DohEncryptionMode.EncryptedOnly);

        // Assert
        Assert.False(result.IsSuccess);
        systemConfiguration.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.Addresses.Contains("192.0.2.53")),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenCommandsSucceed_ProbesCapabilityAndConfiguresAdapter()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);

        // Act
        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        // Assert
        Assert.True(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "dnsclient", "show", "encryption" })),
            It.IsAny<CancellationToken>()), Times.Once);

    }

    [Fact]
    public async Task ApplyAsync_WhenCapabilityProbeFails_DoesNotMutateConfiguration()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(
                "netsh.exe",
                It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(new[] { "dnsclient", "show", "encryption" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "Команда не найдена"));
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);

        // Act
        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        // Assert
        Assert.False(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("set") && args.Contains("encryption")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_WhenAdapterVerificationFails_RestoresPreviousStaticDns()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53", "192.0.2.54"], IsDhcp: false));
        var systemConfiguration = CreateSystemConfiguration();
        systemConfiguration.Setup(x => x.VerifyAdapterAsync(
                "Ethernet", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(runner, network, systemConfiguration);

        // Act
        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        // Assert
        Assert.False(result.IsSuccess);
        systemConfiguration.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.Addresses.SequenceEqual(new[] { "192.0.2.53", "192.0.2.54" })),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_WhenEncryptionRegistrationFails_DoesNotChangeAdapterDns()
    {
        // Arrange
        var runner = new Mock<IProcessRunner>();
        runner.SetupSequence(x => x.RunAsync("netsh.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "OK", ""))
            .ReturnsAsync(new ProcessRunResult(1, "", "Access denied"));
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);

        // Act
        var result = await service.ApplyAsync("Ethernet", CreateProvider());

        // Assert
        Assert.False(result.IsSuccess);
        runner.Verify(x => x.RunAsync(
            "netsh.exe",
            It.Is<IReadOnlyList<string>>(args => args.Contains("dnsservers")),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisableAsync_ClearsInterfaceRecords_AndRestoresEverythingWhenIpv6ResetFails()
    {
        var runner = CreateSuccessfulRunner();
        // Полное применение отключённого состояния завершается ошибкой.

        var network = CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["1.1.1.1", "2606:4700:4700::1111"], false));
        var system = CreateSystemConfiguration();
        system.SetupSequence(x => x.RestoreSystemStateAsync(
                It.IsAny<DohOperationJournal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "ipv6 failed"))
            .ReturnsAsync(new DohApplyResult(true, "rollback ok"));
        var service = CreateService(runner, network, system);

        var result = await service.DisableAsync("Wi-Fi");

        Assert.False(result.IsSuccess);
        system.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.IsDhcp && j.Ipv6.IsDhcp),
            It.IsAny<CancellationToken>()), Times.Once);
        system.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => !j.Ipv4.IsDhcp || !j.Ipv6.IsDhcp),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DisableAsync_WhenEarlyFailureRollbackSucceeds_ClearsJournal()
    {
        var system = CreateSystemConfiguration();
        system.SetupSequence(x => x.RestoreSystemStateAsync(It.IsAny<DohOperationJournal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "disable failed"))
            .ReturnsAsync(new DohApplyResult(true, "rollback ok"));
        var settings = CreateSettingsService();
        var service = CreateService(CreateSuccessfulRunner(),
            CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["1.1.1.1"], false)), system, settings);

        var result = await service.DisableAsync("Wi-Fi");

        Assert.False(result.IsSuccess);
        Assert.Null(settings.Object.Load().Doh.OperationJournal);
    }

    [Fact]
    public async Task DisableAsync_WhenEarlyFailureRollbackFails_KeepsJournalAndReportsFailure()
    {
        var system = CreateSystemConfiguration();
        system.SetupSequence(x => x.RestoreSystemStateAsync(It.IsAny<DohOperationJournal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "disable failed"))
            .ReturnsAsync(new DohApplyResult(false, "rollback failed"));
        var settings = CreateSettingsService();
        var service = CreateService(CreateSuccessfulRunner(),
            CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["1.1.1.1"], false)), system, settings);

        var result = await service.DisableAsync("Wi-Fi");

        Assert.False(result.IsSuccess);
        Assert.Contains("rollback failed", result.Message);
        Assert.NotNull(settings.Object.Load().Doh.OperationJournal);
    }

    [Fact]
    public async Task DisableAsync_ResetsIpv4AndIpv6ToDhcp()
    {
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["192.0.2.53"], IsDhcp: false));
        var system = CreateSystemConfiguration();
        var service = CreateService(runner, network, system);

        Assert.True((await service.DisableAsync("Wi-Fi")).IsSuccess);
        system.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.IsDhcp && j.Ipv6.IsDhcp),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisableAsync_WhenPreviousSnapshotExists_ReturnsDhcp()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["192.0.2.53", "192.0.2.54"], IsDhcp: false));
        var service = CreateService(runner, network);
        var apply = await service.ApplyAsync("Wi-Fi", CreateProvider());
        Assert.True(apply.IsSuccess);

        // Act
        var result = await service.DisableAsync("Wi-Fi");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("DHCP", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableAsync_WhenPreviousSnapshotUsedDhcp_RestoresDhcpMode()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Wi-Fi", ["192.0.2.53"], IsDhcp: true));
        var service = CreateService(runner, network);
        Assert.True((await service.ApplyAsync("Wi-Fi", CreateProvider())).IsSuccess);

        // Act
        var result = await service.DisableAsync("Wi-Fi");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("DHCP", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_WhenRepeated_DisableReturnsDhcp()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var service = CreateService(runner, network);
        Assert.True((await service.ApplyAsync("Ethernet", CreateProvider())).IsSuccess);
        network.Setup(x => x.CaptureAsync("Ethernet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DnsAdapterSnapshot("Ethernet", ["1.1.1.1"], IsDhcp: false));
        Assert.True((await service.ApplyAsync("Ethernet", CreateProvider())).IsSuccess);

        // Act
        var result = await service.DisableAsync("Ethernet");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("DHCP", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_WhenCancelledAfterDnsMutation_RollsBackWithNonCancelledToken()
    {
        // Arrange
        var runner = CreateSuccessfulRunner();
        var cts = new CancellationTokenSource();
        runner.Setup(x => x.RunAsync(
                "ipconfig.exe", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var network = CreateNetworkService(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], IsDhcp: false));
        var systemConfiguration = CreateSystemConfiguration();
        var service = CreateService(runner, network, systemConfiguration);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.ApplyAsync(
                "Ethernet", CreateProvider(), DohEncryptionMode.EncryptedOnly, cts.Token));
        systemConfiguration.Verify(x => x.RestoreSystemStateAsync(
            It.Is<DohOperationJournal>(j => j.Ipv4.Addresses.Contains("192.0.2.53")),
            CancellationToken.None), Times.Once);
    }

    private static Mock<IProcessRunner> CreateSuccessfulRunner()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(x => x.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "OK", ""));
        runner.Setup(x => x.RunAsync(
                "netsh.exe",
                It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(
                    new[] { "dnsclient", "show", "encryption", "server=1.1.1.1" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(
                0,
                "https://cloudflare-dns.com/dns-query\nАвтоматическое обновление : yes\nРезерв UDP : no",
                ""));
        return runner;
    }

    private static Mock<IDnsAdapterService> CreateNetworkService(DnsAdapterSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.InterfaceId))
        {
            snapshot = snapshot with { InterfaceId = "{2E61C098-C635-4D56-AB43-E41ED7033B99}" };
        }

        var network = new Mock<IDnsAdapterService>();
        network.Setup(x => x.CaptureAsync(snapshot.InterfaceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        network.Setup(x => x.VerifyDnsAddressesAsync(
                snapshot.InterfaceName, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return network;
    }

    private static Mock<IDohSystemConfigurationService> CreateSystemConfiguration()
    {
        var systemConfiguration = new Mock<IDohSystemConfigurationService>();
        systemConfiguration.Setup(x => x.ConfigureAsync(
                It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "OK"));
        systemConfiguration.Setup(x => x.VerifyAsync(
                It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        systemConfiguration.Setup(x => x.ConfigureAdapterAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "OK"));
        systemConfiguration.Setup(x => x.ConfigureInterfaceModeAsync(
                It.IsAny<string>(), It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "OK"));
        systemConfiguration.Setup(x => x.VerifyAdapterAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        systemConfiguration.Setup(x => x.VerifyInterfaceModeAsync(
                It.IsAny<string>(), It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        systemConfiguration.Setup(x => x.CaptureSystemStateAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohSystemStateSnapshot(DohGlobalMode.Disabled, [], []));
        systemConfiguration.Setup(x => x.RestoreSystemStateAsync(
                It.IsAny<DohOperationJournal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "OK"));
        systemConfiguration.Setup(x => x.VerifySystemStateAsync(
                It.IsAny<DohOperationJournal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return systemConfiguration;
    }

    private static WindowsDohConfigurationService CreateService(
        Mock<IProcessRunner> runner,
        Mock<IDnsAdapterService> network,
        Mock<IDohSystemConfigurationService>? systemConfiguration = null,
        Mock<ISettingsService>? settings = null)
    {
        systemConfiguration ??= CreateSystemConfiguration();
        settings ??= CreateSettingsService();
        return new WindowsDohConfigurationService(
            runner.Object,
            network.Object,
            systemConfiguration.Object,
            settings.Object,
            new Mock<ILogger<WindowsDohConfigurationService>>().Object,
            isWindows: () => true,
            isAdministrator: () => true);
    }

    private static Mock<ISettingsService> CreateSettingsService(
        List<string>? order = null,
        DohOperationJournal? initialJournal = null)
    {
        var current = new AppSettings { Doh = new DohSettings { OperationJournal = initialJournal } };
        var settings = new Mock<ISettingsService>();
        settings.Setup(x => x.Load()).Returns(() => current);
        settings.Setup(x => x.Save(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(value =>
            {
                if (value.Doh.OperationJournal is not null)
                {
                    order?.Add("journal");
                }
                current = new AppSettings
                {
                    Doh = new DohSettings { OperationJournal = value.Doh.OperationJournal }
                };
            });
        return settings;
    }

    private static DohProvider CreateProvider() => new(
        "cloudflare",
        "Cloudflare",
        new Uri("https://cloudflare-dns.com/dns-query"),
        ["1.1.1.1"],
        true);
}


