using System;
using System.Collections.ObjectModel;
using Avalonia;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

/// <summary>One format as the export panel offers it: a name, a line about it, and whether it is ticked.</summary>
public sealed class ExportOption : ReactiveObject
{
    private readonly Action _onChanged;
    private bool _selected;

    /// <param name="onChanged">
    /// Told when this option is ticked or unticked. The panel's own enable state
    /// depends on whether anything is ticked, and a tickbox that can leave the
    /// export button greyed with no tickbox off is the bug this avoids.
    /// </param>
    public ExportOption(ExportFormat format, bool selected, Action onChanged)
    {
        Format = format;
        _selected = selected;
        _onChanged = onChanged;
    }

    public ExportFormat Format { get; }

    /// <summary>The extension, for the line under the name. The operator recognises
    /// this more reliably than the format name.</summary>
    public string Suffix => Format.FileSuffix();

    /// <summary>
    /// The name, in the language the operator is working in. The Core project
    /// carries an English name for callers with no UI, and the panel never uses
    /// it, because a shop exporting for a customer does not want English headings
    /// in a Dutch document.
    /// </summary>
    public string TitleKey => Format switch
    {
        ExportFormat.DymoLabel => "Export_FormatDymo",
        ExportFormat.LabelPdf => "Export_FormatLabelPdf",
        ExportFormat.ReportPdf => "Export_FormatReportPdf",
        ExportFormat.Json => "Export_FormatJson",
        ExportFormat.Csv => "Export_FormatCsv",
        _ => "Export_FormatJson",
    };

    /// <summary>What this format is for, so nobody is picking by extension.</summary>
    public string NoteKey => Format switch
    {
        ExportFormat.DymoLabel => "Export_FormatDymoNote",
        ExportFormat.LabelPdf => "Export_FormatLabelPdfNote",
        ExportFormat.ReportPdf => "Export_FormatReportPdfNote",
        ExportFormat.Json => "Export_FormatJsonNote",
        ExportFormat.Csv => "Export_FormatCsvNote",
        _ => "Export_FormatJsonNote",
    };

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            this.RaisePropertyChanged();
            _onChanged();
        }
    }
}

/// <summary>One file that was written, with the things an operator can do to it.</summary>
public sealed class ExportResultItem : ReactiveObject
{
    public ExportResultItem(LabelWriter.Outcome outcome, string folder)
    {
        Outcome = outcome;
        Folder = folder;
        TitleKey = outcome.Format switch
        {
            ExportFormat.DymoLabel => "Export_FormatDymo",
            ExportFormat.LabelPdf => "Export_FormatLabelPdf",
            ExportFormat.ReportPdf => "Export_FormatReportPdf",
            ExportFormat.Json => "Export_FormatJson",
            _ => "Export_FormatCsv",
        };
    }

    public LabelWriter.Outcome Outcome { get; }

    public string Folder { get; }

    public string TitleKey { get; }

    /// <summary>The file name on its own. The folder is shown once, above the list.</summary>
    public string FileName => Outcome.Path is { Length: > 0 } path
        ? Path.GetFileName(path)
        : "";

    public bool Succeeded => Outcome.Succeeded;

    /// <summary>
    /// Whether this file can be printed. Only a PDF can: the .dymo file needs DYMO
    /// software to print, and the numbers are not a document. Offering a print
    /// button for a JSON file is a button that can only fail.
    /// </summary>
    public bool CanPrint => Succeeded
        && Outcome.Format is ExportFormat.LabelPdf or ExportFormat.ReportPdf;

    /// <summary>Whether opening it in its own application makes sense. Only for the label file.</summary>
    public bool CanOpenInApp => Succeeded && Outcome.Format == ExportFormat.DymoLabel;

    /// <summary>Where a printer can be chosen for this file.</summary>
    public bool AcceptsAPrinter => CanPrint;
}

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
/// reason <see cref="LabelLayoutItem"/> exists for the stocks.
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
            if (!this.RaiseAndSetIfChanged(ref _isAvailable, value)) return;

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
    /// <c>!IsAvailable</c> is not something the class syntax evaluates: the row came
    /// out identical in both symbologies, which is exactly the pair of screenshots
    /// that was taken to catch it.
    /// </remarks>
    public bool IsClosed => !IsAvailable;

    /// <summary>
    /// How strongly this row is drawn.
    /// </summary>
    /// <remarks>
    /// Bound straight onto the row rather than reached through a class, because a
    /// class bound to a property inside a drop down's own item template is a style
    /// that has to survive being re-templated, and this one did not: the row came
    /// out at full strength in the pair of screenshots taken to check it. A number
    /// on the row cannot be lost that way.
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
            // One question, and which way it is answered depends on the mode.
            //
            // For every mode but the combined one, a value too wide for bars is
            // printed as words instead, and the label still says what the mode
            // promised. A fifteen digit identifier does not fit as bars on the two
            // small rolls, so the identifier mode is printed as text there with a
            // warning above it. That is a setting that works, in a smaller form, and
            // closing its row would tell the operator it cannot be used at all.
            //
            // The combined mode is the exception, because there is no smaller form
            // of it: its whole point is one code carrying both halves, and printed as
            // words it is not what was chosen. So it is offered exactly when it can
            // be drawn, which is what puts the symbology picker and this row in the
            // same card.
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
    /// most room on the paper for the words, which on a 28mm label is not much.
    /// </remarks>
    public static IReadOnlyList<LabelBarcodeItem> All { get; } =
    [
        new(LabelBarcodeMode.Identifier, "Export_BarcodeIdentifier", "Export_BarcodeIdentifierNote"),
        new(LabelBarcodeMode.Split, "Export_BarcodeSplit", "Export_BarcodeSplitNote"),
        new(LabelBarcodeMode.Combined, "Export_BarcodeCombined", "Export_BarcodeCombinedNote"),
        new(LabelBarcodeMode.None, "Export_BarcodeNone", "Export_BarcodeNoneNote"),
    ];

    /// <summary>The item for a mode, falling back to the identifier alone.</summary>
    public static LabelBarcodeItem For(LabelBarcodeMode mode) =>
        All.FirstOrDefault(item => item.Mode == mode) ?? All[1];

    /// <summary>Moves the tick onto one item and off the others.</summary>
    internal static void Select(LabelBarcodeMode mode)
    {
        foreach (LabelBarcodeItem item in All) item.IsSelected = item.Mode == mode;
    }
}

