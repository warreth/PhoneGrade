namespace PhoneGrade.Core;

/// <summary>
/// The label as it will come out of the printer, in millimetres.
///
/// One layout, read by both the .dymo template and the label PDF. They have to
/// agree: a label whose PDF puts the grade where the .dymo puts the battery is
/// two different labels depending on which button the operator pressed, and the
/// only way that is caught is by having one source of truth for the sizes.
/// </summary>
/// <param name="WidthMm">Label stock width. 106mm is a DYMO address label (30252).</param>
/// <param name="HeightMm">Label stock height.</param>
/// <param name="BarcodeHeightMm">Height of the barcode band, caption included.</param>
/// <param name="BarcodeYmm">Where the barcode band starts, from the top.</param>
/// <param name="TextYmm">Where the text block starts, from the top.</param>
/// <param name="TextHeightMm">Height reserved for the text block.</param>
/// <param name="MarginMm">Inset on the left and right.</param>
public sealed record LabelLayout(
    float WidthMm = 106f,
    float HeightMm = 57f,
    float BarcodeHeightMm = 14f,
    float BarcodeYmm = 5f,
    float TextYmm = 22f,
    float TextHeightMm = 26f,
    float MarginMm = 4f)
{
    /// <summary>A DYMO address label, which is what the shipped template describes.</summary>
    public static LabelLayout Address { get; } = new();

    /// <summary>A wide address label, for the 54mm tape.</summary>
    public static LabelLayout WideAddress { get; } = new(WidthMm: 159f, HeightMm: 57f);

    /// <summary>The stock sizes the export panel offers, by name for the settings file.</summary>
    public static IReadOnlyDictionary<string, LabelLayout> Presets { get; } =
        new Dictionary<string, LabelLayout>(StringComparer.OrdinalIgnoreCase)
        {
            ["106x57"] = Address,
            ["159x57"] = WideAddress,
            ["89x36"] = new(WidthMm: 89f, HeightMm: 36f, BarcodeHeightMm: 10f, BarcodeYmm: 3f,
                TextYmm: 14f, TextHeightMm: 18f, MarginMm: 3f),
        };

    /// <summary>The name the settings file stores this layout under.</summary>
    public string Key => Presets
        .FirstOrDefault(pair => pair.Value == this).Key
        ?? $"{WidthMm:0}x{HeightMm:0}";

    /// <summary>The first line: what the device is and what it is worth.</summary>
    public static string TextLine(LabelFields fields) =>
        $"{fields.Model} {fields.Storage} {fields.Color} {fields.Grade} {fields.Battery} {fields.PayMethod}";

    /// <summary>
    /// The second line: the charge count and the faults that are not locks.
    ///
    /// Its own line because it is the line a shop reads first. The first line says
    /// what the phone is; this one says what is wrong with it, and it is empty on a
    /// clean phone so its presence is itself the news. The locks are not repeated
    /// here because they get a line of their own, set larger.
    /// </summary>
    public static string DetailLine(LabelFields fields)
    {
        string faults = fields.Faults.FaultsOnly;
        string cycles = fields.BatteryCycles == DevicePlaceholders.BatteryCycles
            ? ""
            : fields.BatteryCycles + " CYCLES";

        return string.Join(" ", new[] { cycles, faults }.Where(part => part.Length > 0));
    }

    /// <summary>The third line: the locks, on their own and never shared.</summary>
    public static string LockLine(LabelFields fields) => fields.Faults.LockLine;

    /// <summary>How many text lines this label carries.</summary>
    public static int Lines(LabelFields fields) =>
        1 + (DetailLine(fields).Length > 0 ? 1 : 0) + (LockLine(fields).Length > 0 ? 1 : 0);

    /// <summary>
    /// The .dymo values, keyed by the sentinels the template carries, so the PDF
    /// and the label are filled from one read of the device.
    /// </summary>
    public IReadOnlyDictionary<string, string> DymoValues(LabelFields fields) => new Dictionary<string, string>
    {
        ["IDENTIFIER"] = fields.Identifier,
        ["MODEL"] = fields.Model,
        ["PCOLOR"] = fields.Color,
        ["BATTERY"] = fields.Battery,
        ["QUALITY"] = fields.Grade,
        ["PAYM"] = fields.PayMethod,
        ["STORAGE"] = fields.Storage,
        ["MEMORY"] = fields.Memory,
        ["CYCLES"] = fields.BatteryCycles,
        ["FAULTS"] = fields.Faults.Summary,
        ["LOCKS"] = fields.Faults.LockLine,
        ["DETAIL"] = LabelLayout.DetailLine(fields),
        ["SPEC"] = LabelLayout.TextLine(fields),
    };
}