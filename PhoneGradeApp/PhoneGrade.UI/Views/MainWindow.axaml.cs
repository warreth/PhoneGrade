using System;
using Avalonia.Controls;
using PhoneGrade.Core;
using PhoneGrade.UI.ViewModels;

namespace PhoneGrade.UI.Views;

public partial class MainWindow : Window, IDisposable
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        // Data editor requests arrive from the view model; keep a single editor instance.
        vm.DataEditorRequested += data =>
        {
            var editor = new DataEditorWindow { DataContext = new DataEditorViewModel(data) };
            editor.Show();
        };
    }

    /// <summary>
    /// Releases the view model behind the window, which closes the public address
    /// its tunnel opened. Closing the window already does this through the Closed
    /// event in App; this is for a window that is only built, because one of those
    /// is never closed and would otherwise leave the connector running.
    /// </summary>
    void IDisposable.Dispose() => (DataContext as MainWindowViewModel)?.Shutdown();
}
