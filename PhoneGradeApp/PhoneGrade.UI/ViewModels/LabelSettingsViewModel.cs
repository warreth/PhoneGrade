using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>
/// A label stock size as the picker shows it.
///
/// The sizes live in the Core project because the files are drawn from them, and
/// the panel needs the same three numbers to draw its preview. Wrapping them means
/// the picker never formats a measurement itself, so the preview and the PDF
/// cannot describe the same stock two different ways.
/// </summary>
public sealed class LabelLayoutItem
{
    public LabelLayoutItem(LabelStock stock)
    {
        Stock = stock;
    }

    /// <summary>The paper, named by the part number a shop orders it by.</summary>
    public LabelStock Stock { get; }

    /// <summary>The arrangement on that paper, for the renderer that draws it.</summary>
    public LabelLayout Layout => new(Stock);

    /// <summary>
    /// The name of the roll, in the language the operator is working in.
    /// </summary>
    /// <remarks>
    /// A key rather than the name Core carries. A shop exporting for a customer
    /// does not want English headings on a Dutch panel, and the names here are the
    /// ones a Dutch operator reads off a box of rolls.
    /// </remarks>
    public string TitleKey => $"LabelStock_{Stock.PartNumber}";

    /// <summary>
    /// The part number, which is how a shop orders the roll and how two rolls both
    /// called "address" are told apart.
    /// </summary>
    /// <remarks>
    /// On a line of its own under the name rather than appended to it. Appended,
    /// the longest name is cut off inside the picker and the operator cannot read
    /// the stock they are about to choose.
    /// </remarks>
    public string PartKey => $"Export_StockPart";

    public string PartNumber => Stock.PartNumber;

    /// <summary>The name as Core carries it, for anything with no UI to show it.</summary>
    public string Label => Stock.Label;

    public override string ToString() => Label;
}

/// <summary>
/// One choice for what the barcode carries, with the wording that explains it.
/// </summary>
/// <remarks>
/// Wraps the Core enum so the picker never formats a choice itself, the same
/// reason <see cref="LabelLayoutItem"/> exists for the stocks. It lives behind the
/// barcode switch in the label settings: the default is one Code39 code with the
/// serial number, and these are the options for the shop whose own software reads
/// more than that.
/// </remarks>
public sealed class LabelBarcodeItem : ReactiveObject
{
    private bool _isSelected;
    private bool _isAvailable = true;
    private bool _isKnown;

    private LabelBarcodeItem(LabelBarcodeMode mode, string titleKey, string noteKey)
    {
        Mode = mode;
        TitleKey = titleKey;
        NoteKey = noteKey;
    }

    public LabelBarcodeMode Mode { get; }

    public string TitleKey { get; }

    public string NoteKey { get; }

    /// <summary>
    /// Whether this mode can put bars on the paper at all, as bars.
    /// </summary>
    /// <remarks>
    /// The one mode that can be unavailable is the combined one, and only in Code39,
    /// where the identifier and the specification together are wider than any roll in
    /// the list. An unavailable mode is shown rather than hidden, with the reason in
    /// the panel: a shop that has just bought a Code128 scanner needs to find the
    /// setting and see why it is grey, not find that it does not exist.
    ///
    /// Held on the item for the same reason the tick is: the picker binds to a fixed
    /// list and a view cannot ask a Core type whether a value fits a roll.
    /// </remarks>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            // The field is asked, not the return value: ReactiveUI 20 returns the
            // new value from RaiseAndSetIfChanged, so the old guard skipped the
            // dependent raises exactly when a mode became unavailable.
            if (_isAvailable == value) return;
            this.RaiseAndSetIfChanged(ref _isAvailable, value);