/// <summary>
/// Which symbology the label's barcode is drawn in, as a choice on the panel.
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

/// <summary>A printer the operator can pick, and where it came from.</summary>
public sealed class PrinterOption
{
    public PrinterOption(string name, string detail, PrintService.Outcome kind, DymoPrinter? dymo = null)
    {
        Name = name;
        Detail = detail;
        Kind = kind;
        Dymo = dymo;
    }

    public string Name { get; }

    /// <summary>Where it was found, in words.</summary>
    public string Detail { get; }

    public PrintService.Outcome Kind { get; }

    /// <summary>The DYMO it stands for, when it came from the web service.</summary>
    public DymoPrinter? Dymo { get; }
}

/// <summary>
/// The export panel.
///
/// One screen for everything an inspection leaves behind. The old shape was a row
/// of buttons that each wrote one file into a folder the operator had never heard
/// of, and a print button that opened whichever file happened to be there. This
/// is the opposite: the label is drawn on screen exactly as it will print, the
/// formats are chosen by ticking them, and one button writes every one of them
/// into a folder that is named on the screen and can be opened from there.
///
/// The three states it can be in are deliberately distinct, because they answer
/// three different questions. Nothing written yet: the preview and the choices.
/// Written: what was written, and what can be done to each file. Something went
/// wrong: which format failed and why, with the others still listed as written.
/// </summary>
public class ExportViewModel : ReactiveObject
{
    private readonly MainWindowViewModel _main;
    // Seeded with true so the command's can-execute has a first value: a subject
    // that has never emitted would leave the export button dead until the first
    // time somebody touches a tickbox.
    private readonly BehaviorSubject<bool> _selectionChanged = new(true);
    private bool _isOpen;
    private bool _isBusy;
    private string _status = "";
    private bool _statusIsError;
    private string _folder = ExportService.ExportDir;
    private LabelLayoutItem? _stock;
    private LabelBarcodeItem? _barcode;

    private LabelSymbologyItem? _symbology;
    private PrinterOption? _printer;
    private DymoPrintResult? _lastDymoPrint;
    private int _copies = 1;

    /// <summary>Whether the operator has been told the sheet may be smaller than the label.</summary>
    private bool _sheetOverflows;

    /// <summary>
    /// Whether the label is drawn smaller than its real size because the column
    /// cannot hold it at full width.
    ///
    /// Scaled down it stays the right shape, which is what the preview is for, but
    /// it is no longer a picture of the physical label, and an operator judging a
    /// label's proportions deserves to know that. Said on the panel rather than
    /// left for them to work out from a ruler.
    /// </summary>
    public bool SheetOverflows
    {
        get => _sheetOverflows;
        private set => this.RaiseAndSetIfChanged(ref _sheetOverflows, value);
    }

