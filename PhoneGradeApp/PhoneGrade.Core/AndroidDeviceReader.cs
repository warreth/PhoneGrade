using System.Globalization;

namespace PhoneGrade.Core;

/// <summary>
/// One shot of everything an Android handset is willing to tell us over adb.
///
/// The collector used to ask for one property at a time, which cost a process
/// spawn per value. Everything here comes out of a single <c>getprop</c> dump
/// plus four more reads, so adding a field no longer means adding a round trip.
/// </summary>
public sealed record AndroidDeviceFacts
{
    public string Serial { get; init; } = "";
    public string HardwareSerial { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Model { get; init; } = "";
    public string AndroidVersion { get; init; } = "";

    /// <summary>Raw colour value, e.g. "WHT". Empty when the device exposes none.</summary>
    public string Color { get; init; } = "";

    /// <summary>Raw storage value, e.g. "128GB,Samsung". Empty when the device exposes none.</summary>
    public string Storage { get; init; } = "";

    /// <summary>Raw memory value, e.g. "12GiB,Micron,LPDDR5,ff07".</summary>
    public string Memory { get; init; } = "";

    /// <summary>Usable <c>/data</c> size in bytes, taken from df. 0 when df is unreadable.</summary>
    public long DataBytes { get; init; }

    public string DumpsysBattery { get; init; } = "";
    public string ChargeFull { get; init; } = "";
    public string ChargeFullDesign { get; init; } = "";

    public string BuildFingerprint { get; init; } = "";
    public string FlashLocked { get; init; } = "";
    public string VbmetaDeviceState { get; init; } = "";
    public string VerifiedBootState { get; init; } = "";
    public string WarrantyBit { get; init; } = "";
}

/// <summary>
/// Reads an Android device over adb. The shell command is injected so the
/// collector can be exercised against captured device output instead of a mock,
/// which is the only way to prove the parsing still matches what phones print.
/// </summary>
public sealed class AndroidDeviceReader
{
    /// <summary>Runs one shell command on the device and returns its stdout.</summary>
    public delegate Task<string> ShellRunner(string command);

    private readonly string _serial;
    private readonly ShellRunner _shell;

    public AndroidDeviceReader(string serial, ShellRunner shell)
    {
        _serial = serial;
        _shell = shell;
    }

    /// <summary>The production reader: every command goes through adb.</summary>
    public static AndroidDeviceReader CreateDefault(string serial) => new(serial, async command =>
    {
        var (stdout, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} shell {command}");
        return stdout;
    });

    /// <summary>Reads a single property. Kept because callers outside the collector want one value.</summary>
    public async Task<string> GetPropAsync(string propName)
    {
        try { return (await _shell($"getprop {propName}")).Trim(); }
        catch { return ""; }
    }

    /// <summary>The whole property dump as a lookup, for callers that need more than one value.</summary>
    public async Task<Dictionary<string, string>> GetPropsAsync() =>
        ParseGetpropOutput(await SafeAsync("getprop"));

    private async Task<string> SafeAsync(string command)
    {
        try { return await _shell(command); }
        catch { return ""; }
    }

    public async Task<AndroidDeviceFacts> ReadAsync()
    {
        // One dump covers every property below. Reading them one by one meant one
        // adb process per value, and the first failing one aborted the rest.
        string getprop = await SafeAsync("getprop");
        var props = ParseGetpropOutput(getprop);

        string First(params string[] names)
        {
            foreach (string name in names)
                if (props.TryGetValue(name, out string? value) && value.Length > 0)
                    return value;
            return "";
        }

        string df = await SafeAsync("df -k /data");
        string battery = await SafeAsync("dumpsys battery");
        string chargeFull = await SafeAsync("cat /sys/class/power_supply/battery/charge_full");
        string chargeFullDesign = await SafeAsync("cat /sys/class/power_supply/battery/charge_full_design");

        return new AndroidDeviceFacts
        {
            Serial = _serial,
            HardwareSerial = First("ro.serialno", "ro.boot.serialno"),
            Brand = First("ro.product.brand"),
            Model = PickModel(props),
            AndroidVersion = First("ro.build.version.release"),

            // Google publishes the retail values under ro.boot.hardware.*; the other
            // names are the spellings other manufacturers use for the same thing.
            Color = First("ro.boot.hardware.color", "ro.boot.product.color", "ro.product.color",
                          "ro.config.color", "ro.boot.color", "ro.build.color"),
            Storage = First("ro.boot.hardware.ufs", "ro.boot.hardware.emmc", "ro.product.storage",
                            "ro.boot.product.storage", "ro.build.storage"),
            Memory = First("ro.boot.hardware.ddr", "ro.product.memory", "ro.boot.hardware.ram"),

            DataBytes = Parsers.ParseAndroidDataBytes(df),

            DumpsysBattery = battery,
            ChargeFull = chargeFull,
            ChargeFullDesign = chargeFullDesign,

            BuildFingerprint = First("ro.build.fingerprint", "ro.vendor.build.fingerprint"),
            FlashLocked = First("ro.boot.flash.locked"),
            VbmetaDeviceState = First("ro.boot.vbmeta.device_state"),
            VerifiedBootState = First("ro.boot.verifiedbootstate"),
            WarrantyBit = First("ro.boot.warranty_bit", "ro.warranty_bit"),
        };
    }

