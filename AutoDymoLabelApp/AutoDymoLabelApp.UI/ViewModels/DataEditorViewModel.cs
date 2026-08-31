using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using AutoDymoLabel.Core;
using ReactiveUI;

namespace AutoDymoLabelApp.UI.ViewModels;

/// <summary>Editable view of the device data that will land on the label.</summary>
public class DataEditorViewModel(DeviceData deviceData, Action<DeviceData>? onSave = null)
{
    public DeviceData DeviceData { get; } = deviceData;
    public ReactiveCommand<Unit, Unit> SaveAndOpenLabelCommand { get; }
    public Interaction<Unit, Unit> CloseWindowInteraction { get; } = new();

    public DataEditorViewModel() : this(new DeviceData()) { }

    public async Task SaveAndOpenLabelAsync()
    {
        LabelService.GenerateLabel(DeviceData);
        LabelService.OpenLabelFile();
        onSave?.Invoke(DeviceData);
        await CloseWindowInteraction.Handle(Unit.Default).ToTask();
    }
}