            this.RaisePropertyChanged(nameof(IsClosed));
            this.RaisePropertyChanged(nameof(RowStrength));
            this.RaisePropertyChanged(nameof(ShowUnavailableReason));
        }
    }

    /// <summary>
    /// Whether this row cannot be chosen, which is what greys it.
    /// </summary>
    /// <remarks>
    /// Its own property rather than a negated binding in the view. The class is
    /// applied through <c>Classes.closed</c>, and a binding of the form
    /// <c>!IsAvailable</c> is not something the class syntax evaluates.
    /// </remarks>
    public bool IsClosed => !IsAvailable;

    /// <summary>
    /// How strongly this row is drawn.
    /// </summary>
    /// <remarks>
    /// Bound straight onto the row rather than reached through a class, because a
    /// class bound to a property inside a drop down's own item template is a style
    /// that has to survive being re-templated.
    /// </remarks>
    public double RowStrength => IsAvailable ? 1.0 : 0.72;

    /// <summary>
    /// Why this row is shut, in a form short enough to sit inside it.
    /// </summary>
    public string UnavailableKey => "Export_BarcodeUnavailable";

    /// <summary>
    /// Whether the panel should say why this mode is unavailable, rather than just
    /// greying it out.
    /// </summary>
    /// <remarks>
    /// Only worth saying when it would do any good. On the small rolls a phone's
    /// identifier is printed as words whatever the mode, so greying out the identifier
    /// alone would be claiming a setting cannot be used when in fact half of it is
    /// already on the label as text.
    /// </remarks>
    public bool ShowUnavailableReason => IsClosed;

    /// <summary>
    /// Recomputes which modes the stock and the symbology in force allow.
    /// </summary>
    /// <remarks>
    /// Called whenever the roll or the symbology changes, because those are the two
    /// things that decide it. The two measures are not the same question and both are
    /// needed: whether anything can be drawn as bars decides whether the setting is
    /// offered, and whether it can be drawn as bars decides whether the words have to
    /// carry it.
    /// </remarks>
    internal static void RefreshFor(
        LabelStock stock, LabelCodeSymbology symbology, LabelCode code, bool identifiable)
    {
        foreach (LabelBarcodeItem item in All)
        {
            bool offered = code.Available(stock, item.Mode, symbology, identifiable);

            bool available = item.Mode switch
            {
                LabelBarcodeMode.None => true,
                LabelBarcodeMode.Combined => offered,
                _ => offered
                    || code.On(stock, item.Mode, symbology, identifiable).Barred.Count > 0,
            };

            bool unchanged = item._isKnown && item._isAvailable == available;

            item.IsAvailable = available;

            if (unchanged) continue;

            item._isKnown = true;
        }
    }

    /// <summary>
    /// Whether this is the chosen mode, for the picker's tick.
    /// </summary>
    /// <remarks>
    /// Held on the item rather than worked out in the view, because the items are a
    /// fixed list the picker binds to and a view cannot ask them anything. An
    /// earlier version compared the item's own mode with itself, which is always
    /// true, so every option showed as chosen at once.
    /// </remarks>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// The modes, in the order they are offered.
    /// </summary>
    /// <remarks>
    /// The identifier alone leads because it is what a till reads and it leaves the
    /// most room on the paper for the words, which on a 28mm label is not much. None
    /// is not in this list: whether there is a barcode at all is the switch above,
    /// and a mode picker that could also turn the barcode off gave the same setting
    /// two places to live.
    /// </remarks>
    public static IReadOnlyList<LabelBarcodeItem> All { get; } =
    [
        new(LabelBarcodeMode.Identifier, "Export_BarcodeIdentifier", "Export_BarcodeIdentifierNote"),
        new(LabelBarcodeMode.Split, "Export_BarcodeSplit", "Export_BarcodeSplitNote"),
        new(LabelBarcodeMode.Combined, "Export_BarcodeCombined", "Export_BarcodeCombinedNote"),
    ];

    /// <summary>The item for a mode, falling back to the identifier alone.</summary>
    public static LabelBarcodeItem For(LabelBarcodeMode mode) =>
        All.FirstOrDefault(item => item.Mode == mode) ?? All[0];

    /// <summary>Moves the tick onto one item and off the others.</summary>
    internal static void Select(LabelBarcodeMode mode)
    {
        foreach (LabelBarcodeItem item in All) item.IsSelected = item.Mode == mode;
    }
}

/// <summary>
/// Which symbology the label's barcode is drawn in, as a choice on the settings page.
/// </summary>
/// <remarks>
/// Its own list rather than reusing <see cref="LabelBarcodeItem"/>, because a
/// symbology is not a mode: every mode is available in every symbology, and what
/// changes is whether the combined mode fits and whether the shop's scanner can
/// read it at all.
/// </remarks>
public sealed class LabelSymbologyItem : ReactiveObject
{
    private bool _isSelected;

