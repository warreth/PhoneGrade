using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;
using Xunit;

namespace PhoneGrade.Tests;

public class AppWorkflowStateTests
{
    [Fact]
    public void AppWorkflowState_DefaultIsIdle()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal(AppWorkflowState.Idle, vm.WorkflowState);
        Assert.True(vm.IsIdleState);
        Assert.False(vm.IsActiveState);
        Assert.False(vm.IsSummaryState);
    }

    [Fact]
    public void AppWorkflowState_TransitionsCorrectly()
    {
        var vm = new MainWindowViewModel();
        
        vm.WorkflowState = AppWorkflowState.Active;
        Assert.True(vm.IsActiveState);
        Assert.False(vm.IsIdleState);
        Assert.False(vm.IsSummaryState);

        vm.WorkflowState = AppWorkflowState.Summary;
        Assert.True(vm.IsSummaryState);
        Assert.False(vm.IsActiveState);
        Assert.False(vm.IsIdleState);

        vm.BackToIdleCommand.Execute().Subscribe();
        Assert.True(vm.IsIdleState);
    }

    [Fact]
    public void SettingsDrawerAndModals_ToggleCorrectly()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsSettingsDrawerOpen);
        Assert.False(vm.IsLogsModalOpen);
        Assert.False(vm.IsTroubleshootModalOpen);

        vm.ToggleSettingsCommand.Execute().Subscribe();
        Assert.True(vm.IsSettingsDrawerOpen);

        vm.OpenLogsModalCommand.Execute().Subscribe();
        Assert.True(vm.IsLogsModalOpen);
        Assert.False(vm.IsSettingsDrawerOpen); // Drawer closes when opening modal

        vm.CloseLogsModalCommand.Execute().Subscribe();
        Assert.False(vm.IsLogsModalOpen);

        vm.OpenTroubleshootModalCommand.Execute().Subscribe();
        Assert.True(vm.IsTroubleshootModalOpen);
        Assert.False(vm.IsSettingsDrawerOpen);

        vm.CloseTroubleshootModalCommand.Execute().Subscribe();
        Assert.False(vm.IsTroubleshootModalOpen);
    }
}
