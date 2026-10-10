using System.Reactive;
using PhoneGrade.Core;
using ReactiveUI;

namespace PhoneGrade.UI.ViewModels;

public partial class MainWindowViewModel
{

    /// <summary>
    /// The export panel. Owned here because it needs the device on screen, the
    /// settings and the battery threshold, and because the summary screen is where
    /// it is opened from. Declared nullable and set in the constructor so that the
    /// compiler is forced to say when it is missing.
    /// </summary>
    public ExportViewModel? ExportViewModel { get; private set; }

    /// <summary>
    /// The label settings and their live preview, for the settings page.
    /// </summary>
    /// <remarks>
    /// Separate from the export panel because it answers a different question: the
    /// panel is "what does this phone's label look like", the settings are "what
    /// does every label look like". They read the same stored values and both draw
    /// through the same plate.
    /// </remarks>
    public LabelSettingsViewModel? LabelSettings { get; private set; }

    /// <summary>
    /// The label template the export writes from, as the settings hold it. Empty
    /// means the template that came with the app, which is the right default and
    /// the reason a fresh install can print a label with nothing set up.
    /// </summary>
    public string LabelTemplatePath
    {
        get => _settings.TemplatePath ?? "";
        set
        {
            string chosen = value ?? "";
            if (_settings.TemplatePath == chosen) return;
            _settings.TemplatePath = chosen.Length > 0 ? chosen : null;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _labelTemplatePath, chosen);
        }
    }

    /// <summary>
    /// Whether the label that was written can be printed at all. False means no
    /// printer is known, which is worth saying on the summary screen rather than
    /// finding out at the till.
    /// </summary>
    public bool HasPrinter => PrintService.ListQueues().Count > 0;

    /// <summary>
    /// Whether the export panel is open. The panel is a full overlay rather than a
    /// window, because the summary screen underneath it is where the operator came
    /// from and where they go back to, and a separate window would put two
    /// windows on top of each other with no visual order between them.
    /// </summary>
    public bool IsExportOpen
    {
        get => ExportViewModel?.IsOpen ?? false;
        set
        {
            if (ExportViewModel is null) return;
            if (value) ExportViewModel.Open();
            else ExportViewModel.Close();
        }
    }

    // ============ What the label says ============
    //
    // Six settings that decide what is printed on a label, kept together because
    // they are read together: the paper and the barcodes take up the space the
    // words go in, so changing one changes what fits of the others.

    /// <summary>
    /// The DYMO part number of the roll in the printer.
    ///
    /// Read as a part number rather than as millimetres because that is how the roll
    /// is ordered and how it is recognised: 89x28 and 89x36 are both "address", and
    /// an operator holding the wrong one has no way to tell from the name.
    /// </summary>
    public string LabelStockPartNumber
    {
        get => _labelStockPartNumber;
        set
        {
            string wanted = LabelStock.FromPartNumber(value).PartNumber;
            _labelStockPartNumber = wanted;
            _settings.LabelStockPartNumber = wanted;
            _settings.Save();
            this.RaiseAndSetIfChanged(ref _labelStockPartNumber, wanted);
            LabelSettingsChanged();
        }
    }

    /// <summary>What the barcode carries: the identifier, the pair, or one of each.</summary>
    public LabelBarcodeMode LabelBarcodeMode
    {
        get => _settings.LabelBarcodeMode;
        set
        {
            _settings.LabelBarcodeMode = value;
            _settings.Save();
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(EffectiveLabelBarcodeMode));
            LabelSettingsChanged();
        }
    }

    /// <summary>
    /// Which symbology the label's barcode is drawn in.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than a part of the mode, because the two answer
    /// different questions: the mode says what the code carries and the symbology says
    /// how it is written down. A shop that wants one code carrying everything needs
    /// both of them chosen, and a shop whose scanner only reads Code39 needs the
    /// other pairing.
    /// </remarks>
    public LabelCodeSymbology LabelSymbology
    {
        get => _settings.LabelSymbology;
        set
        {
            _settings.LabelSymbology = value;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>Called by the label settings picker, which owns the collection.</summary>
    public void SetLabelStock(LabelStock stock)
    {
        if (string.Equals(_labelStockPartNumber, stock.PartNumber, StringComparison.Ordinal)) return;
        _labelStockPartNumber = stock.PartNumber;
        _settings.LabelStockPartNumber = stock.PartNumber;
        _settings.Save();
        this.RaiseAndSetIfChanged(ref _labelStockPartNumber, stock.PartNumber);
        this.RaisePropertyChanged(nameof(LabelStock));
        LabelSettingsChanged();
    }

    /// <summary>Called by the label settings picker.</summary>
    public void SetLabelSymbology(LabelCodeSymbology symbology) => LabelSymbology = symbology;

    /// <summary>Called by the label settings picker.</summary>
    public void SetLabelBarcodeMode(LabelBarcodeMode mode)
    {
        if (_settings.LabelBarcodeMode == mode) return;
        _settings.LabelBarcodeMode = mode;
        _settings.Save();
        this.RaisePropertyChanged(nameof(LabelBarcodeMode));
        this.RaisePropertyChanged(nameof(EffectiveLabelBarcodeMode));
        LabelSettingsChanged();
    }

    /// <summary>The stock, as the label writers take it.</summary>
    public LabelStock LabelStock => LabelStock.FromPartNumber(_labelStockPartNumber);

    /// <summary>
    /// The switches that decide what the label says, as the writers take them.
    /// </summary>
    /// <remarks>
    /// Gathered into one object so the label's wording is passed around as a single
    /// value. Every renderer takes it, which is the only way the preview can promise
    /// it is showing what the file will contain.
    ///
    /// The faults and the locks are always on. They used to be switches, and a switch
    /// that takes the locks off a label takes off the one line that stops a FRP locked
    /// phone being sold; the panel wording said so and it was still the wrong thing to
    /// offer. A clean phone carries neither line because there is nothing to say, not
    /// because somebody turned the line off.
    /// </remarks>
    public LabelContent LabelContent => new(
        BatteryCycles: _settings.LabelShowBatteryCycles,
        Faults: true,
        Locks: true);

    /// <summary>Whether the charge count is on the label at all.</summary>
    public bool LabelShowBatteryCycles
    {
        get => _settings.LabelShowBatteryCycles;
        set { _settings.LabelShowBatteryCycles = value; _settings.Save(); this.RaiseAndSetIfChanged(ref _labelShowBatteryCycles, value); LabelSettingsChanged(); }
    }

    /// <summary>
    /// How the label is arranged: the single specification line, the structured
    /// arrangement, or the grade as a block of its own.
    /// </summary>
    public LabelVariant LabelVariant
    {
        get => _settings.LabelVariant;
        set { _settings.LabelVariant = value; _settings.Save(); this.RaisePropertyChanged(); LabelSettingsChanged(); }
    }

    /// <summary>
    /// Whether the label carries a barcode at all.
    /// </summary>
    /// <remarks>
    /// Off is for the shop whose scanner cannot read one; the barcode itself is one
    /// Code39 code with the serial number, which is what every till reads. What the
    /// code carries and which symbology it is drawn in stay in the label settings
    /// behind this switch, because a shop that scans nothing has no use for either.
    /// </remarks>
    public bool LabelBarcodeEnabled
    {
        get => _settings.LabelBarcodeEnabled;
        set
        {
            _settings.LabelBarcodeEnabled = value;
            _settings.Save();
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(LabelBarcodeMode));
            LabelSettingsChanged();
        }
    }

    /// <summary>The barcode mode the writers actually use: none when the switch is off.</summary>
    public LabelBarcodeMode EffectiveLabelBarcodeMode =>
        _settings.LabelBarcodeEnabled ? _settings.LabelBarcodeMode : LabelBarcodeMode.None;

    /// <summary>
    /// The battery percentage under which the marker goes on the label, and under
    /// which the report counts the battery as a deviation.
    /// </summary>
    public int LabelBatteryThreshold
    {
        get => _settings.LabelBatteryThreshold;
        set
        {
            int wanted = Math.Clamp(value, 50, 95);
            if (_settings.LabelBatteryThreshold == wanted) return;
            _settings.LabelBatteryThreshold = wanted;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>Below this charge count the number is left off the label.</summary>
    public int LabelCyclesThreshold
    {
        get => _settings.LabelCyclesThreshold;
        set
        {
            int wanted = Math.Clamp(value, 0, 5000);
            if (_settings.LabelCyclesThreshold == wanted) return;
            _settings.LabelCyclesThreshold = wanted;
            _settings.Save();
            this.RaisePropertyChanged();
            LabelSettingsChanged();
        }
    }

    /// <summary>The files a finished inspection writes without being asked.</summary>
    public List<ExportFormat> ExportFormats
    {
        get => _settings.ExportFormats;
        set
        {
            _settings.ExportFormats = value;
            _settings.Save();
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Whether a format is in the set a finish writes.</summary>
    public bool WritesFormat(ExportFormat format) => _settings.ExportFormats.Contains(format);

    /// <summary>Adds or removes one format from the set a finish writes.</summary>
    public void SetWritesFormat(ExportFormat format, bool wanted)
    {
        var formats = new List<ExportFormat>(_settings.ExportFormats);
        if (wanted && !formats.Contains(format)) formats.Add(format);
        if (!wanted) formats.Remove(format);

        ExportFormats = formats;
    }

    /// <summary>How the exports are grouped in the folder the operator sees.</summary>
    public ExportFolderScheme ExportFolderScheme
    {
        get => _settings.ExportFolderScheme;
        set { _settings.ExportFolderScheme = value; _settings.Save(); this.RaisePropertyChanged(); }
    }

    /// <summary>Tells the preview and the writers that the label's wording changed.</summary>
    private void LabelSettingsChanged()
    {
        ExportViewModel?.ReloadLabelSettings();
        LabelSettings?.Refresh();
    }

    /// <summary>Opens the export panel. One screen for the label, the report and the numbers.</summary>
    public ReactiveCommand<Unit, Unit> OpenExportCommand { get; }

}
