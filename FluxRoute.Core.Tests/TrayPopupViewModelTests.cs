using FluxRoute.ViewModels;

namespace FluxRoute.Core.Tests;

public sealed class TrayPopupViewModelTests
{
    [Fact]
    public void UpdateState_ActiveServices_UpdatesStatusText()
    {
        // Arrange
        var viewModel = new TrayPopupViewModel(() => { }, () => { });

        // Act
        viewModel.UpdateState("General", true, true, true, true);

        // Assert
        Assert.Equal("General", viewModel.StrategyName);
        Assert.Equal("Защита включена", viewModel.ProtectionStatus);
        Assert.Equal("Включён", viewModel.OrchestratorStatus);
        Assert.Equal("Работает", viewModel.TgProxyStatus);
        Assert.Equal("Включён", viewModel.GameFilterStatus);
    }

    [Fact]
    public void UpdateState_EmptyStrategy_UsesFallbackAndDisabledStatuses()
    {
        // Arrange
        var viewModel = new TrayPopupViewModel(() => { }, () => { });

        // Act
        viewModel.UpdateState("  ", false, false, false, false);

        // Assert
        Assert.Equal("—", viewModel.StrategyName);
        Assert.Equal("Защита выключена", viewModel.ProtectionStatus);
        Assert.Equal("Выключен", viewModel.OrchestratorStatus);
        Assert.Equal("Остановлен", viewModel.TgProxyStatus);
        Assert.Equal("Выключен", viewModel.GameFilterStatus);
    }

    [Fact]
    public void Commands_InvokeProvidedActions()
    {
        // Arrange
        var openCount = 0;
        var exitCount = 0;
        var viewModel = new TrayPopupViewModel(
            () => openCount++,
            () => exitCount++);

        // Act
        viewModel.OpenApplicationCommand.Execute(null);
        viewModel.ExitApplicationCommand.Execute(null);

        // Assert
        Assert.Equal(1, openCount);
        Assert.Equal(1, exitCount);
    }
}