    /// <summary>
    /// Turns a <c>getprop</c> dump into a lookup. The dump is one bracketed
    /// property per line: <c>[ro.product.model]: [Pixel 8 Pro]</c>. A value may
    /// itself contain brackets, so the closing bracket is taken from the end.
    /// </summary>
    public static Dictionary<string, string> ParseGetpropOutput(string? output)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output)) return props;

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('[') || !line.EndsWith(']')) continue;

            int split = line.IndexOf("]: [", StringComparison.Ordinal);
            if (split < 0) continue;

            string name = line[1..split];
            if (name.Length == 0) continue;

            // "]: [" is four characters, and the value starts right after it, still
            // inside the opening bracket of the value itself.
            string value = line[(split + 4)..];
            if (value.EndsWith(']')) value = value[..^1];
            props[name] = value.Trim();
        }

        return props;
    }

    /// <summary>
    /// The properties that carry the name a handset is sold under, best first.
    /// <c>ro.product.model</c> only holds the factory code the factory prints on
    /// the box, "LLY-LX1" for a Honor X8b, while the shop floor knows the phone
    /// as "HONOR X8b". A dump that reports none of these falls back to that code,
    /// because the code still says which handset it is where an empty field would not.
    /// </summary>
    public static readonly string[] MarketingNameProps =
    {
        "ro.config.marketing_name",     // Honor, Huawei
        "ro.product.marketname",        // Xiaomi, POCO, Realme
        "ro.product.vendor.marketname", // brands that keep it off the system partition
    };

    /// <summary>The name the handset is sold under, or its factory code when it reports none.</summary>
    public static string PickModel(IReadOnlyDictionary<string, string> props)
    {
        foreach (string name in MarketingNameProps)
            if (props.TryGetValue(name, out string? value) && value.Trim().Length > 0)
                return value.Trim();

        return props.TryGetValue("ro.product.model", out string? code) ? code.Trim() : "";
    }

    /// <summary>Device data the collector fills in from these facts.</summary>
    public static DeviceData ToDeviceData(AndroidDeviceFacts facts)
    {
        string displayModel = Mappers.MapAndroidDisplayModel(facts.Brand, facts.Model);
        string serial = string.IsNullOrWhiteSpace(facts.HardwareSerial) ? facts.Serial : facts.HardwareSerial;

        var data = new DeviceData
        {
            DeviceId = facts.Serial,
            ProductType = $"Android ({displayModel})",
            Model = string.IsNullOrWhiteSpace(displayModel) ? "Android Device" : displayModel,
            Identifier = serial,
            MotherboardSerialNumber = serial,
            IosVersion = string.IsNullOrWhiteSpace(facts.AndroidVersion) ? "Android" : $"Android {facts.AndroidVersion}",
        };

        data.Color = Mappers.MapAndroidColor(facts.Color);
        data.Storage = Mappers.MapAndroidStorage(facts.Storage, facts.DataBytes);
        data.Memory = Mappers.MapAndroidMemory(facts.Memory);

        // Condition and charge level are different things. dumpsys only carries the
        // charge level and a status code, so the condition comes from the capacity
        // counters and the status code is what is left over.
        int level = Parsers.ParseAndroidChargeLevel(facts.DumpsysBattery);
        if (level > 0) data.BatteryLevel = level;

        int condition = Parsers.ParseAndroidBatteryCondition(facts.ChargeFull, facts.ChargeFullDesign);
        data.BatteryHealth = condition > 0
            ? $"{condition}%"
            : Parsers.ParseAndroidBatteryStatus(facts.DumpsysBattery);

        return data;
    }
}
