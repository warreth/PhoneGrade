namespace PhoneGrade.Core;

/// <summary>
/// The seven values that end up on a label, decided in one place.
///
/// Everything that prints a label goes through here: the .dymo file, the label
/// PDF, the preview in the export panel and the row the operator reads back. Four
/// separate places each deciding when a battery reads 68% and when it reads
/// 68% [X] is how a preview and a printed label end up disagreeing with each
/// other, and the operator is the one who finds out.
///
/// The placeholders the phone reports are kept as they arrive. "NOBATT" means the
/// platform gave nothing, and a label reading "NOBATT" is honest in a way that
/// "unknown" is not; the export report is where the wording belongs.
/// </summary>
public sealed record LabelFields
{
    /// <summary>Battery condition, as a percentage with a marker under 85 percent.</summary>
    public required string Battery { get; init; }

    /// <summary>Grade as the operator picked it, or the placeholder while none is.</summary>
    public required string Grade { get; init; }

    /// <summary>The device identifier, which is also what the barcode encodes.</summary>
    public required string Identifier { get; init; }

    /// <summary>Colour, or the placeholder while the platform gave none.</summary>
    public required string Color { get; init; }

    /// <summary>Installed memory, or the placeholder while the platform gave none.</summary>
    public required string Memory { get; init; }

    /// <summary>Model, or the placeholder while the platform gave none.</summary>
    public required string Model { get; init; }

    /// <summary>Invoice method, or the placeholder while none is chosen.</summary>
    public required string PayMethod { get; init; }

    /// <summary>Storage, or the placeholder while the platform gave none.</summary>
    public required string Storage { get; init; }

    /// <summary>True when the battery is under the threshold and carries the marker.</summary>
    public bool BatteryIsLow { get; init; }

    /// <summary>True when a value is still a placeholder and must not reach a label.</summary>
    public bool IsComplete =>
        Identifier != DevicePlaceholders.Identifier
        && Model != DevicePlaceholders.Model
        && Storage != DevicePlaceholders.Storage
        && Color != DevicePlaceholders.Color
        && Grade != DevicePlaceholders.Grade
        && PayMethod != DevicePlaceholders.PayMethod
        && Battery != DevicePlaceholders.Battery;

    /// <summary>Below this percentage of design capacity the battery is flagged.</summary>
    public const int LowBatteryPercent = 85;

    /// <summary>
    /// Reads the values off an inspection.
    /// </summary>
    /// <param name="data">what the phone reported</param>
    /// <param name="flagLowBattery">
    /// Whether a battery below <see cref="LowBatteryPercent"/> carries the marker.
    /// The setting is off for shops that grade every battery as it stands; without
    /// the switch the marker is on the label whether the operator wants it or not.
    /// </param>
    public static LabelFields From(DeviceData data, bool flagLowBattery = true)
    {
        (string battery, bool low) = BatteryField(data.BatteryHealth, flagLowBattery);
        return new LabelFields
        {
            Battery = battery,
            BatteryIsLow = low,
            Grade = Clean(data.Quality, DevicePlaceholders.Grade),
            Identifier = Clean(data.Identifier, DevicePlaceholders.Identifier),
            Color = Clean(data.Color, DevicePlaceholders.Color),
            Memory = Clean(data.Memory, DevicePlaceholders.Memory),
            Model = Clean(data.Model, DevicePlaceholders.Model),
            PayMethod = Clean(data.PayMethod, DevicePlaceholders.PayMethod),
            Storage = Clean(data.Storage, DevicePlaceholders.Storage),
        };
    }

    /// <summary>
    /// Formats the battery condition. Android reports a percentage when the
    /// capacity counters are readable and a status word when they are not, and a
    /// word with a percent sign behind it ("Good%") is nonsense on a label.
    /// </summary>
    private static (string Text, bool Low) BatteryField(string health, bool flagLowBattery)
    {
        string value = health?.Trim() ?? "";
        if (value.Length == 0 || value == DevicePlaceholders.Battery) return (value, false);

        string digits = value.EndsWith('%') ? value[..^1].Trim() : value;
        if (!int.TryParse(digits, out int percent)) return (value, false);

        bool low = percent < LowBatteryPercent;
        string text = $"{percent}%";
        return (low && flagLowBattery ? $"{text} {LabelMarkers.LowBattery}" : text, low);
    }

    /// <summary>An empty value would leave a gap on the label; the placeholder says why.</summary>
    private static string Clean(string? value, string placeholder)
        => string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();
}

/// <summary>
/// The words the phone reports when it did not report anything. They are not
/// errors and they are not values, and every consumer needs to tell them apart
/// from both.
/// </summary>
public static class DevicePlaceholders
{
    public const string Identifier = "NOID";
    public const string Battery = "NOBATT";
    public const string Color = "NOCOLOR";
    public const string Memory = "NOMEMORY";
    public const string Model = "NOMODEL";
    public const string Grade = "NOQUALITY";
    public const string PayMethod = "NOPAY";
    public const string Storage = "NOSTORAGE";
}

/// <summary>The marks that go on a label, in the glyphs DYMO printers have to offer.</summary>
public static class LabelMarkers
{
    /// <summary>Stands in for a cross. The label stock is thermal, so it is a character.</summary>
    public const string LowBattery = "[X]";
}