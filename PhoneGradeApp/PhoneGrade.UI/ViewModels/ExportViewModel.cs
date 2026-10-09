using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using PhoneGrade.Core;
using PhoneGrade.UI.Services;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

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
/// The finish panel: the last screen of an inspection.
///
/// One action, the one every shop performs on every phone: print the label and file
/// the record. The label is drawn on screen exactly as it will print, from the same
/// plate the PDF is written from, and one button does the rest. What a shop
/// configures once - the roll, the arrangement, the barcode, the files, the folder -
/// lives in the settings and is shown here as a list, not asked again.
///
/// The one thing that can be different for a single inspection - another roll, no
/// print, the numbers as well - sits behind "Deze keer anders", collapsed, so the
/// everyday case is a preview and a button.
///
/// A phone that did not report one of the four values a label cannot do without is
/// not labelled. The panel says which value is missing and opens the editor, and it
/// offers to file the record without a label rather than printing a tag with a
/// placeholder on it.
/// </summary>
public class ExportViewModel : ReactiveObject
{
    private readonly MainWindowViewModel _main;

    private bool _isOpen;
    private bool _isBusy;
    private string _status = "";
    private bool _statusIsError;
    private string _folder = ExportService.ExportDir;
    private LabelLayoutItem? _stock;
    private PrinterOption? _printer;
    private bool _saveOnly;
    private bool _extraFiles;
    private bool _confirmed;
    private LabelPlate? _plate;
    private LabelFields? _label;

