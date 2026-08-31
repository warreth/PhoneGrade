namespace AutoDymoLabel.Core;

/// <summary>Represents the data retrieved from the connected device.</summary>
public class DeviceData
{
    public string Identifier { get; set; } = "NOID";       // serial number or IMEI
    public string BatteryHealth { get; set; } = "NOBATT";
    public string Color { get; set; } = "NOCOLOR";
    public string Storage { get; set; } = "NOSTORAGE";
    public string Model { get; set; } = "NOMODEL";
    public string Quality { get; set; } = "NOQUALITY";
    public string PayMethod { get; set; } = "NOPAY";
    public string DeviceId { get; set; } = "NODEVICEID";
    public string ProductType { get; set; } = "";          // raw, e.g. iPhone14,2 — used by diagnostics
    public string? IosVersion { get; set; }
}

/// <summary>A diagnostic finding for the device, e.g. from a panic log.</summary>
public class DiagnosticIssue
{
    public required string Title { get; init; }
    /// <summary>Plain-language explanation of what the finding means.</summary>
    public required string Explanation { get; init; }
    /// <summary>What to check / replace first.</summary>
    public required string Fix { get; init; }
    /// <summary>OK / Warning / Error. Errors block the auto flow; warnings show a hint.</summary>
    public Severity Level { get; init; } = Severity.Warning;
}

public enum Severity { Ok, Warning, Error }
