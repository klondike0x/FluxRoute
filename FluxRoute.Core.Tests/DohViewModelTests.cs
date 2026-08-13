using FluxRoute.Core.Models;
using FluxRoute.Core.Services;
using FluxRoute.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace FluxRoute.Core.Tests;

public sealed class DohViewModelTests
{
    [Fact]
    public async Task AutomaticOperations_AreSerialized_MaxConcurrencyIsOne()
    {
        var harness = new Harness(enabled: true);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrent = 0;
        var maximum = 0;
        harness.Configuration.Setup(x => x.ApplyAsync("Ethernet", It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref concurrent));
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                Interlocked.Decrement(ref concurrent);
                return new DohApplyResult(true, "applied");
            });
        var vm = harness.Create();

        vm.EncryptionMode = DohEncryptionMode.EncryptedWithFallback;
        await firstStarted.Task;
        vm.EncryptionMode = DohEncryptionMode.Unencrypted;
        releaseFirst.TrySetResult();
        await vm.WaitForPendingOperationAsync();

        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task NewGeneration_PreventsStaleApplyFromOverwritingState()
    {
        var harness = new Harness(enabled: true);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        harness.Configuration.Setup(x => x.ApplyAsync("Ethernet", It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task;
                    return new DohApplyResult(false, "stale failure");
                }
                return new DohApplyResult(true, "fresh success");
            });
        var vm = harness.Create();

        vm.EncryptionMode = DohEncryptionMode.EncryptedWithFallback;
        await firstStarted.Task;
        vm.EncryptionMode = DohEncryptionMode.Unencrypted;
        releaseFirst.TrySetResult();
        await vm.WaitForPendingOperationAsync();

        Assert.Equal("fresh success", vm.Status);
        Assert.True(vm.IsDohEnabled);
        Assert.Equal(DohEncryptionMode.Unencrypted, harness.Settings.Object.Load().Doh.EncryptionMode);
    }

    [Fact]
    public async Task ApplyCancelCommand_CancelsTokenPassedToService()
    {
        var harness = new Harness(enabled: false);
        var tokenObserved = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Configuration.Setup(x => x.ApplyAsync("Ethernet", It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
            .Returns<string, DohProvider, DohEncryptionMode, CancellationToken>(async (_, _, _, ct) =>
            {
                tokenObserved.TrySetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new DohApplyResult(true, "unreachable");
            });
        var vm = harness.Create();

        var operation = vm.ApplyCommand.ExecuteAsync(null);
        var token = await tokenObserved.Task;
        vm.ApplyCancelCommand.Execute(null);
        await operation;

        Assert.True(token.IsCancellationRequested);
        Assert.Contains("отменено", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackgroundSwitchFault_IsObserved_AndDisablesUnsafeToggle()
    {
        var harness = new Harness(enabled: true);
        harness.Switch.Setup(x => x.SwitchAsync("Ethernet", It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), It.IsAny<DohProvider>(), It.IsAny<DohEncryptionMode>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("restore failed"));
        var vm = harness.Create();

        vm.SelectedProvider = harness.Providers[1];
        await vm.WaitForPendingOperationAsync();

        Assert.False(vm.IsDohEnabled);
        Assert.False(harness.Current.Doh.Enabled);
        Assert.Null(harness.Current.Doh.AppliedProviderId);
        Assert.Empty(harness.Current.Doh.AppliedDnsAddresses);
        Assert.Contains("откат", vm.Status, StringComparison.OrdinalIgnoreCase);
        harness.Logger.VerifyLog(LogLevel.Error, Times.AtLeastOnce());
    }

    [Fact]
    public async Task StartupSwitch_UsesPersistedAppliedProviderAndMode_NotUiSelection()
    {
        var harness = new Harness(enabled: true, selectedProviderId: "new",
            appliedProviderId: "old", selectedMode: DohEncryptionMode.Unencrypted,
            appliedMode: DohEncryptionMode.EncryptedWithFallback);
        harness.Switch.Setup(x => x.SwitchAsync("Ethernet", harness.Providers[0],
                DohEncryptionMode.EncryptedWithFallback, harness.Providers[2],
                DohEncryptionMode.Unencrypted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "applied"));
        var vm = harness.Create();

        vm.SelectedProvider = harness.Providers[2];
        await vm.WaitForPendingOperationAsync();

        harness.Switch.VerifyAll();
    }

    [Fact]
    public async Task SuccessfulSwitch_PersistsAppliedProviderAndMode()
    {
        var harness = new Harness(enabled: true);
        harness.Switch.Setup(x => x.SwitchAsync(It.IsAny<string>(), It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), harness.Providers[1], DohEncryptionMode.EncryptedOnly,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(true, "applied", DohProviderSwitchOutcome.AppliedNew));
        var vm = harness.Create();

        vm.SelectedProvider = harness.Providers[1];
        await vm.WaitForPendingOperationAsync();

        Assert.Equal("new", harness.Current.Doh.AppliedProviderId);
        Assert.Equal(DohEncryptionMode.EncryptedOnly, harness.Current.Doh.AppliedEncryptionMode);
    }

    [Fact]
    public async Task RestoredPrevious_RestoresActuallyAppliedProviderAndMode()
    {
        var harness = new Harness(enabled: true, selectedProviderId: "new",
            appliedProviderId: "old", selectedMode: DohEncryptionMode.Unencrypted,
            appliedMode: DohEncryptionMode.EncryptedWithFallback);
        harness.Switch.Setup(x => x.SwitchAsync(It.IsAny<string>(), harness.Providers[0],
                DohEncryptionMode.EncryptedWithFallback, harness.Providers[2], DohEncryptionMode.Unencrypted,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "restored", DohProviderSwitchOutcome.RestoredPrevious));
        var vm = harness.Create();

        vm.SelectedProvider = harness.Providers[2];
        await vm.WaitForPendingOperationAsync();

        Assert.Same(harness.Providers[0], vm.SelectedProvider);
        Assert.Equal(DohEncryptionMode.EncryptedWithFallback, vm.EncryptionMode);
        Assert.True(vm.IsDohEnabled);
        Assert.Equal("old", harness.Current.Doh.SelectedProviderId);
        Assert.Equal("old", harness.Current.Doh.AppliedProviderId);
        Assert.Equal(DohEncryptionMode.EncryptedWithFallback, harness.Current.Doh.EncryptionMode);
        Assert.Equal(DohEncryptionMode.EncryptedWithFallback, harness.Current.Doh.AppliedEncryptionMode);
    }

    [Fact]
    public async Task RestoreFailed_DisablesToggleAndPersistedState()
    {
        var harness = new Harness(enabled: true);
        harness.Switch.Setup(x => x.SwitchAsync(It.IsAny<string>(), It.IsAny<DohProvider>(),
                It.IsAny<DohEncryptionMode>(), harness.Providers[1], It.IsAny<DohEncryptionMode>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DohApplyResult(false, "restore failed", DohProviderSwitchOutcome.RestoreFailed));
        var vm = harness.Create();

        vm.SelectedProvider = harness.Providers[1];
        await vm.WaitForPendingOperationAsync();

        Assert.False(vm.IsDohEnabled);
        Assert.False(harness.Current.Doh.Enabled);
        Assert.Null(harness.Current.Doh.AppliedProviderId);
        Assert.Empty(harness.Current.Doh.AppliedDnsAddresses);
        Assert.Contains("restore failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Harness
    {
        public readonly DohProvider[] Providers =
        [
            new("old", "Old", new Uri("https://old.test/dns-query"), ["1.1.1.1"], true),
            new("new", "New", new Uri("https://new.test/dns-query"), ["8.8.8.8"], true),
            new("third", "Third", new Uri("https://third.test/dns-query"), ["9.9.9.9"], true)
        ];
        public Mock<IWindowsDohConfigurationService> Configuration { get; } = new();
        public Mock<IDohProviderSwitchService> Switch { get; } = new();
        public Mock<ISettingsService> Settings { get; } = new();
        public Mock<ILogger<DohViewModel>> Logger { get; } = new();
        private AppSettings _settings;
        public AppSettings Current => _settings;

        public Harness(
            bool enabled,
            string selectedProviderId = "old",
            string appliedProviderId = "old",
            DohEncryptionMode selectedMode = DohEncryptionMode.EncryptedOnly,
            DohEncryptionMode appliedMode = DohEncryptionMode.EncryptedOnly)
        {
            _settings = new AppSettings
            {
                Doh = new DohSettings
                {
                    Enabled = enabled,
                    InterfaceName = "Ethernet",
                    SelectedProviderId = selectedProviderId,
                    AppliedProviderId = appliedProviderId,
                    EncryptionMode = selectedMode,
                    AppliedEncryptionMode = appliedMode
                }
            };
            Settings.Setup(x => x.Load()).Returns(() => _settings);
            Settings.Setup(x => x.Save(It.IsAny<AppSettings>())).Callback<AppSettings>(x => _settings = x);
            Configuration.Setup(x => x.ApplyAsync(It.IsAny<string>(), It.IsAny<DohProvider>(),
                    It.IsAny<DohEncryptionMode>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DohApplyResult(true, "applied"));
        }

        public DohViewModel Create()
        {
            var providerService = new Mock<IDohProviderService>();
            providerService.SetupGet(x => x.Providers).Returns(Providers);
            var selection = new Mock<IDohSelectionService>();
            var dns = new Mock<IDnsAdapterService>();
            dns.Setup(x => x.CaptureAsync("Ethernet", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DnsAdapterSnapshot("Ethernet", ["192.0.2.53"], false));
            return new DohViewModel(providerService.Object, selection.Object, Configuration.Object,
                Switch.Object, dns.Object, Settings.Object, Logger.Object,
                ["Ethernet"], _ => _settings.Doh.AppliedDnsAddresses);
        }
    }
}

internal static class LoggerMockExtensions
{
    public static void VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, Times times) =>
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
}