    private LabelSymbologyItem(LabelCodeSymbology symbology, string titleKey, string noteKey)
    {
        Symbology = symbology;
        TitleKey = titleKey;
        NoteKey = noteKey;
    }

    public LabelCodeSymbology Symbology { get; }

    public string TitleKey { get; }

    public string NoteKey { get; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// The symbologies, in the order they are offered.
    /// </summary>
    /// <remarks>
    /// Code39 first because it is what every till reads and what the shipped DYMO
    /// template declares, so a shop that changes nothing gets a label every scanner
    /// in the building reads. Code128 is the second choice because it is the one that
    /// lets a single code carry the whole device.
    /// </remarks>
    public static IReadOnlyList<LabelSymbologyItem> All { get; } =
    [
        new(LabelCodeSymbology.Code39, "Export_Symbology39", "Export_Symbology39Note"),
        new(LabelCodeSymbology.Code128, "Export_Symbology128", "Export_Symbology128Note"),
    ];

    public static LabelSymbologyItem For(LabelCodeSymbology symbology) =>
        All.FirstOrDefault(item => item.Symbology == symbology) ?? All[0];

    /// <summary>Moves the tick onto one item and off the others.</summary>
    internal static void Select(LabelCodeSymbology symbology)
    {
        foreach (LabelSymbologyItem item in All) item.IsSelected = item.Symbology == symbology;
    }
}

/// <summary>
/// One arrangement of the label, as the settings picker offers it.
/// </summary>
public sealed class LabelVariantItem
{
    private LabelVariantItem(LabelVariant variant, string titleKey, string noteKey)
    {
        Variant = variant;
        TitleKey = titleKey;
        NoteKey = noteKey;
    }

    public LabelVariant Variant { get; }

    public string TitleKey { get; }

    public string NoteKey { get; }

    /// <summary>
    /// The arrangements, in the order they are offered.
    /// </summary>
    /// <remarks>
    /// The cleaned single line leads because it is what the label has always been;
    /// a shop that updates the app gets the same label it had. The other two are
    /// there for a shop that wants the grade or the faults to be the first thing
    /// the eye meets.
    /// </remarks>
    public static IReadOnlyList<LabelVariantItem> All { get; } =
    [
        new(LabelVariant.Clean, "Settings_LabelVariantClean", "Settings_LabelVariantCleanNote"),
        new(LabelVariant.Structured, "Settings_LabelVariantStructured", "Settings_LabelVariantStructuredNote"),
        new(LabelVariant.GradeBlock, "Settings_LabelVariantGradeBlock", "Settings_LabelVariantGradeBlockNote"),
    ];
}

/// <summary>One grouping of the export folder, as the settings picker offers it.</summary>
public sealed class ExportFolderItem
{
    private ExportFolderItem(ExportFolderScheme scheme, string titleKey, string noteKey)
    {
        Scheme = scheme;
        TitleKey = titleKey;
        NoteKey = noteKey;
    }

    public ExportFolderScheme Scheme { get; }

    public string TitleKey { get; }

    public string NoteKey { get; }

    public static IReadOnlyList<ExportFolderItem> All { get; } =
    [
        new(ExportFolderScheme.Day, "Settings_FolderDay", "Settings_FolderDayNote"),
        new(ExportFolderScheme.Week, "Settings_FolderWeek", "Settings_FolderWeekNote"),
        new(ExportFolderScheme.Month, "Settings_FolderMonth", "Settings_FolderMonthNote"),
        new(ExportFolderScheme.Inspection, "Settings_FolderInspection", "Settings_FolderInspectionNote"),
    ];
}

/// <summary>
/// The label settings, with the preview that shows what they add up to.
/// </summary>
/// <remarks>
/// The pickers and the switches write straight through to the settings, because a
/// shop configures its label once and every label after that follows. The preview
/// is the plate both the finish panel and the label PDF are drawn from, so what a
/// shop sees here is the label it will get.
/// </remarks>
public class LabelSettingsViewModel : ReactiveObject
{
    private readonly MainWindowViewModel _main;
    private LabelLayoutItem? _stock;
    private LabelBarcodeItem? _barcode;
    private LabelSymbologyItem? _symbology;
    private LabelPlate? _plate;

