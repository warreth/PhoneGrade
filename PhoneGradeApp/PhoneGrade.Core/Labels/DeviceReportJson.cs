using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneGrade.Core;

/// <summary>
/// The raw numbers of an inspection as JSON.
///
/// Every field name is written out rather than left to a naming policy, and every
/// field carries a number so the order is fixed. That is deliberate: a report gets
/// archived, then gets read a year later by something that was not written yet,
/// and a field that silently changes its name between two versions of this app is
/// a field that quietly stops being read. Pinning both the name and the order is
/// what makes two exports of the same device diff cleanly.
/// </summary>
public static class DeviceReportJson
{
    /// <summary>
    /// Bumped when a field changes meaning or disappears, never for a new field.
    /// A reader checks this before trusting anything else in the file.
    ///
    /// 2: the battery section no longer carries VoltageMv or TemperatureDeciCelsius.
    /// The app stopped reading them, and an export that keeps emitting a field the
    /// tool cannot fill would read as a measurement.
    /// </summary>
    public const int SchemaVersion = 2;

    public static string Serialize(DeviceData data)
    {
        var report = new Report
        {
            SchemaVersion = SchemaVersion,
            ExportedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            Device = Map(data),
        };

        return JsonSerializer.Serialize(report, DeviceReportJsonContext.Default.Report);
    }

    /// <summary>Writes the report to a file.</summary>
    public static void Write(string path, DeviceData data) =>
        File.WriteAllText(path, Serialize(data), new System.Text.UTF8Encoding(false));

    private static DeviceSection Map(DeviceData data) => new()
    {
        Identifier = data.Identifier,
        DeviceId = data.DeviceId,
        Model = data.Model,
        ProductType = data.ProductType,
        Color = data.Color,
        Storage = data.Storage,
        Memory = data.Memory,
        OsVersion = data.IosVersion,
        FmiVerificationSource = data.FmiVerificationSource,
        Battery = new BatterySection
        {
            Condition = data.BatteryHealth,
            ConditionIsLow = LabelFields.From(data).BatteryIsLow,
            LevelPercent = data.BatteryLevel,
            CycleCount = data.BatteryCycleCount,
            DesignCapacityMah = data.BatteryDesignCapacity,
            CurrentCapacityMah = data.BatteryCurrentCapacity,
            SerialNumber = data.BatterySerialNumber,
            FactorySerialNumber = data.OriginalBatterySerialNumber,
        },
        Network = new NetworkSection
        {
            WifiMac = data.WifiMacAddress,
            BluetoothMac = data.BluetoothMacAddress,
            CellularAddress = data.CellularAddress,
            Imei2 = data.Imei2,
        },
        Security = new SecuritySection
        {
            ActivationLock = data.ActivationLock?.ToString(),
            CarrierLockIos = Lock(data.CarrierLockIOS?.IsCarrierLocked),
            CarrierIos = data.CarrierLockIOS?.CarrierName,
            CarrierLockAndroid = Lock(data.CarrierLockAndroid?.IsCarrierLocked),
            CarrierAndroid = data.CarrierLockAndroid?.CarrierName,
            SimState = data.CarrierLockAndroid?.SIMState,
            FactoryResetProtection = data.FactoryResetProtection?.ToString(),
            Blacklisted = data.Blacklist?.IsBlacklisted,
            BlacklistReason = data.Blacklist?.Reason,
            BlacklistSource = data.Blacklist?.Source,
        },
        Components = new ComponentSection
        {
            Display = data.DisplaySerialNumber,
            CoverGlass = data.CoverGlassSerialNumber,
            FrontCamera = data.FrontCameraSerialNumber,
            RearCamera = data.RearCameraSerialNumber,
            Motherboard = data.MotherboardSerialNumber,
            TouchOrFaceId = data.TouchIdFaceIdSerialNumber,
        },
        Checks = [.. data.ComponentChecks.Select(check => new Check
        {
            Name = check.Name,
            SerialRead = check.SerialRead,
            SerialFactory = check.SerialOriginal,
            Status = check.Status.ToString(),
            Description = check.Description,
            Details = check.Details,
        })],
        Tests = data.InteractiveTests?.Tests.Select(test => new Test
        {
            Id = test.Id,
            Name = test.Name,
            Status = test.Status.ToString(),
            Notes = test.Notes,
            DurationMs = test.DurationMs,
            Details = test.Details?.ToDictionary(entry => entry.Key, entry => entry.Value?.ToString()),
        }).ToList() ?? [],
        // Present even when nothing was withheld, so a reader can tell an empty
        // list from a version of this app that did not record the field.
        NotRead = [.. data.WithheldReads],
        Grading = new GradingSection
        {
            Grade = data.Quality,
            InvoiceMethod = data.PayMethod,
        },
    };

