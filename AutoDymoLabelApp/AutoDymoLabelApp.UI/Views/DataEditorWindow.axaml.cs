using Avalonia.ReactiveUI;
using AutoDymoLabelApp.UI.ViewModels;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;

namespace AutoDymoLabelApp.UI.Views;

public partial class DataEditorWindow : ReactiveWindow<DataEditorViewModel>
{
    public DataEditorWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            if (DataContext is DataEditorViewModel vm)
            {
                vm.SaveAndOpenLabelCommand.Subscribe(_ => Close());
                vm.CloseWindowInteraction.RegisterHandler(interaction =>
                {
                    Close();
                    interaction.SetOutput(Unit.Default);
                }).DisposeWith(disposables);
            }
        });
    }
}