    public LabelSettingsViewModel(MainWindowViewModel main)
    {
        _main = main;

        Layouts = new ObservableCollection<LabelLayoutItem>(
            LabelStock.All.Select(stock => new LabelLayoutItem(stock)));
        BarcodeModes = new ObservableCollection<LabelBarcodeItem>(LabelBarcodeItem.All);
        Symbologies = new ObservableCollection<LabelSymbologyItem>(LabelSymbologyItem.All);
        Variants = new ObservableCollection<LabelVariantItem>(LabelVariantItem.All);
        FolderSchemes = new ObservableCollection<ExportFolderItem>(ExportFolderItem.All);

        _stock = Layouts.FirstOrDefault(item =>
            string.Equals(item.Stock.PartNumber, main.LabelStockPartNumber, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];

        _barcode = LabelBarcodeItem.For(main.LabelBarcodeMode);
        LabelBarcodeItem.Select(_barcode.Mode);
        _symbology = LabelSymbologyItem.For(main.LabelSymbology);
        LabelSymbologyItem.Select(_symbology.Symbology);

        Refresh();
    }

    /// <summary>The stocks a shop can choose from, most used first.</summary>
    public ObservableCollection<LabelLayoutItem> Layouts { get; }

    /// <summary>The barcode modes behind the barcode switch.</summary>
    public ObservableCollection<LabelBarcodeItem> BarcodeModes { get; }

    /// <summary>The symbologies the bars can be drawn in.</summary>
    public ObservableCollection<LabelSymbologyItem> Symbologies { get; }

    /// <summary>The arrangements the label can be drawn in.</summary>
    public ObservableCollection<LabelVariantItem> Variants { get; }

    /// <summary>The groupings the exports folder can use.</summary>
    public ObservableCollection<ExportFolderItem> FolderSchemes { get; }

    /// <summary>The roll in the printer.</summary>
    public LabelLayoutItem? Stock
    {
        get => _stock;
        set
        {
            if (Equals(_stock, value) || value is null) return;
            _stock = value;
            _main.SetLabelStock(value.Stock);
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(Layout));
            Refresh();
        }
    }

    /// <summary>The arrangement of the label.</summary>
    public LabelVariant Variant
    {
        get => _main.LabelVariant;
        set
        {
            if (_main.LabelVariant == value) return;
            _main.LabelVariant = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(VariantItem));
            Refresh();
        }
    }

    /// <summary>The arrangement, as the picker holds it.</summary>
    public LabelVariantItem? VariantItem
    {
        get => Variants.FirstOrDefault(item => item.Variant == _main.LabelVariant) ?? Variants[0];
        set
        {
            if (value is null) return;
            Variant = value.Variant;
        }
    }

    /// <summary>Whether the label carries a barcode at all.</summary>
    public bool BarcodeEnabled
    {
        get => _main.LabelBarcodeEnabled;
        set
        {
            if (_main.LabelBarcodeEnabled == value) return;
            _main.LabelBarcodeEnabled = value;
            this.RaisePropertyChanged();
            Refresh();
        }
    }

    /// <summary>What the barcode carries, for the shop whose software reads more than a serial.</summary>
    public LabelBarcodeItem? Barcode
    {
        get => _barcode;
        set
        {
            if (Equals(_barcode, value) || value is null) return;

            // A mode that cannot be drawn as bars on this roll is not a choice the
            // page accepts, and the picker has already greyed it out. A settings file
            // naming one anyway falls back to the identifier alone rather than
            // writing a label whose barcode does not scan.
            LabelBarcodeItem use = value.IsAvailable ? value : LabelBarcodeItem.For(LabelBarcodeMode.Identifier);

            _barcode = use;
            LabelBarcodeItem.Select(use.Mode);
            _main.SetLabelBarcodeMode(use.Mode);
            this.RaisePropertyChanged();
            Refresh();
        }
    }

