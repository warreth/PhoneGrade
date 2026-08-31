using Avalonia.Controls;
using AutoDymoLabelApp.UI.ViewModels;
using System.Reactive;

namespace AutoDymoLabelApp.UI.Views;

/// <summary>Data editor window. Plain Window (not ReactiveWindow): the close
/// interaction is registered when the ViewModel arrives, which keeps the
/// window constructible in headless tests without a platform activator.</summary>
public partial class DataEditorWindow : Window
{
    public DataEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += InstallCloseHook;
    }

    private void InstallCloseHook(object? sender, EventArgs e)
    {
        if (DataContext is not DataEditorViewModel vm) return;

        vm.CloseWindowInteraction.RegisterHandler(interaction =>
        {
            Close();
            interaction.SetOutput(Unit.Default);
        });
    }
}