    public ExportViewModel(MainWindowViewModel main)
    {
        _main = main;

        Options = new ObservableCollection<ExportOption>();
        foreach (ExportFormat format in ExportService.All)
            Options.Add(new ExportOption(format, ExportService.DefaultSet.Contains(format), NoteSelection));

        // The stocks the panel offers, most used first, and the one the operator
        // chose. Read from the settings rather than defaulted here, because the
        // stock is chosen once and then applies to every label from then on: a
        // picker that forgot it on each panel would put a 36mm label on a 28mm roll.
        Layouts = new ObservableCollection<LabelLayoutItem>(
            LabelStock.All.Select(stock => new LabelLayoutItem(stock)));

        BarcodeModes = new ObservableCollection<LabelBarcodeItem>(LabelBarcodeItem.All);
        Symbologies = new ObservableCollection<LabelSymbologyItem>(LabelSymbologyItem.All);
        Printers = new ObservableCollection<PrinterOption>();
        Results = new ObservableCollection<ExportResultItem>();

        // Seeded from the settings rather than left for the operator to pick, because
        // both of these decide how much of the paper the barcodes take. An unset
        // picker draws a preview of one label and writes another.
        Stock = Layouts.FirstOrDefault(item =>
            string.Equals(item.Stock.PartNumber, main.LabelStockPartNumber, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];
        Barcode = LabelBarcodeItem.For(main.LabelBarcodeMode);
        LabelBarcodeItem.Select(Barcode.Mode);
        Symbology = LabelSymbologyItem.For(main.LabelSymbology);
        LabelSymbologyItem.Select(Symbology.Symbology);

        // The formats live in a collection rather than in properties, so a tick cannot
        // raise a property change on its own. The subject is how a tickbox tells
        // the panel, and the panel is what tells the button.
        var somethingSelected = _selectionChanged
            .Select(_ => Options.Any(option => option.Selected));

        var notBusy = this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy);

        // CombineLatest rather than And: both inputs are re-checked whenever either
        // one changes, which is what lets clearing the last tick disable the button.
        var canExport = Observable.CombineLatest(somethingSelected, notBusy,
            (selected, idle) => selected && idle);
        canExport.Subscribe(allowed => this.RaisePropertyChanged(nameof(CanExport)));

        // The button binds to CanExport rather than to the command's own
        // can-execute, because a button that looks enabled and refuses to run is
        // the operator pressing it twice.
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync, canExport);
        PrintLabelCommand = ReactiveCommand.CreateFromTask(PrintLabelAsync, notBusy);
        DetectPrintersCommand = ReactiveCommand.CreateFromTask(DetectPrintersAsync, notBusy);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        CloseCommand = ReactiveCommand.Create(Close);
        OpenFileCommand = ReactiveCommand.Create<string>(path => _ = PrintService.Open(path, ExportWordingBuilder.Current()));
        PrintFileCommand = ReactiveCommand.Create<ExportResultItem>(item => _ = PrintFileAsync(item));
    }

    /// <summary>How wide the label is drawn on the preview sheet, in pixels.</summary>
    public double SheetWidth
    {
        get
        {
            double wanted = Layout.PaperWidthMm * PixelsPerMm;
            double allowed = _available > 0 ? _available : MaxSheetWidth;

            // Never below a width that still shows something: a sheet squeezed to
            // a sliver because the window is narrow is not a smaller label, it is
            // no preview at all, and the operator is better served by one that is
            // too small to read than by none.
            return Math.Max(NarrowestSheetWidth, Math.Min(wanted, Math.Min(MaxSheetWidth, allowed)));
        }
    }

    /// <summary>
    /// How wide the column the preview sits in actually is, in pixels.
    /// </summary>
    /// <remarks>
    /// Told by the view once it has been laid out, because only the laid out panel
    /// knows. Without it the sheet was drawn at a fixed width and ran off the side
    /// of its own column on a narrow window, which is the kiosk size the shop
    /// actually uses.
    /// </remarks>
    private double _available;

    /// <summary>The narrowest a sheet may be drawn before it stops being one.</summary>
    private const double NarrowestSheetWidth = 120;

    /// <summary>
    /// How many preview pixels a millimetre of paper is at the size the sheet is
    /// being drawn.
    /// </summary>
    /// <remarks>
    /// Not <see cref="PixelsPerMm"/>, because the sheet is often narrower than the
    /// column allows and is scaled down to fit. Every measurement taken off the
    /// preview is multiplied by this, which is what lets the preview be drawn at the
    /// proportions of the roll rather than at proportions of its own.
    /// </remarks>
    private double SheetScale => SheetWidth / Layout.PaperWidthMm;

    /// <summary>How tall one barcode band is on the sheet, bars and caption together.</summary>
    public double BarcodeBandHeight => Layout.BarcodeBandMm(LabelBarcodes.Count) * SheetScale;

    /// <summary>
    /// How much of a barcode band the bars take, the rest being the value under them.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed. The value is set at five and a half points and
    /// the box a line of it occupies is about a third taller again, so reserving the
    /// nominal size of the caption left less room than it takes and the value was
    /// drawn across the bottom of the bars. On a two code label it reached the bars
    /// of the code below it.
    /// </remarks>
    public double BarcodeBarShare
    {
        get
        {
            double band = BarcodeBandHeight;
            if (band <= 0) return 0.78;

            double caption = CaptionFontSize * LineBoxOverFontSize;

            return Math.Clamp(1 - (caption / band), 0.4, 0.9);
        }
    }

    /// <summary>
    /// How much taller the box around a line of type is than the type itself.
    /// </summary>
    /// <remarks>
    /// The figure every text layout works on, and it depends on the font's own
    /// metrics rather than on the size asked for. Lato sits near the usual one and
    /// a third is what it comes to here.
    /// </remarks>
    private const double LineBoxOverFontSize = 1.35;

    /// <summary>
    /// The size the value under the barcode is set at.
    /// </summary>
    /// <remarks>
    /// The size the printed label uses, which is five and a half points, rather than
    /// a size chosen to look right here. It is read against the rest of the label so
    /// a shop sees the proportion it will get: a caption set as large as the
    /// specification would say the label is something it is not.
    /// </remarks>
    public double CaptionFontSize => 5.5f * PointsToPixels;

    /// <summary>How much room the words have, which the barcode bands have taken from.</summary>
    public double TextRoomHeight => Layout.TextHeightMm(LabelBarcodes.Count) * SheetScale;

    /// <summary>
    /// Empty paper above the content, so a roll bigger than the label carries it in
    /// the middle rather than against its top edge.
    /// </summary>
    public double ContentTopOffset =>
        LabelType.Centring(Layout, LabelBarcodes.Count, LabelBodyPoint,
            LabelLines.Count, LabelLockLine.Length > 0) * SheetScale;

    /// <summary>The size the specification line is set at, scaled to the sheet.</summary>
    public double SpecFontSize => LabelBodyPoint * PointsToPixels;

    /// <summary>The size the charge count and the faults are set at.</summary>
    public double DetailFontSize => LabelBodyPoint * PointsToPixels;

    /// <summary>The size the locks line is set at, larger than everything else.</summary>
    public double LockFontSize => LabelLockPoint * PointsToPixels;

    /// <summary>
    /// The size the whole text block is set at, worked out from the same place the
    /// printed label works it out.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="LabelType"/> rather than measured here. A preview that
    /// sets its text at a size of its own choosing cannot answer the question it is
    /// on the panel for, which is whether the words will fit on the roll.
    /// </remarks>
    private float LabelBodyPoint =>
        LabelType.BlockSize(LabelLines, Layout, LabelBarcodes.Count);

    /// <summary>The locks line, which is set larger than the rest of the block.</summary>
    private float LabelLockPoint =>
        LabelType.LineSize(LabelLockLine, Layout, LabelBarcodes.Count,
            LabelBodyPoint * LabelType.LocksLargerThanBody, LabelBodyPoint);

    /// <summary>The lines the label carries, in the order it says them.</summary>
    private IReadOnlyList<string> LabelLines => new[]
        { LabelSpelled, LabelTextLine, LabelDetailLine, LabelLockLine }
        .Where(line => line.Length > 0).ToList();

    /// <summary>
    /// Points as preview pixels at the size this sheet is drawn.
    /// </summary>
    /// <remarks>
    /// Millimetres a point is, not the height of a line of type. A font size is the
    /// size of the letters, and the box a line of them occupies is about a fifth
    /// taller again, so converting with the line height and then handing the result
    /// over as a font size made every line a fifth taller than the space that had
    /// been measured for it. On a 28mm address label that fifth is the difference
    /// between the locks line fitting and being cut off the bottom.
    /// </remarks>
    private double PointsToPixels => MillimetresPerPoint * SheetScale;

    /// <summary>How many millimetres a point of type is, which is 72 to the inch.</summary>
    private const double MillimetresPerPoint = 25.4 / 72.0;

    /// <summary>
    /// The paper the printer cannot reach, as a margin on the sheet.
    /// </summary>
    /// <remarks>
    /// Drawn on the preview because it is on the label, and a preview that showed
    /// the printable area filling the whole sheet would be showing a label with no
    /// margins, which is not a thing a printer produces.
    /// </remarks>
    public Thickness SideInsetThickness => new(
        Layout.Stock.SideMarginMm * SheetScale,
        Layout.Stock.TopMarginMm * SheetScale);

    /// <summary>
    /// The same insets, with the empty paper above the content added on top.
    /// </summary>
    /// <remarks>
    /// A roll taller than the label's own content carries it in the middle. Against
    /// the top edge instead, a 59mm roll came out as a barcode, a hand's width of
    /// white, and then the words.
    /// </remarks>
    public Thickness BandInsetThickness => new(
        SideInsetThickness.Left,
        SideInsetThickness.Top + ContentTopOffset,
        SideInsetThickness.Right,
        0d);

    /// <summary>How tall it is, at the same ratio as the width, which is what keeps the shape.</summary>
    public double SheetHeight => SheetWidth / Layout.PaperWidthMm * Layout.PaperHeightMm;

    /// <summary>
    /// The widest the sheet may be drawn.
    /// </summary>
    /// <remarks>
    /// Set from the width the column actually has, and generous, because the sheet
    /// is the one thing on the panel the operator reads. At 300 pixels an 89mm
    /// label is drawn at three and a half pixels a millimetre, which puts its
    /// eight point words at about ten pixels high: the shape was right and the
    /// words on it were unreadable, which is the one thing a preview cannot be.
    /// </remarks>
    public const double MaxSheetWidth = 460;

    /// <summary>
    /// Recomputes whether the sheet had to be scaled down, for the line under the
    /// preview that says so.
    /// </summary>
    public void MeasureSheet(double availableWidth)
    {
        // The paper, because that is what the sheet is drawn as. Measuring the
        // printable area against it left the last few millimetres of every label
        // out of the sum, so a stock that fitted exactly reported that it had
        // been scaled down when it had not.
        double wanted = Layout.PaperWidthMm * PixelsPerMm;

        bool resized = Math.Abs(_available - availableWidth) > 0.5;
        _available = availableWidth;
        SheetOverflows = availableWidth > 0 && wanted > availableWidth;

        // This is called on every layout pass, and a pass that fires sixty times a
        // second must not write sixty property changes a second into a view that
        // will re-measure and lay out again on the back of it.
        if (resized)
        {
            this.RaisePropertyChanged(nameof(SheetWidth));
            this.RaisePropertyChanged(nameof(SheetHeight));
        }
    }

    /// <summary>
    /// How finely the sheet is drawn, chosen so a label fills the room its column
    /// has rather than sitting small in the middle of it.
    /// </summary>
    /// <remarks>
    /// At 96 dpi an 89mm address label came out 336 pixels wide in a column with
    /// room for 400, and 105 pixels high, which is not enough for a barcode band
    /// and three lines of words: the locks line came off the bottom. The panel
    /// already says the sheet is not life size whenever it does not fit, so drawing
    /// it as large as it will go is the better of the two, and the shape is
    /// unaffected because every measurement is scaled by the same figure.
    /// </remarks>
    private const double PixelsPerMm = 120.0 / 25.4;

    /// <summary>
    /// What the barcodes carry, in the order they are drawn.
    ///
    /// Built by the same code the writer builds its payloads with, so a preview
    /// showing one thing and a file carrying another is not possible rather than
    /// merely unlikely. Empty where a barcode is switched off, and the preview leaves
    /// the space out entirely rather than drawing an empty band.
    /// </summary>
    public IReadOnlyList<string> LabelBarcodes => new LabelCode(
            Label.Identifier, LabelLayout.ScannableLine(Label, LabelSymbology))
        .On(Layout.Stock, BarcodeMode, LabelSymbology, Label.IsIdentifiable).Barred;

    /// <summary>
    /// The values that will not fit a barcode on this stock, and are printed as
    /// words instead.
    /// </summary>
    /// <remarks>
    /// Empty on the stock most shops use. On the two small multi purpose rolls a
    /// fifteen digit identifier is wider than the paper at a width a scanner reads,
    /// so it is spelled out, and the preview has to say so: a label drawn here with
    /// a barcode that the file will not have is worse than no preview.
    /// </remarks>
    public IReadOnlyList<string> LabelSpelledBarcodes => new LabelCode(
            Label.Identifier, LabelLayout.ScannableLine(Label, LabelSymbology))
        .On(Layout.Stock, BarcodeMode, LabelSymbology, Label.IsIdentifiable).Spelled;

    /// <summary>The identifier alone, which is the first barcode in every mode.</summary>
    public string LabelBarcode => LabelBarcodes.Count > 0 ? LabelBarcodes[0] : "";

    /// <summary>The second code, in the modes that draw one.</summary>
    public string LabelBarcodeSecond => LabelBarcodes.Count > 1 ? LabelBarcodes[1] : "";

    /// <summary>Whether there is a barcode to draw at all.</summary>
    public bool HasLabelBarcode => LabelBarcodes.Count > 0;

    /// <summary>Whether a second barcode is drawn below the first.</summary>
    public bool HasSecondLabelBarcode => LabelBarcodes.Count > 1;

    /// <summary>What is printed as words in place of a barcode that would not fit.</summary>
    public string LabelSpelled => string.Join("  ", LabelSpelledBarcodes);

    /// <summary>What the barcode carries, as the setting holds it.</summary>
    public LabelBarcodeMode BarcodeMode => Barcode?.Mode ?? _main.LabelBarcodeMode;

    /// <summary>
    /// Which symbology the bars are drawn in, as the files and the preview want it.
    /// </summary>
    /// <remarks>
    /// Read from the picker and falling back to the settings, so a panel opened before
    /// the picker has settled still describes the label the export will write. The
    /// .dymo file ignores it and follows its own template, which is the right way
    /// round: DYMO draws that file rather than this app.
    /// </remarks>
    public LabelCodeSymbology LabelSymbology =>
        Symbology?.Symbology ?? _main.LabelSymbology;

    /// <summary>The symbologies, as the picker holds them.</summary>
    public ObservableCollection<LabelSymbologyItem> Symbologies { get; }

    public ObservableCollection<ExportOption> Options { get; }

    public ObservableCollection<LabelLayoutItem> Layouts { get; }

    public ObservableCollection<LabelBarcodeItem> BarcodeModes { get; }

    public ObservableCollection<PrinterOption> Printers { get; }

    public ObservableCollection<ExportResultItem> Results { get; }

    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    public ReactiveCommand<Unit, Unit> PrintLabelCommand { get; }

    public ReactiveCommand<Unit, Unit> DetectPrintersCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ReactiveCommand<string, Unit> OpenFileCommand { get; }

    public ReactiveCommand<ExportResultItem, Unit> PrintFileCommand { get; }

    /// <summary>One flag opens the panel. The summary screen owns it.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => this.RaiseAndSetIfChanged(ref _isOpen, value);
    }

    /// <summary>True while files are being written or a printer is being looked for.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanExport));
        }
    }

    /// <summary>
    /// The line under the buttons. It says what happened and where, because a
    /// successful export that the operator cannot locate is not a successful export.
    /// </summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>Whether the status line is reporting a failure.</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => this.RaiseAndSetIfChanged(ref _statusIsError, value);
    }

    /// <summary>True when at least one format is ticked, which is what enables the button.</summary>
    public bool CanExport
    {
        get => !IsBusy && Options.Any(option => option.Selected);
    }

    /// <summary>True once there is something to open a folder to.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Where the files go, named on the screen so nobody has to find it.</summary>
    public string Folder
    {
        get => _folder;
        private set
        {
            if (string.Equals(_folder, value, StringComparison.Ordinal)) return;
            _folder = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(FolderName));
        }
    }

    /// <summary>The folder as a name rather than a path. A full path on a button is unreadable.</summary>
    public string FolderName => Path.GetFileName(Folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
        ? name
        : Folder;

    /// <summary>The label stock, as the picker holds it.</summary>
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
            this.RaisePropertyChanged(nameof(SheetHeight));
            RaiseLabelChanged();
        }
    }

    /// <summary>
    /// The stock the files are written on. Falls back to the address label, which is
    /// what the shipped template describes, so a picker with nothing chosen still
    /// writes something that matches the template.
    /// </summary>
    public LabelLayout Layout => Stock?.Layout ?? LabelLayout.Address;

    /// <summary>
    /// What the barcode carries, as the picker holds it.
    ///
    /// A setting rather than a per export choice, because the mode decides how much
    /// room the barcodes take on the paper, and a label whose text position depends
    /// on something asked at print time is a label that moves under the operator.
    /// </summary>
    public LabelBarcodeItem? Barcode
    {
        get => _barcode;
        set
        {
            if (Equals(_barcode, value) || value is null) return;

            // A mode that cannot be drawn as bars on this roll is not a choice the
            // panel accepts, and the picker has already greyed it out. A settings
            // file naming one anyway, from an older version of the app or from a shop
            // that changed the roll since, falls back to the identifier alone rather
            // than writing a label whose barcode does not scan.
            LabelBarcodeItem wanted = value;
            LabelBarcodeItem use = wanted.IsAvailable ? wanted : LabelBarcodeItem.For(LabelBarcodeMode.Identifier);

            if (!ReferenceEquals(wanted, use))
            {
                this.RaiseAndSetIfChanged(ref _barcode, use, nameof(Barcode));
                LabelBarcodeItem.Select(use.Mode);
            }
            else
            {
                _barcode = value;
                LabelBarcodeItem.Select(value.Mode);
            }

            _main.SetLabelBarcodeMode(use.Mode);
            RaiseLabelChanged();
        }
    }

    /// <summary>
    /// Which symbology the barcode is drawn in, as the picker holds it.
    /// </summary>
    /// <remarks>
    /// A setting rather than a per export choice, for the same reason the mode is
    /// one: the symbology decides whether a code can be drawn at all, and a label
    /// whose contents move under the operator's hand is a label they cannot trust.
    /// </remarks>
    public LabelSymbologyItem? Symbology
    {
        get => _symbology;
        set
        {
            if (Equals(_symbology, value) || value is null) return;
            _symbology = value;
            LabelSymbologyItem.Select(value.Symbology);
            _main.SetLabelSymbology(value.Symbology);
            RaiseLabelChanged();
        }
    }

    /// <summary>The chosen printer, or null for the print dialog.</summary>
    public PrinterOption? Printer
    {
        get => _printer;
        set
        {
            if (Equals(_printer, value)) return;
            _printer = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasPrinter));
            this.RaisePropertyChanged(nameof(PrinterName));
        }
    }

    /// <summary>
    /// Whether a specific printer was chosen. False means the operating system's
    /// own print dialog is used, which is the right default: it is the only choice
    /// that works on a machine with no printers configured yet.
    /// </summary>
    public bool HasPrinter => Printer is not null;

    public string PrinterName => Printer?.Name ?? LocalizationManager.GetString("Export_PrintDialog");

    /// <summary>How many labels one print puts out.</summary>
    public int Copies
    {
        get => _copies;
        set => this.RaiseAndSetIfChanged(ref _copies, value < 1 ? 1 : value);
    }

    /// <summary>What the last print to DYMO came out as, so the operator is told the truth.</summary>
    public DymoPrintResult? LastDymoPrint
    {
        get => _lastDymoPrint;
        private set
        {
            this.RaiseAndSetIfChanged(ref _lastDymoPrint, value);
            this.RaisePropertyChanged(nameof(HasDymoPrintResult));
        }
    }

    public bool HasDymoPrintResult => LastDymoPrint is not null;

    /// <summary>
    /// The label as it will print, drawn from the same read of the device the
    /// files are written from. It is here so the operator can see a battery marker
    /// or a missing model before the label is on a device, rather than after.
    /// </summary>
    public LabelFields Label => LabelFields.From(
        _main.DeviceData, _main.Enable85PercentChecker, content: _main.LabelContent,);

    /// <summary>The first line of the preview: what the device is and what it is worth.</summary>
    public string LabelTextLine => LabelLayout.TextLine(Label);

    /// <summary>The second line: the charge count and the faults, empty on a clean phone.</summary>
    public string LabelDetailLine => LabelLayout.DetailLine(Label);

    /// <summary>The third line: the locks, on their own and never shared.</summary>
    public string LabelLockLine => LabelLayout.LockLine(Label);

    /// <summary>Whether the fault line has anything to say, so the preview can leave it out.</summary>
    public bool HasLabelDetail => LabelDetailLine.Length > 0;

    /// <summary>
    /// Whether a value had to be printed as words because no barcode of it would
    /// fit this stock at a width a scanner reads.
    /// </summary>
    /// <remarks>
    /// Said on the panel rather than left for the operator to work out from a
    /// missing barcode. It is the two small multi purpose rolls: a fifteen digit
    /// identifier is 271 units of Code39, which is 51.5mm at the narrowest bar a
    /// scanner reads, against the 48mm and 51mm those rolls have.
    /// </remarks>
    public bool HasSpelledBarcode => LabelSpelledBarcodes.Count > 0;

    /// <summary>Whether the charge count is on the label.</summary>
    public bool LabelShowBatteryCycles
    {
        get => _main.LabelShowBatteryCycles;
        set => _main.LabelShowBatteryCycles = value;
    }

    /// <summary>Whether the faults are on the label.</summary>
    public bool LabelShowFaults
    {
        get => _main.LabelShowFaults;
        set => _main.LabelShowFaults = value;
    }

    /// <summary>
    /// Whether the locks are on the label. A shop that turns this off should know
    /// that it removes the one line that stops a FRP locked phone being sold.
    /// </summary>
    public bool LabelShowLocks
    {
        get => _main.LabelShowLocks;
        set => _main.LabelShowLocks = value;
    }

    /// <summary>Whether the locks line has anything to say.</summary>
    public bool HasLabelLocks => LabelLockLine.Length > 0;

    /// <summary>
    /// Opens the panel for the device currently on screen. Anything from a previous
    /// device is dropped: a label preview from the last phone is worse than no
    /// preview.
    /// </summary>
    public void Open()
    {
        Results.Clear();
        LastDymoPrint = null;
        Status = "";
        StatusIsError = false;
        Printers.Clear();
        IsOpen = true;
        this.RaisePropertyChanged(nameof(HasResults));
        RaiseLabelChanged();
        this.RaisePropertyChanged(nameof(SheetHeight));

        // Printers are looked for without being waited on. The panel is useful
        // with no printer found, because most of what it does is writing files.
        _ = DetectPrintersAsync();
    }

    /// <summary>
    /// Tells the preview that everything drawn on it may have changed.
    ///
    /// One place, because the preview has three text lines and the faults among
    /// them appear and disappear as an inspection goes on. A line that is not
    /// announced leaves the previous phone's fault on the sheet, which is the one
    /// thing a preview must never do.
    /// </summary>
    private void RaiseLabelChanged()
    {
        // Before anything is read off the panel. The barcodes on the sheet are laid
        // out for a symbology, and a mode that cannot be drawn in it has to be off the
        // picker by the time anything binds to it, or the panel briefly shows a
        // setting it is about to grey out.
        LabelBarcodeItem.RefreshFor(
            Layout.Stock, LabelSymbology,
            new LabelCode(Label.Identifier, LabelLayout.ScannableLine(Label, LabelSymbology)),
            Label.IsIdentifiable);

        this.RaisePropertyChanged(nameof(Label));
        this.RaisePropertyChanged(nameof(LabelTextLine));
        this.RaisePropertyChanged(nameof(LabelDetailLine));
        this.RaisePropertyChanged(nameof(LabelLockLine));
        this.RaisePropertyChanged(nameof(HasLabelDetail));
        this.RaisePropertyChanged(nameof(HasLabelLocks));
        this.RaisePropertyChanged(nameof(LabelBarcode));
        this.RaisePropertyChanged(nameof(LabelBarcodeSecond));
        this.RaisePropertyChanged(nameof(LabelBarcodes));
        this.RaisePropertyChanged(nameof(LabelSpelledBarcodes));
        this.RaisePropertyChanged(nameof(LabelSpelled));
        this.RaisePropertyChanged(nameof(BarcodeBarShare));
        this.RaisePropertyChanged(nameof(CaptionFontSize));
        this.RaisePropertyChanged(nameof(ContentTopOffset));
        this.RaisePropertyChanged(nameof(BandInsetThickness));
        this.RaisePropertyChanged(nameof(HasLabelBarcode));
        this.RaisePropertyChanged(nameof(HasSecondLabelBarcode));
        this.RaisePropertyChanged(nameof(HasSpelledBarcode));
        this.RaisePropertyChanged(nameof(Barcode));
        this.RaisePropertyChanged(nameof(BarcodeMode));
        this.RaisePropertyChanged(nameof(Symbology));
        this.RaisePropertyChanged(nameof(LabelSymbology));
        this.RaisePropertyChanged(nameof(LabelShowBatteryCycles));
        this.RaisePropertyChanged(nameof(LabelShowFaults));
        this.RaisePropertyChanged(nameof(LabelShowLocks));
        this.RaisePropertyChanged(nameof(SheetWidth));
    }

    /// <summary>
    /// Reads the label settings again, after one of them changed elsewhere.
    ///
    /// The pickers on this panel and the switches on the settings page edit the same
    /// settings, so whichever changed the other has to be told: a panel left showing
    /// one stock while the files went out at another is the whole class of fault
    /// this preview exists to prevent.
    /// </summary>
    public void ReloadLabelSettings()
    {
        Stock = Layouts.FirstOrDefault(item =>
            string.Equals(item.Stock.PartNumber, _main.LabelStockPartNumber, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];
        Symbology = LabelSymbologyItem.For(_main.LabelSymbology);
        LabelSymbologyItem.Select(Symbology.Symbology);

        // Set straight rather than through the setter. The setter writes the mode back
        // to the settings, and a roll that has just changed can leave the mode the
        // operator had chosen unable to be drawn. Writing it back from here would
        // quietly replace their choice with a different one, and do it without the
        // panel showing anything having changed.
        _barcode = LabelBarcodeItem.For(_main.LabelBarcodeMode);
        LabelBarcodeItem.Select(_barcode.Mode);

        RaiseLabelChanged();
    }

    public void Close() => IsOpen = false;

    /// <summary>Called by each format when it is ticked or unticked.</summary>
    private void NoteSelection() => _selectionChanged.OnNext(true);

    /// <summary>
    /// Puts a finished batch on the screen: one row per format, and a status line
    /// that says what happened and where.
    ///
    /// Separate from the writing so that a batch can be shown without being
    /// written here, which is what lets the panel be photographed in the state an
    /// operator sees after an export without an export being run for every frame.
    /// </summary>
    public void Show(LabelWriter.Batch batch)
    {
        Results.Clear();
        foreach (LabelWriter.Outcome outcome in batch.Files)
            Results.Add(new ExportResultItem(outcome, batch.Folder));

        Folder = batch.Folder;
        Status = Summarise(batch);
        StatusIsError = !batch.Succeeded;
        this.RaisePropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Writes every ticked format. One click, because the operator's hands are on
    /// a phone and the alternative is remembering which combination produces which
    /// file.
    /// </summary>
    private async Task ExportAsync()
    {
        IsBusy = true;
        Status = LocalizationManager.GetString("Status_Exporting");
        StatusIsError = false;
        try
        {
            var wanted = Options.Where(option => option.Selected).Select(option => option.Format).ToHashSet();
            string stem = LabelWriter.FileStem(_main.DeviceData);

            // Named throughout. The request has eleven parameters and gained one more
            // when the barcode symbology became the operator's choice, which silently
            // turned four positional arguments here into the wrong ones.
            LabelWriter.Batch batch = await LabelWriter.WriteAsync(_main.DeviceData, new LabelWriter.Request(
                Formats: new HashSet<ExportFormat>(wanted),
                Folder: Folder,
                FileName: stem,
                TemplatePath: _main.LabelTemplatePath,
                Layout: Layout,
                Barcode: BarcodeMode,
                FlagLowBattery: _main.Enable85PercentChecker,
                Content: _main.LabelContent,
                Wording: ReportWordingBuilder.Current(),
                Messages: ExportWordingBuilder.Current(),));

            Show(batch);

            // A DYMO that is installed and switched on is worth using without a
            // second click. Failing here is not an error: the files are written.
            PrinterOption? dymo = Printer?.Dymo is { } found
                ? new PrinterOption(found.Name, found.Model, PrintService.Outcome.Sent, found)
                : Printers.FirstOrDefault(option => option.Dymo?.IsConnected == true);

            if (dymo?.Dymo is { } printer) await SendToDymoAsync(batch, printer);
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(CanExport));
        }
    }

    private async Task SendToDymoAsync(LabelWriter.Batch batch, DymoPrinter printer)
    {
        LabelWriter.Outcome? label = batch.Files
            .FirstOrDefault(file => file.Format == ExportFormat.DymoLabel && file.Succeeded);
        if (label?.Path is not { Length: > 0 } path) return;

        try
        {
            Uri? service = await DymoPrintService.FindAsync();
            if (service is null)
            {
                // The files are written and that is what the operator asked for.
                // Saying so plainly is better than reporting a failed print over a
                // successful export.
                LastDymoPrint = null;
                this.RaisePropertyChanged(nameof(HasDymoPrintResult));
                return;
            }

            LastDymoPrint = await DymoPrintService.PrintAsync(
                service, printer, File.ReadAllText(path), Copies);
            this.RaisePropertyChanged(nameof(HasDymoPrintResult));
        }
        catch (Exception ex)
        {
            LastDymoPrint = new DymoPrintResult(false, ex.Message);
            this.RaisePropertyChanged(nameof(HasDymoPrintResult));
        }
    }

    /// <summary>
    /// One line about the whole batch, saying both halves of a partly successful
    /// export once: how many arrived, which format did not, and where they went.
    ///
    /// Naming only the failure reads as "nothing was written", which is the reading
    /// that makes an operator press the button again over a device they have
    /// already sold.
    /// </summary>
    private static string Summarise(LabelWriter.Batch batch)
    {
        int written = batch.Files.Count(file => file.Succeeded);
        int failed = batch.Files.Count - written;

        if (failed == 0)
            return string.Format(
                LocalizationManager.GetString("Status_ExportDone"),
                written,
                batch.Folder);

        string whatFailed = string.Join(", ", batch.Failures.Select(file => ExportNames.Localized(file.Format)));

        return string.Format(
            LocalizationManager.GetString("Status_ExportPartly"),
            written,
            batch.Files.Count,
            whatFailed,
            batch.Folder);
    }

    /// <summary>
    /// Prints the label, and only the label. The panel's main button writes files;
    /// this one puts paper in a printer, which is a different thing to have
    /// happen by accident.
    /// </summary>
    private async Task PrintLabelAsync()
    {
        IsBusy = true;
        try
        {
            // Written to a scratch folder rather than the export folder: a file
            // that exists only to be printed should not sit in the operator's
            // records afterwards.
            string scratch = Path.Combine(Path.GetTempPath(), "PhoneGrade", "print");
            string stem = LabelWriter.FileStem(_main.DeviceData);

            LabelWriter.Batch batch = await LabelWriter.WriteAsync(_main.DeviceData, new LabelWriter.Request(
                Formats: new HashSet<ExportFormat> { ExportFormat.LabelPdf },
                Folder: scratch,
                FileName: stem,
                TemplatePath: _main.LabelTemplatePath,
                Layout: Layout,
                Barcode: BarcodeMode,
                FlagLowBattery: _main.Enable85PercentChecker,
                Content: _main.LabelContent,
                Messages: ExportWordingBuilder.Current(),));

            LabelWriter.Outcome? pdf = batch.Files.FirstOrDefault(file => file.Succeeded);
            if (pdf?.Path is not { Length: > 0 } path)
            {
                Status = batch.Failures.FirstOrDefault()?.Problem
                         ?? LocalizationManager.GetString("Export_PrintFailed");
                StatusIsError = true;
                return;
            }

            await PrintAsync(ExportFormat.LabelPdf, path);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Puts a PDF in front of a printer. With a printer chosen it goes straight
    /// there on the platforms that can, and everywhere else it opens the
    /// operating system's dialog. Nothing is printed silently anywhere.
    /// </summary>
    private async Task PrintAsync(ExportFormat format, string? knownPath = null)
    {
        IsBusy = true;
        try
        {
            string path = knownPath ?? await EnsureWrittenAsync(format);
            if (path.Length == 0) return;

            ExportWording words = ExportWordingBuilder.Current();
            PrintService.Attempt result = Printer is { Dymo: null, Kind: not PrintService.Outcome.Sent } chosen
                ? PrintService.PrintToQueue(path, chosen.Name, wording: words)
                : HasPrinter
                    ? PrintService.PrintToQueue(path, Printer!.Name, wording: words)
                    : PrintService.PrintDialog(path, words);

            Status = result.Message;
            StatusIsError = result.Outcome == PrintService.Outcome.Failed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Writes one format into the export folder if it is not already there.</summary>
    private async Task<string> EnsureWrittenAsync(ExportFormat format)
    {
        string stem = LabelWriter.FileStem(_main.DeviceData);
        string path = Path.Combine(Folder, $"{stem}{format.FileSuffix()}");
        if (File.Exists(path)) return path;

        LabelWriter.Batch batch = await LabelWriter.WriteAsync(_main.DeviceData, new LabelWriter.Request(
            Formats: new HashSet<ExportFormat> { format },
            Folder: Folder,
            FileName: stem,
            TemplatePath: _main.LabelTemplatePath,
            Layout: Layout,
            Barcode: BarcodeMode,
            FlagLowBattery: _main.Enable85PercentChecker,
            Content: _main.LabelContent,
            Messages: ExportWordingBuilder.Current(),));

        LabelWriter.Outcome? written = batch.Files.FirstOrDefault(file => file.Succeeded);
        if (written?.Path is { Length: > 0 } writtenPath)
        {
            Results.Add(new ExportResultItem(written, batch.Folder));
            this.RaisePropertyChanged(nameof(HasResults));
            return writtenPath;
        }

        Status = batch.Failures.FirstOrDefault()?.Problem
                 ?? LocalizationManager.GetString("Export_PrintFailed");
        StatusIsError = true;
        return "";
    }

    private async Task PrintFileAsync(ExportResultItem item)
    {
        if (item.Outcome.Path is not { Length: > 0 } path) return;

        IsBusy = true;
        try
        {
            ExportWording words = ExportWordingBuilder.Current();
            PrintService.Attempt result = Printer is { Kind: not PrintService.Outcome.Sent } chosen
                ? PrintService.PrintToQueue(path, chosen.Name, wording: words)
                : PrintService.PrintDialog(path, words);

            Status = result.Message;
            StatusIsError = result.Outcome == PrintService.Outcome.Failed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Looks for printers, and says what was found. Both sources are searched and
    /// neither is required: DYMO Connect for a DYMO, the print system for anything
    /// else.
    /// </summary>
    private async Task DetectPrintersAsync()
    {
        Printers.Clear();

        foreach (PrintQueue queue in PrintService.ListQueues())
            Printers.Add(new PrinterOption(queue.Name,
                queue.IsIdle
                    ? LocalizationManager.GetString("Export_PrinterReady")
                    : LocalizationManager.GetString("Export_PrinterBusy"),
                PrintService.Outcome.Sent));

        Uri? service = await DymoPrintService.FindAsync();
        if (service is not null)
        {
            foreach (DymoPrinter printer in await DymoPrintService.GetPrintersAsync(service))
                Printers.Add(new PrinterOption(printer.Name, printer.Model, PrintService.Outcome.Sent, printer));
        }

        // Nothing found is not a problem worth an error line: printing is one of
        // several things this panel does, and the files are written either way.
        if (Printer is null && Printers.Count > 0) Printer = Printers[0];
        this.RaisePropertyChanged(nameof(HasPrinter));
        this.RaisePropertyChanged(nameof(PrinterName));
    }

    private void OpenFolder()
    {
        PrintService.Attempt result = PrintService.OpenFolder(Folder, ExportWordingBuilder.Current());
        Status = result.Message;
        StatusIsError = result.Outcome == PrintService.Outcome.Failed;
    }
}

/// <summary>
/// The names of the formats, as the operator's language names them.
///
/// The Core project carries an English name for callers with no UI. The panel uses
/// this one instead, because a status line reading "DYMO label" to a Dutch
/// operator while every button around it is in Dutch is the sort of half
/// translation nobody mentions and everybody notices.
/// </summary>
internal static class ExportNames
{
    /// <summary>The name in the current language, from the app's own dictionaries.</summary>
    public static string Localized(ExportFormat format) =>
        LocalizationManager.GetString(format switch
        {
            ExportFormat.DymoLabel => "Export_FormatDymo",
            ExportFormat.LabelPdf => "Export_FormatLabelPdf",
            ExportFormat.ReportPdf => "Export_FormatReportPdf",
            ExportFormat.Json => "Export_FormatJson",
            _ => "Export_FormatCsv",
        });

    /// <summary>The name in English, for a caller with no dictionaries loaded.</summary>
    public static string English(ExportFormat format) => format switch
    {
        ExportFormat.DymoLabel => "DYMO label",
        ExportFormat.LabelPdf => "Label PDF",
        ExportFormat.ReportPdf => "Report PDF",
        ExportFormat.Json => "JSON",
        ExportFormat.Csv => "CSV",
        _ => format.ToString(),
    };
}