    /// <summary>Which symbology the bars are drawn in.</summary>
    public LabelSymbologyItem? Symbology
    {
        get => _symbology;
        set
        {
            if (Equals(_symbology, value) || value is null) return;
            _symbology = value;
            LabelSymbologyItem.Select(value.Symbology);
            _main.SetLabelSymbology(value.Symbology);
            this.RaisePropertyChanged();
            Refresh();
        }
    }

    /// <summary>Whether the charge count is on the label at all.</summary>
    public bool ShowCycles
    {
        get => _main.LabelShowBatteryCycles;
        set
        {
            if (_main.LabelShowBatteryCycles == value) return;
            _main.LabelShowBatteryCycles = value;
            this.RaisePropertyChanged();
            Refresh();
        }
    }

    // ============ The quality thresholds ============

    /// <summary>The battery percentage under which the marker goes on the label.</summary>
    public double BatteryThreshold
    {
        get => _main.LabelBatteryThreshold;
        set
        {
            int wanted = (int)Math.Round(value);
            if (_main.LabelBatteryThreshold == wanted) return;
            _main.LabelBatteryThreshold = wanted;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BatteryThresholdText));
            this.RaisePropertyChanged(nameof(BatteryExample));
            Refresh();
        }
    }

    /// <summary>The threshold as it is read beside the slider.</summary>
    public string BatteryThresholdText => $"{_main.LabelBatteryThreshold}%";

    /// <summary>
    /// One sentence showing what the threshold does to the phone on screen, so a
    /// shop reads the effect rather than the number.
    /// </summary>
    public string BatteryExample
    {
        get
        {
            LabelFields fields = LabelPreview.Fields(_main);
            string battery = fields.Battery == DevicePlaceholders.Battery
                ? LocalizationManager.GetString("Settings_QualityBatteryUnknown")
                : fields.Battery;

            return string.Format(LocalizationManager.GetString("Settings_QualityBatteryExample"), battery);
        }
    }

    /// <summary>Below this charge count the number is left off the label.</summary>
    public double CyclesThreshold
    {
        get => _main.LabelCyclesThreshold;
        set
        {
            int wanted = (int)Math.Round(value);
            if (_main.LabelCyclesThreshold == wanted) return;
            _main.LabelCyclesThreshold = wanted;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(CyclesThresholdText));
            this.RaisePropertyChanged(nameof(CyclesExample));
            Refresh();
        }
    }

    /// <summary>The threshold as it is read beside the slider.</summary>
    public string CyclesThresholdText => _main.LabelCyclesThreshold.ToString();

    /// <summary>One sentence showing what the threshold does to the phone on screen.</summary>
    public string CyclesExample
    {
        get
        {
            LabelFields fields = LabelPreview.Fields(_main);

            // A phone whose counter could not be read gets the sentence about that,
            // not a sentence about NOCYCLES cycles.
            if (fields.BatteryCycles == DevicePlaceholders.BatteryCycles)
                return LocalizationManager.GetString("Settings_QualityCyclesUnknown");

            bool shown = int.TryParse(fields.BatteryCycles, out int count)
                         && count >= _main.LabelCyclesThreshold;

            return string.Format(
                LocalizationManager.GetString(shown
                    ? "Settings_QualityCyclesShown"
                    : "Settings_QualityCyclesHidden"),
                fields.BatteryCycles);
        }
    }

    /// <summary>Whether the marker for a tired battery is on the label at all.</summary>
    public bool BatteryCheck
    {
        get => _main.Enable85PercentChecker;
        set
        {
            if (_main.Enable85PercentChecker == value) return;
            _main.Enable85PercentChecker = value;
            this.RaisePropertyChanged();
            Refresh();
        }
    }

    // ============ The files and the folder ============

    /// <summary>Whether the label file is written at every finish.</summary>
    public bool WritesDymo
    {
        get => _main.WritesFormat(ExportFormat.DymoLabel);
        set { _main.SetWritesFormat(ExportFormat.DymoLabel, value); RaiseWrites(); }
    }

    /// <summary>Whether the label PDF is written at every finish.</summary>
    public bool WritesLabelPdf
    {
        get => _main.WritesFormat(ExportFormat.LabelPdf);
        set { _main.SetWritesFormat(ExportFormat.LabelPdf, value); RaiseWrites(); }
    }

    /// <summary>Whether the report PDF is written at every finish.</summary>
    public bool WritesReportPdf
    {
        get => _main.WritesFormat(ExportFormat.ReportPdf);
        set { _main.SetWritesFormat(ExportFormat.ReportPdf, value); RaiseWrites(); }
    }

    /// <summary>Whether the numbers are written as JSON at every finish.</summary>
    public bool WritesJson
    {
        get => _main.WritesFormat(ExportFormat.Json);
        set { _main.SetWritesFormat(ExportFormat.Json, value); RaiseWrites(); }
    }

    /// <summary>Whether the numbers are written as CSV at every finish.</summary>
    public bool WritesCsv
    {
        get => _main.WritesFormat(ExportFormat.Csv);
        set { _main.SetWritesFormat(ExportFormat.Csv, value); RaiseWrites(); }
    }

    /// <summary>How the exports are grouped in the folder.</summary>
    public ExportFolderItem? FolderScheme
    {
        get => FolderSchemes.FirstOrDefault(item => item.Scheme == _main.ExportFolderScheme) ?? FolderSchemes[1];
        set
        {
            if (value is null || _main.ExportFolderScheme == value.Scheme) return;
            _main.ExportFolderScheme = value.Scheme;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(FolderExample));
        }
    }

    /// <summary>Where this device's files would land with the chosen scheme.</summary>
    public string FolderExample
    {
        get
        {
            string folder = ExportFolders.FolderUnder(
                ExportService.ExportDir, _main.ExportFolderScheme, DateTime.Now,
                LabelWriter.DeviceToken(_main.DeviceData));
            string relative = Path.GetRelativePath(ExportService.ExportDir, folder);
            return Path.Combine("export", relative);
        }
    }

    private void RaiseWrites()
    {
        this.RaisePropertyChanged(nameof(WritesDymo));
        this.RaisePropertyChanged(nameof(WritesLabelPdf));
        this.RaisePropertyChanged(nameof(WritesReportPdf));
        this.RaisePropertyChanged(nameof(WritesJson));
        this.RaisePropertyChanged(nameof(WritesCsv));
    }

    /// <summary>The stock, as the renderers take it.</summary>
    public LabelLayout Layout => _stock?.Layout ?? LabelLayout.Address;

    /// <summary>The drawing plan the preview shows.</summary>
    public LabelPlate? Plate
    {
        get => _plate;
        private set => this.RaiseAndSetIfChanged(ref _plate, value);
    }

    /// <summary>
    /// Rebuilds the preview and re-asks which barcode modes fit this roll.
    /// </summary>
    /// <remarks>
    /// Called whenever a setting changes, here or on the finish panel, because both
    /// edit the same stored values. The availability refresh comes first: a mode that
    /// cannot be drawn in the symbology in force has to be off the picker before
    /// anything binds to it.
    /// </remarks>
    public void Refresh()
    {
        LabelFields fields = LabelPreview.Fields(_main);

        LabelBarcodeItem.RefreshFor(
            Layout.Stock, _main.LabelSymbology,
            new LabelCode(fields.Identifier, LabelLayout.ScannableLine(fields, _main.LabelSymbology)),
            fields.IsIdentifiable);

        // The tick follows whatever the settings hold, which may have been changed
        // from the finish panel.
        _barcode = LabelBarcodeItem.For(_main.LabelBarcodeMode);
        LabelBarcodeItem.Select(_barcode.Mode);
        _symbology = LabelSymbologyItem.For(_main.LabelSymbology);
        LabelSymbologyItem.Select(_symbology.Symbology);
        this.RaisePropertyChanged(nameof(Barcode));
        this.RaisePropertyChanged(nameof(Symbology));

        Plate = LabelPreview.Plate(
            _main, Layout,
            _main.LabelBarcodeEnabled ? _main.LabelBarcodeMode : LabelBarcodeMode.None,
            _main.LabelSymbology, _main.LabelVariant);

        // The example lines read the phone on screen, so a device change moves them
        // too.
        this.RaisePropertyChanged(nameof(BatteryExample));
        this.RaisePropertyChanged(nameof(CyclesExample));
        this.RaisePropertyChanged(nameof(FolderExample));
    }
}
