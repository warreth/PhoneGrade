using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using PhoneGrade.Core;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>Editable view of the device data that will land on the label.</summary>
public class DataEditorViewModel
{
    public DeviceData DeviceData { get; }

    /// <summary>The choices the editor offers, in the shape they are stored in.</summary>
    public string[] QualityOptions { get; } = ["", "A", "B", "C"];
    public string[] PaymentOptions { get; } = ["", "Marge", "BTW"];

    /// <summary>
    /// The grade and the invoice method as the pickers hold them: the phone
    /// reports placeholders while nothing has been decided, and a picker whose
    /// item list does not contain its own selected value draws an empty box.
    /// Writing back stores the plain value again, so the label keeps seeing what
    /// it has always seen.
    /// </summary>
    public string Quality
    {
        get => DeviceData.Quality is null or "NOQUALITY" ? "" : DeviceData.Quality;
        set => DeviceData.Quality = value;
    }

    public string PayMethod
    {
        get => DeviceData.PayMethod is null or "NOPAY" or "" ? "" : DeviceData.PayMethod;
        set => DeviceData.PayMethod = value;
    }

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
        AuditLogService.ExportAuditLog(DeviceData);
        LabelService.OpenLabelFile();
        await CloseWindowInteraction.Handle(Unit.Default).ToTask();
    }
}
