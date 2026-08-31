using Avalonia.Controls;
using AutoDymoLabel.Core;
using AutoDymoLabelApp.UI.ViewModels;

namespace AutoDymoLabelApp.UI.Views;

public partial class MainWindow : Window
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
}
