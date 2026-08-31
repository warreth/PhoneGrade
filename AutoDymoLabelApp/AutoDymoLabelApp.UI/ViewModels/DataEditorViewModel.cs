using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using AutoDymoLabel.Core;
using ReactiveUI;

namespace AutoDymoLabelApp.UI.ViewModels;

/// <summary>Editable view of the device data that will land on the label.</summary>
public class DataEditorViewModel
{
    public DeviceData DeviceData { get; }

    /// <summary>Saves the edited data back to the label and opens it.</summary>
    public ReactiveCommand<Unit, Unit> SaveAndOpenLabelCommand { get; }

    /// <summary>View hook: the window closes itself when this interaction is handled.</summary>
    public Interaction<Unit, Unit> CloseWindowInteraction { get; } = new();

    /// <summary>Main constructor.</summary>
    public DataEditorViewModel(DeviceData deviceData)
    {
        DeviceData = deviceData;
        SaveAndOpenLabelCommand = ReactiveCommand.CreateFromTask(SaveAndOpenLabelAsync);
    }

    /// <summary>Parameterless constructor for the XAML designer.</summary>
    public DataEditorViewModel() : this(new DeviceData()) { }

    public async Task SaveAndOpenLabelAsync()
    {
        LabelService.GenerateLabel(DeviceData);
        LabelService.OpenLabelFile();
        await CloseWindowInteraction.Handle(Unit.Default).ToTask();
    }
}
