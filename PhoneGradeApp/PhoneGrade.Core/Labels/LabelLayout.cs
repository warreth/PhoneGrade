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
/// <param name="BarcodeHeightMm">Height of the barcode band.</param>
/// <param name="BarcodeYmm">Where the barcode band starts, from the top.</param>
/// <param name="TextYmm">Where the text line starts, from the top.</param>
/// <param name="TextHeightMm">Height of the text line.</param>
/// <param name="MarginMm">Inset on the left and right.</param>
public sealed record LabelLayout(
    float WidthMm = 106f,
    float HeightMm = 57f,
    float BarcodeHeightMm = 18f,
    float BarcodeYmm = 6f,
    float TextYmm = 30f,
    float TextHeightMm = 9f,
    float MarginMm = 6f)
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
            ["89x36"] = new(WidthMm: 89f, HeightMm: 36f, BarcodeHeightMm: 11f, BarcodeYmm: 4f,
                TextYmm: 19f, TextHeightMm: 7f, MarginMm: 4f),
        };

    /// <summary>The name the settings file stores this layout under.</summary>
    public string Key => Presets
        .FirstOrDefault(pair => pair.Value == this).Key
        ?? $"{WidthMm:0}x{HeightMm:0}";

    /// <summary>The single line of text under the barcode, in the order the template uses.</summary>
    public string TextLine(LabelFields fields) =>
        $"{fields.Model} {fields.Storage} {fields.Color} {fields.Grade} {fields.Battery} {fields.PayMethod}";

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
    };
}