    /// <summary>A lock that is on, off, or was never established.</summary>
    private static bool? Lock(bool? locked) => locked;

    // The shape on disk. Declared here, serialized through the generated context
    // in DeviceReportJsonContext.cs, which cannot live in this file because the
    // generator writes a partial class beside it.

    internal sealed class Report
    {
        [JsonPropertyName("schemaVersion")]
        [JsonPropertyOrder(0)]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("exportedAt")]
        [JsonPropertyOrder(1)]
        public string ExportedAt { get; set; } = "";

        [JsonPropertyName("device")]
        [JsonPropertyOrder(2)]
        public DeviceSection Device { get; set; } = new();
    }

    internal sealed class DeviceSection
    {
        [JsonPropertyName("identifier")]
        [JsonPropertyOrder(0)]
        public string Identifier { get; set; } = "";

        [JsonPropertyName("deviceId")]
        [JsonPropertyOrder(1)]
        public string DeviceId { get; set; } = "";

        [JsonPropertyName("model")]
        [JsonPropertyOrder(2)]
        public string Model { get; set; } = "";

        [JsonPropertyName("productType")]
        [JsonPropertyOrder(3)]
        public string ProductType { get; set; } = "";

        [JsonPropertyName("color")]
        [JsonPropertyOrder(4)]
        public string Color { get; set; } = "";

        [JsonPropertyName("storage")]
        [JsonPropertyOrder(5)]
        public string Storage { get; set; } = "";

        [JsonPropertyName("memory")]
        [JsonPropertyOrder(6)]
        public string Memory { get; set; } = "";

        [JsonPropertyName("osVersion")]
        [JsonPropertyOrder(7)]
        public string? OsVersion { get; set; }

        [JsonPropertyName("fmiVerificationSource")]
        [JsonPropertyOrder(8)]
        public string FmiVerificationSource { get; set; } = "";

        [JsonPropertyName("battery")]
        [JsonPropertyOrder(9)]
        public BatterySection Battery { get; set; } = new();

        [JsonPropertyName("network")]
        [JsonPropertyOrder(10)]
        public NetworkSection Network { get; set; } = new();

        [JsonPropertyName("security")]
        [JsonPropertyOrder(11)]
        public SecuritySection Security { get; set; } = new();

        [JsonPropertyName("components")]
        [JsonPropertyOrder(12)]
        public ComponentSection Components { get; set; } = new();

        [JsonPropertyName("checks")]
        [JsonPropertyOrder(13)]
        public List<Check> Checks { get; set; } = [];

        [JsonPropertyName("tests")]
        [JsonPropertyOrder(14)]
        public List<Test> Tests { get; set; } = [];

        [JsonPropertyName("notRead")]
        [JsonPropertyOrder(15)]
        public List<string> NotRead { get; set; } = [];

        [JsonPropertyName("grading")]
        [JsonPropertyOrder(16)]
        public GradingSection Grading { get; set; } = new();
    }

    internal sealed class BatterySection
    {
        [JsonPropertyName("condition")]
        [JsonPropertyOrder(0)]
        public string Condition { get; set; } = "";

        [JsonPropertyName("conditionIsLow")]
        [JsonPropertyOrder(1)]
        public bool ConditionIsLow { get; set; }

        [JsonPropertyName("levelPercent")]
        [JsonPropertyOrder(2)]
        public int? LevelPercent { get; set; }

        [JsonPropertyName("cycleCount")]
        [JsonPropertyOrder(3)]
        public int? CycleCount { get; set; }

        [JsonPropertyName("designCapacityMah")]
        [JsonPropertyOrder(4)]
        public int DesignCapacityMah { get; set; }

        [JsonPropertyName("currentCapacityMah")]
        [JsonPropertyOrder(5)]
        public int CurrentCapacityMah { get; set; }

        [JsonPropertyName("serialNumber")]
        [JsonPropertyOrder(6)]
        public string SerialNumber { get; set; } = "";

        [JsonPropertyName("factorySerialNumber")]
        [JsonPropertyOrder(7)]
        public string FactorySerialNumber { get; set; } = "";
    }

    internal sealed class NetworkSection
    {
        [JsonPropertyName("wifiMac")]
        [JsonPropertyOrder(0)]
        public string WifiMac { get; set; } = "";

        [JsonPropertyName("bluetoothMac")]
        [JsonPropertyOrder(1)]
        public string BluetoothMac { get; set; } = "";

        [JsonPropertyName("cellularAddress")]
        [JsonPropertyOrder(2)]
        public string CellularAddress { get; set; } = "";

        [JsonPropertyName("imei2")]
        [JsonPropertyOrder(3)]
        public string Imei2 { get; set; } = "";
    }

    internal sealed class SecuritySection
    {
        [JsonPropertyName("activationLock")]
        [JsonPropertyOrder(0)]
        public string? ActivationLock { get; set; }

        [JsonPropertyName("carrierLockIos")]
        [JsonPropertyOrder(1)]
        public bool? CarrierLockIos { get; set; }

        [JsonPropertyName("carrierIos")]
        [JsonPropertyOrder(2)]
        public string? CarrierIos { get; set; }

        [JsonPropertyName("carrierLockAndroid")]
        [JsonPropertyOrder(3)]
        public bool? CarrierLockAndroid { get; set; }

        [JsonPropertyName("carrierAndroid")]
        [JsonPropertyOrder(4)]
        public string? CarrierAndroid { get; set; }

        [JsonPropertyName("simState")]
        [JsonPropertyOrder(5)]
        public string? SimState { get; set; }

        [JsonPropertyName("factoryResetProtection")]
        [JsonPropertyOrder(6)]
        public string? FactoryResetProtection { get; set; }

        [JsonPropertyName("blacklisted")]
        [JsonPropertyOrder(7)]
        public bool? Blacklisted { get; set; }

        [JsonPropertyName("blacklistReason")]
        [JsonPropertyOrder(8)]
        public string? BlacklistReason { get; set; }

        [JsonPropertyName("blacklistSource")]
        [JsonPropertyOrder(9)]
        public string? BlacklistSource { get; set; }
    }

    internal sealed class ComponentSection
    {
        [JsonPropertyName("display")]
        [JsonPropertyOrder(0)]
        public string Display { get; set; } = "";

        [JsonPropertyName("coverGlass")]
        [JsonPropertyOrder(1)]
        public string CoverGlass { get; set; } = "";

        [JsonPropertyName("frontCamera")]
        [JsonPropertyOrder(2)]
        public string FrontCamera { get; set; } = "";

        [JsonPropertyName("rearCamera")]
        [JsonPropertyOrder(3)]
        public string RearCamera { get; set; } = "";

        [JsonPropertyName("motherboard")]
        [JsonPropertyOrder(4)]
        public string Motherboard { get; set; } = "";

        [JsonPropertyName("touchOrFaceId")]
        [JsonPropertyOrder(5)]
        public string TouchOrFaceId { get; set; } = "";
    }

    internal sealed class Check
    {
        [JsonPropertyName("name")]
        [JsonPropertyOrder(0)]
        public string Name { get; set; } = "";

        [JsonPropertyName("serialRead")]
        [JsonPropertyOrder(1)]
        public string? SerialRead { get; set; }

        [JsonPropertyName("serialFactory")]
        [JsonPropertyOrder(2)]
        public string? SerialFactory { get; set; }

        [JsonPropertyName("status")]
        [JsonPropertyOrder(3)]
        public string Status { get; set; } = "";

        [JsonPropertyName("description")]
        [JsonPropertyOrder(4)]
        public string? Description { get; set; }

        [JsonPropertyName("details")]
        [JsonPropertyOrder(5)]
        public string? Details { get; set; }
    }

    internal sealed class Test
    {
        [JsonPropertyName("id")]
        [JsonPropertyOrder(0)]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        [JsonPropertyOrder(1)]
        public string Name { get; set; } = "";

        [JsonPropertyName("status")]
        [JsonPropertyOrder(2)]
        public string Status { get; set; } = "";

        [JsonPropertyName("notes")]
        [JsonPropertyOrder(3)]
        public string? Notes { get; set; }

        [JsonPropertyName("durationMs")]
        [JsonPropertyOrder(4)]
        public long DurationMs { get; set; }

        [JsonPropertyName("details")]
        [JsonPropertyOrder(5)]
        public Dictionary<string, string?>? Details { get; set; }
    }

    internal sealed class GradingSection
    {
        [JsonPropertyName("grade")]
        [JsonPropertyOrder(0)]
        public string Grade { get; set; } = "";

        [JsonPropertyName("invoiceMethod")]
        [JsonPropertyOrder(1)]
        public string InvoiceMethod { get; set; } = "";
    }
}