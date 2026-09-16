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

    // Extended battery metrics
    public int BatteryCycleCount { get; set; }
    public int BatteryDesignCapacity { get; set; }
    public int BatteryCurrentCapacity { get; set; }
    public string BatterySerialNumber { get; set; } = "";

    // Factory / original component serials
    public string OriginalBatterySerialNumber { get; set; } = "";
    public string DisplaySerialNumber { get; set; } = "";
    public string CoverGlassSerialNumber { get; set; } = "";
    public string FrontCameraSerialNumber { get; set; } = "";
    public string RearCameraSerialNumber { get; set; } = "";
    public string MotherboardSerialNumber { get; set; } = "";

    // Component audit verification list
    public List<ComponentStatus> ComponentChecks { get; set; } = [];
}

public enum ComponentStatusType
{
    Match,
    Mismatch,
    Untrusted,
    Unknown
}

public class ComponentStatus
{
    public required string Name { get; init; }
    public string? SerialRead { get; init; }
    public string? SerialOriginal { get; init; }
    public ComponentStatusType Status { get; init; } = ComponentStatusType.Unknown;
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
