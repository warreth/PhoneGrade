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

    /// <summary>
    /// Installed memory, in the box rather than as the raw placeholder.
    ///
    /// The phone stores "NOMEMORY" when it could not read the size, and showing
    /// that word in an editable field reads as a value an operator could keep.
    /// The field is empty instead, and an empty field written back stores the
    /// placeholder again, so the label keeps seeing the same unknown it saw
    /// before the editor was opened.
    /// </summary>
    public string Memory
    {
        get => DeviceData.Memory is null or "NOMEMORY" or "" ? "" : DeviceData.Memory;
        set => DeviceData.Memory = string.IsNullOrWhiteSpace(value) ? "NOMEMORY" : value;
    }

    /// <summary>
    /// True once the phone or the operator supplied at least one of the three
    /// read-only battery numbers. The card holds what the phone reported, so a
    /// phone that reported nothing gets no card: three zeroes under a heading
    /// read as three readings of zero.
    /// </summary>
    public bool HasBatteryMetrics =>
        DeviceData.BatteryCycleCount > 0
        || DeviceData.BatteryDesignCapacity > 0
        || DeviceData.BatteryCurrentCapacity > 0;

    /// <summary>
    /// Saves the edited values and closes.
    ///
    /// It used to also write the label and open it, which meant the only way out of
    /// this window wrote a file and handed it to whatever application the system
    /// had registered for a .dymo file. Writing the file is now the export panel's
    /// job, which is one place that knows about labels rather than two, and this
    /// window just saves. A correction made here has to be exported, and the panel
    /// opens with the corrected values in it.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveAndOpenLabelCommand { get; }

    /// <summary>Raised after a save, so the caller can put the operator in the export panel.</summary>
    public Interaction<Unit, Unit> SavedInteraction { get; } = new();

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
        // The values are already on the shared DeviceData: the fields bind to it in
        // place rather than copying it. So saving is recording the inspection, and
        // the export panel picks the corrected values up from there.
        AuditLogService.ExportAuditLog(DeviceData);
        await SavedInteraction.Handle(Unit.Default).ToTask();
        await CloseWindowInteraction.Handle(Unit.Default).ToTask();
    }
}
