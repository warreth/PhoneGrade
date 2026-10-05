using System;
using System.Reactive;
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
            var form = new DataEditorViewModel(data);
            var editor = new DataEditorWindow { DataContext = form };

            // A correction made on the label form is followed by the export panel.
            // The form edits the same DeviceData the panel reads, so opening the
            // panel here is enough: the operator lands on a preview showing what
            // they just typed rather than having to ask for the label again.
            form.SavedInteraction.RegisterHandler(interaction =>
            {
                vm.ExportViewModel?.Open();
                interaction.SetOutput(Unit.Default);
            });

            editor.Show();
        };
    }

    /// <summary>
    /// Takes the picked device from the dropdown. The binding only ever runs from
    /// the view model into the control, because every refresh rebuilds the item list
    /// and the dropdown answers that by handing over null, and null does not fit in
    /// the struct the view model stores. A write that does not fit is not dropped
    /// quietly: Avalonia records it on the control and prints it underneath, which is
    /// how the text of the conversion failure ended up permanently below the list of
    /// detected devices. Nothing here reacts to an empty choice, and the refresh is
    /// what puts the selection back once it knows which devices are there.
    /// </summary>
    private void DeviceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && e.AddedItems.Count > 0
            && e.AddedItems[0] is KeyValuePair<string, string> device)
        {
            viewModel.SelectedDevice = device;
        }
    }

    /// <summary>
    /// Releases the view model behind the window, which closes the public address
    /// its tunnel opened. Closing the window already does this through the Closed
    /// event in App; this is for a window that is only built, because one of those
    /// is never closed and would otherwise leave the connector running.
    /// </summary>
    void IDisposable.Dispose() => (DataContext as MainWindowViewModel)?.Shutdown();
}