    public ExportViewModel(MainWindowViewModel main)
    {
        _main = main;

        Layouts = new ObservableCollection<LabelLayoutItem>(
            LabelStock.All.Select(stock => new LabelLayoutItem(stock)));
        Printers = new ObservableCollection<PrinterOption>();
        Results = new ObservableCollection<ExportResultItem>();

        // Seeded from the settings, and never written back: the roll in the fold is
        // for this inspection only, and the standard stays what the settings say.
        _stock = Layouts.FirstOrDefault(item =>
            string.Equals(item.Stock.PartNumber, main.LabelStockPartNumber, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];

        var notBusy = this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy);
        var hasFormats = this.WhenAnyValue(x => x.HasFormats);

        // Settings the panel displays but does not own: a change on the settings page
        // has to move the list and the folder line here, or the panel keeps showing
        // what the shop changed a minute ago.
        _main.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MainWindowViewModel.ExportFormats):
                    this.RaisePropertyChanged(nameof(HasFormats));
                    this.RaisePropertyChanged(nameof(CanFinish));
                    this.RaisePropertyChanged(nameof(ConfiguredFormats));
                    break;
                case nameof(MainWindowViewModel.ExportFolderScheme):
                    this.RaisePropertyChanged(nameof(ConfiguredFolder));
                    this.RaisePropertyChanged(nameof(FolderPreview));
                    this.RaisePropertyChanged(nameof(FolderLine));
                    break;
            }
        };

        // CombineLatest rather than And: both inputs are re-checked whenever either
        // one changes, which is what lets clearing the last format disable the button.
        var canFinish = Observable.CombineLatest(notBusy, hasFormats,
            (idle, formats) => idle && formats);
        canFinish.Subscribe(_ => this.RaisePropertyChanged(nameof(CanFinish)));

        // The button binds to CanFinish rather than to the command's own
        // can-execute, because a button that looks enabled and refuses to run is
        // the operator pressing it twice.
        FinishCommand = ReactiveCommand.CreateFromTask(FinishAsync, canFinish);
        DetectPrintersCommand = ReactiveCommand.CreateFromTask(DetectPrintersAsync, notBusy);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        CloseCommand = ReactiveCommand.Create(Close);
        OpenFileCommand = ReactiveCommand.Create<string>(path => _ = PrintService.Open(path, ExportWordingBuilder.Current()));
        PrintFileCommand = ReactiveCommand.Create<ExportResultItem>(PrintFile);
        FixCommand = ReactiveCommand.Create(() => { _main.OpenEditorCommand.Execute().Subscribe(_ => { }); });
        OpenLabelSettingsCommand = ReactiveCommand.Create(() =>
        {
            Close();
            _main.IsSettingsDrawerOpen = true;
            _main.SelectedSettingsSection = "Label";
        });
        NextDeviceCommand = ReactiveCommand.Create(() =>
        {
            Close();
            _main.BackToIdleCommand.Execute().Subscribe(_ => { });
        });

        RaiseLabelChanged();
    }

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
            this.RaisePropertyChanged(nameof(CanFinish));
        }
    }

    /// <summary>
    /// The line under the buttons. It says what happened and where, because a
    /// successful finish that the operator cannot locate is not a successful finish.
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

    /// <summary>True once the finish ran and the label and files are where they belong.</summary>
    public bool Confirmed
    {
        get => _confirmed;
        private set => this.RaiseAndSetIfChanged(ref _confirmed, value);
    }

    /// <summary>The label as it will print, drawn from the same read of the device the files are written from.</summary>
    public LabelFields Label => _label ??= LabelPreview.Fields(_main);

    /// <summary>The drawing plan the sheet and the PDF are both rendered from.</summary>
    public LabelPlate? Plate
    {
        get => _plate;
        private set => this.RaiseAndSetIfChanged(ref _plate, value);
    }

    /// <summary>
    /// The values a label cannot be drawn without that are still missing. Empty on a
    /// phone that can be labelled.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<LabelField> MissingFields => Label.MissingForLabel;

    /// <summary>Whether the label action is held back until the operator fills a value in.</summary>
    public bool IsLabelBlocked => MissingFields.Count > 0;

    /// <summary>The missing values as one sentence fragment, in the operator's language.</summary>
    public string MissingFieldsText => string.Join(", ",
        MissingFields.Select(field => LocalizationManager.GetString(field switch
        {
            LabelField.Identifier => "LabelField_Identifier",
            LabelField.Model => "LabelField_Model",
            LabelField.Grade => "LabelField_Grade",
            _ => "LabelField_Color",
        })));

    /// <summary>The roll for this inspection, seeded from the settings.</summary>
    public LabelLayoutItem? Stock
    {
        get => _stock;
        set
        {
            if (Equals(_stock, value) || value is null) return;
            _stock = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(Layout));
            RaiseLabelChanged();
        }
    }

    /// <summary>The stock the files are written on. Falls back to the address label.</summary>
    public LabelLayout Layout => Stock?.Layout ?? LabelLayout.Address;

    /// <summary>The stocks the fold offers.</summary>
    public ObservableCollection<LabelLayoutItem> Layouts { get; }

    /// <summary>
    /// Whether this finish only files the record and does not print.
    /// </summary>
    /// <remarks>
    /// For a phone that is not ready for the shelf yet, and for a machine with no
    /// printer on it. It is a per inspection choice rather than a setting because it
    /// is about this phone, not about the shop.
    /// </remarks>
    public bool SaveOnly
    {
        get => _saveOnly;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _saveOnly, value)) return;
            this.RaisePropertyChanged(nameof(PrimaryLabelKey));
        }
    }

    /// <summary>
    /// Whether the numbers (JSON and CSV) are written as well, on top of the files
    /// the shop configured.
    /// </summary>
    public bool ExtraFiles
    {
        get => _extraFiles;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref _extraFiles, value)) return;
            this.RaisePropertyChanged(nameof(HasFormats));
            this.RaisePropertyChanged(nameof(CanFinish));
        }
    }

    /// <summary>Whether anything at all will be written when the button is pressed.</summary>
    public bool HasFormats => _main.ExportFormats.Count > 0 || ExtraFiles;

    /// <summary>True when the button can run: something to write, and not busy.</summary>
    public bool CanFinish => HasFormats && !IsBusy;

    /// <summary>
    /// What the big button says. With printing on it is the whole job; with the
    /// print ticked off it is only the filing, and a button that said print over a
    /// file that will not print is the kind of promise this panel must not make.
    /// </summary>
    public string PrimaryLabelKey => SaveOnly ? "Export_BtnSaveAndFinish" : "Export_BtnPrintAndFinish";

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
            this.RaisePropertyChanged(nameof(ConfiguredPrinter));
        }
    }

    /// <summary>
    /// Whether a specific printer was chosen. False means the operating system's
    /// own print dialog is used, which is the right default: it is the only choice
    /// that works on a machine with no printers configured yet.
    /// </summary>
    public bool HasPrinter => Printer is not null;

    /// <summary>The printers found, from the print system and from DYMO Connect.</summary>
    public ObservableCollection<PrinterOption> Printers { get; }

    /// <summary>What was written, newest finish first. Empty before anything ran.</summary>
    public ObservableCollection<ExportResultItem> Results { get; }

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

    /// <summary>
    /// The folder this finish would land in, relative to the exports folder, for the
    /// line under the button. Worked out here rather than after the writing, because
    /// the operator is told where the files are going before pressing anything.
    /// </summary>
    public string FolderPreview
    {
        get
        {
            string resolved = ResolveFolder();
            string relative = Path.GetRelativePath(ExportService.ExportDir, resolved);
            return relative.StartsWith("..", StringComparison.Ordinal)
                ? resolved
                : Path.Combine("export", relative);
        }
    }

    // ============ The "as configured" list ============

    /// <summary>The arrangement, in the operator's language.</summary>
    public string ConfiguredVariant => LocalizationManager.GetString(_main.LabelVariant switch
    {
        LabelVariant.Structured => "Settings_LabelVariantStructured",
        LabelVariant.GradeBlock => "Settings_LabelVariantGradeBlock",
        _ => "Settings_LabelVariantClean",
    });

    /// <summary>The files a finish writes, in the operator's language.</summary>
    public string ConfiguredFormats
    {
        get
        {
            var names = _main.ExportFormats.Select(ExportNames.Localized).ToList();
            if (ExtraFiles)
            {
                names.Add(ExportNames.Localized(ExportFormat.Json));
                names.Add(ExportNames.Localized(ExportFormat.Csv));
            }

            return names.Count > 0
                ? string.Join(", ", names)
                : LocalizationManager.GetString("Export_ConfiguredNoFiles");
        }
    }

    /// <summary>How the exports are grouped, in the operator's language.</summary>
    public string ConfiguredFolder => LocalizationManager.GetString(_main.ExportFolderScheme switch
    {
        ExportFolderScheme.Day => "Settings_FolderDay",
        ExportFolderScheme.Month => "Settings_FolderMonth",
        ExportFolderScheme.Inspection => "Settings_FolderInspection",
        _ => "Settings_FolderWeek",
    });

    /// <summary>
    /// The line under the button: where this inspection's files will land, worked
    /// out before anything is pressed so the promise is made before the fact.
    /// </summary>
    public string FolderLine => string.Format(
        LocalizationManager.GetString("Export_FolderLine"), FolderPreview);

    /// <summary>The roll, named the way the picker names it.</summary>
    public string ConfiguredRoll
    {
        get
        {
            string key = $"LabelStock_{_main.LabelStockPartNumber}";
            string shown = LocalizationManager.GetString(key);
            return shown == key ? _main.LabelStockPartNumber : shown;
        }
    }

    /// <summary>The printer, or the system dialog when none is chosen.</summary>
    public string ConfiguredPrinter => Printer?.Name ?? LocalizationManager.GetString("Export_PrintDialog");

    public ReactiveCommand<Unit, Unit> FinishCommand { get; }

    public ReactiveCommand<Unit, Unit> DetectPrintersCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ReactiveCommand<string, Unit> OpenFileCommand { get; }

    public ReactiveCommand<ExportResultItem, Unit> PrintFileCommand { get; }

    /// <summary>Opens the data editor on the values a label is still missing.</summary>
    public ReactiveCommand<Unit, Unit> FixCommand { get; }

    /// <summary>Closes the panel and opens the label settings, where the standard is changed.</summary>
    public ReactiveCommand<Unit, Unit> OpenLabelSettingsCommand { get; }

    /// <summary>Closes the panel and goes back to the idle screen for the next phone.</summary>
    public ReactiveCommand<Unit, Unit> NextDeviceCommand { get; }

    /// <summary>
    /// Opens the panel for the device currently on screen. Anything from a previous
    /// device is dropped: a label preview from the last phone is worse than no
    /// preview.
    /// </summary>
    public void Open()
    {
        Results.Clear();
        Confirmed = false;
        Status = "";
        StatusIsError = false;
        Printers.Clear();
        SaveOnly = false;
        ExtraFiles = false;

        // The roll comes back from the settings, because the fold's roll is for one
        // inspection only.
        Stock = Layouts.FirstOrDefault(item =>
            string.Equals(item.Stock.PartNumber, _main.LabelStockPartNumber, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];

        _label = null;
        IsOpen = true;
        this.RaisePropertyChanged(nameof(HasResults));
        RaiseLabelChanged();
        this.RaisePropertyChanged(nameof(FolderPreview));

        // Printers are looked for without being waited on. The panel is useful
        // with no printer found, because most of what it does is writing files.
        _ = DetectPrintersAsync();
    }

    /// <summary>
    /// Reads the label settings again, after one of them changed elsewhere.
    ///
    /// The pickers on the settings page and the panel edit the same settings, so
    /// whichever changed the other has to be told: a panel left showing one stock
    /// while the files went out at another is the whole class of fault the preview
    /// exists to prevent.
    /// </summary>
    public void ReloadLabelSettings()
    {
        _label = null;
        RaiseLabelChanged();
        this.RaisePropertyChanged(nameof(FolderPreview));
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
        Plate = LabelPreview.Plate(
            _main, Layout, EffectiveBarcode, _main.LabelSymbology, _main.LabelVariant);

        this.RaisePropertyChanged(nameof(Label));
        this.RaisePropertyChanged(nameof(Plate));
        this.RaisePropertyChanged(nameof(MissingFields));
        this.RaisePropertyChanged(nameof(IsLabelBlocked));
        this.RaisePropertyChanged(nameof(MissingFieldsText));
        this.RaisePropertyChanged(nameof(ConfiguredVariant));
        this.RaisePropertyChanged(nameof(ConfiguredFormats));
        this.RaisePropertyChanged(nameof(ConfiguredFolder));
        this.RaisePropertyChanged(nameof(ConfiguredRoll));
        this.RaisePropertyChanged(nameof(ConfiguredPrinter));
        this.RaisePropertyChanged(nameof(FolderLine));
    }

    /// <summary>The barcode mode the writers use: none when the switch is off.</summary>
    private LabelBarcodeMode EffectiveBarcode =>
        _main.LabelBarcodeEnabled ? _main.LabelBarcodeMode : LabelBarcodeMode.None;

    public void Close() => IsOpen = false;

    /// <summary>
    /// Puts a finished batch on the screen: one row per format, and a status line
    /// that says what happened and where.
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
    /// Writes the configured files and, unless the operator ticked print off, prints
    /// the label. One click, because the operator's hands are on a phone and the
    /// alternative is remembering which combination produces which file.
    /// </summary>
    private async Task FinishAsync()
    {
        IsBusy = true;
        Confirmed = false;
        Status = LocalizationManager.GetString("Status_Exporting");
        StatusIsError = false;
        try
        {
            string stem = LabelWriter.FileStem(_main.DeviceData);
            Folder = ResolveFolder();

            var wanted = new System.Collections.Generic.HashSet<ExportFormat>(_main.ExportFormats);
            if (ExtraFiles)
            {
                wanted.Add(ExportFormat.Json);
                wanted.Add(ExportFormat.Csv);
            }

            // A label that is missing a serial or a grade is not printed, whatever
            // the configured set says; the record is still filed, because losing the
            // record over a missing colour would be the worse fault.
            if (IsLabelBlocked)
            {
                wanted.Remove(ExportFormat.DymoLabel);
                wanted.Remove(ExportFormat.LabelPdf);
            }

            LabelWriter.Batch batch = await LabelWriter.WriteAsync(_main.DeviceData, new LabelWriter.Request(
                Formats: wanted,
                Folder: Folder,
                FileName: stem,
                TemplatePath: _main.LabelTemplatePath,
                Layout: Layout,
                Barcode: EffectiveBarcode,
                Symbology: _main.LabelSymbology,
                FlagLowBattery: _main.Enable85PercentChecker,
                Content: _main.LabelContent,
                Wording: ReportWordingBuilder.Current(),
                Messages: ExportWordingBuilder.Current(),
                Colour: Services.ColorWording.OnLabel,
                Variant: _main.LabelVariant,
                BatteryThreshold: _main.LabelBatteryThreshold,
                CyclesMinimum: _main.LabelCyclesThreshold));

            Show(batch);

            if (batch.Succeeded && SaveOnly)
            {
                Confirmed = true;
                return;
            }

            // Printing is the promise of the button, whatever the configured set
            // says: a shop that files a report and no label file still prints its
            // label, and the scratch PDF covers the case where the label PDF is not
            // one of the files written.
            if (!IsLabelBlocked && batch.Succeeded)
                await PrintLabelAsync(batch);
            else if (batch.Succeeded)
                Confirmed = true;
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(CanFinish));
        }
    }

    /// <summary>
    /// Puts the label on paper: straight to a connected DYMO when there is one,
    /// through the label PDF and the print system everywhere else. Failing here is
    /// not a failed finish; the files are written and the status line says what
    /// happened.
    /// </summary>
    private async Task PrintLabelAsync(LabelWriter.Batch batch)
    {
        // A DYMO that is installed and switched on is worth using without a second
        // click.
        PrinterOption? dymo = Printer?.Dymo is not null
            ? Printer
            : Printers.FirstOrDefault(option => option.Dymo?.IsConnected == true);

        LabelWriter.Outcome? label = batch.Files
            .FirstOrDefault(file => file.Format == ExportFormat.DymoLabel && file.Succeeded);

        if (dymo?.Dymo is { } printer && label?.Path is { Length: > 0 } labelPath)
        {
            try
            {
                Uri? service = await DymoPrintService.FindAsync();
                if (service is not null)
                {
                    DymoPrintResult print = await DymoPrintService.PrintAsync(
                        service, printer, File.ReadAllText(labelPath), Copies);

                    if (print.Accepted)
                    {
                        Status = string.Format(
                            LocalizationManager.GetString("Export_PrintedAndSaved"), FolderName);
                        Confirmed = true;
                    }
                    else
                    {
                        Status = print.Message;
                        StatusIsError = true;
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                Status = ex.Message;
                StatusIsError = true;
                return;
            }
        }

        // Everything that is not a DYMO goes through the label PDF. The configured
        // set does not have to contain it: a shop that files a .dymo and a report
        // still prints, so the PDF is written to a scratch folder when it is needed.
        string? pdf = batch.Files
            .FirstOrDefault(file => file.Format == ExportFormat.LabelPdf && file.Succeeded)?.Path;

        if (pdf is null)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "PhoneGrade", "print");
            LabelWriter.Batch one = await LabelWriter.WriteAsync(_main.DeviceData, new LabelWriter.Request(
                Formats: new System.Collections.Generic.HashSet<ExportFormat> { ExportFormat.LabelPdf },
                Folder: scratch,
                FileName: LabelWriter.FileStem(_main.DeviceData),
                TemplatePath: _main.LabelTemplatePath,
                Layout: Layout,
                Barcode: EffectiveBarcode,
                Symbology: _main.LabelSymbology,
                FlagLowBattery: _main.Enable85PercentChecker,
                Content: _main.LabelContent,
                Messages: ExportWordingBuilder.Current(),
                Colour: Services.ColorWording.OnLabel,
                Variant: _main.LabelVariant,
                BatteryThreshold: _main.LabelBatteryThreshold,
                CyclesMinimum: _main.LabelCyclesThreshold));

            pdf = one.Files.FirstOrDefault(file => file.Succeeded)?.Path;
        }

        if (pdf is null)
        {
            Status = LocalizationManager.GetString("Export_PrintFailed");
            StatusIsError = true;
            return;
        }

        ExportWording words = ExportWordingBuilder.Current();
        PrintService.Attempt result = HasPrinter
            ? PrintService.PrintToQueue(pdf, Printer!.Name, wording: words)
            : PrintService.PrintDialog(pdf, words);

        Status = result.Message;
        StatusIsError = result.Outcome == PrintService.Outcome.Failed;
        if (!StatusIsError) Confirmed = true;
    }

    /// <summary>How many labels one print puts out.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Where this finish's files go, from the folder scheme setting.</summary>
    private string ResolveFolder() => ExportFolders.FolderUnder(
        ExportService.ExportDir,
        _main.ExportFolderScheme,
        DateTime.Now,
        LabelWriter.DeviceToken(_main.DeviceData));

    private void PrintFile(ExportResultItem item)
    {
        if (item.Outcome.Path is not { Length: > 0 } path) return;

        IsBusy = true;
        try
        {
            ExportWording words = ExportWordingBuilder.Current();
            PrintService.Attempt result = HasPrinter
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

    /// <summary>
    /// One line about the whole batch, saying both halves of a partly successful
    /// finish once: how many arrived, which format did not, and where they went.
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
        this.RaisePropertyChanged(nameof(ConfiguredPrinter));
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
