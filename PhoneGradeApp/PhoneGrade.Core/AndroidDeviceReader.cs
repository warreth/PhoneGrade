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

    /// <summary>
    /// Full <c>dumpsys batterystats</c> output. Carries the learned battery
    /// capacity on builds that keep the sysfs counters closed to the shell.
    /// </summary>
    public string DumpsysBatterystats { get; init; } = "";

    public string ChargeFull { get; init; } = "";
    public string ChargeFullDesign { get; init; } = "";

    public string BuildFingerprint { get; init; } = "";
    public string FlashLocked { get; init; } = "";
    public string VbmetaDeviceState { get; init; } = "";
    public string VerifiedBootState { get; init; } = "";
    public string WarrantyBit { get; init; } = "";

    /// <summary>Primary IMEI, read from the phone's first slot on Android 10 and later.</summary>
    public string Imei1 { get; init; } = "";

    /// <summary>Secondary IMEI, read from the phone's second slot (dual SIM / eSIM).</summary>
    public string Imei2 { get; init; } = "";

    /// <summary>Battery cycle count, null when the handset reports none.</summary>
    public int? BatteryCycleCount { get; init; }

    /// <summary>Wi-Fi MAC address from /sys/class/net/wlan0/address.</summary>
    public string WifiMacAddress { get; init; } = "";

    /// <summary>Bluetooth MAC address from settings get secure bluetooth_address.</summary>
    public string BluetoothMacAddress { get; init; } = "";

    /// <summary>
    /// The reads the handset refused, named after the value rather than the
    /// command. A closed sysfs or a blocked settings provider answers with an
    /// error and nothing on stdout, which without this is indistinguishable from
    /// a handset that simply has nothing to report.
    /// </summary>
    public IReadOnlyList<string> Withheld { get; init; } = [];
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

    /// <summary>
    /// Runs one shell command and reports whether the handset allowed it. The
    /// refusal is what tells a closed sysfs entry apart from an absent value, so
    /// the reader gets both halves of the answer rather than only stdout.
    /// </summary>
    public delegate Task<(string Stdout, bool Refused)> GuardedShellRunner(string command);

    private readonly string _serial;
    private readonly ShellRunner _shell;
    private readonly GuardedShellRunner? _guardedShell;

    public AndroidDeviceReader(string serial, ShellRunner shell)
    {
        _serial = serial;
        _shell = shell;
    }

    public AndroidDeviceReader(string serial, GuardedShellRunner guardedShell)
    {
        _serial = serial;
        _shell = command => guardedShell(command).ContinueWith(t => t.Result.Stdout);
        _guardedShell = guardedShell;
    }

    /// <summary>The production reader: every command goes through adb.</summary>
    public static AndroidDeviceReader CreateDefault(string serial) => new(serial, async (string command) =>
    {
        var (stdout, _, exitCode) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} shell {command}");
        bool refused = exitCode != 0 || IsRefusal(stdout);
        return (stdout, refused);
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

        // Reads the shell is often refused. Each one is asked through the guarded
        // runner so a refusal is remembered by name instead of turning into an
        // empty field that looks like the handset had nothing to say.
        var withheld = new List<string>();

        async Task<(string Value, bool Refused)> Ask(string command, string value)
        {
            if (_guardedShell is null) return (await SafeAsync(command), false);

            var (stdout, refused) = await _guardedShell(command);
            if (refused) withheld.Add(value);
            return (stdout, refused);
        }

        string df = await SafeAsync("df -k /data");
        string battery = await SafeAsync("dumpsys battery");
        string batterystats = await SafeAsync("dumpsys batterystats");
        var (chargeFull, chargeFullRefused) = await Ask("cat /sys/class/power_supply/battery/charge_full", "charge_full");
        var (chargeFullDesign, chargeFullDesignRefused) = await Ask("cat /sys/class/power_supply/battery/charge_full_design", "charge_full_design");

        // Android 10 took the IMEI away from the plain shell call, but the phone
        // still answers when the request names a package that holds the phone
        // state permission and belongs to the uid the shell runs as. That is
        // "com.android.shell" itself. The slot-numbered call is asked first
        // because it is the one that answers on a handset with no subscription;
        // the older spellings follow for builds that know no other.
        string imei1 = await ReadImeiAsync(
        [
            "service call iphonesubinfo 4 i32 0 s16 com.android.shell",
            "service call iphonesubinfo 1 s16 com.android.shell",
            "service call iphonesubinfo 1",
        ], "imei1", withheld);

        string imei2 = await ReadImeiAsync(
        [
            "service call iphonesubinfo 4 i32 1 s16 com.android.shell",
            "service call iphonesubinfo 2 s16 com.android.shell",
            "service call iphonesubinfo 2",
        ], "imei2", withheld);

        // The Wi-Fi and Bluetooth addresses the shell used to read are refused
        // on current builds. ip and dumpsys still print them, so a refusal is
        // only kept when every route came back empty.
        var (wifiRaw, wifiRefused) = await Ask("cat /sys/class/net/wlan0/address 2>/dev/null", "wlan0_address");
        if (!IsMacAddress(wifiRaw))
        {
            string viaIp = Parsers.ParseIpInterfaceMac(await SafeAsync("ip addr show wlan0"));
            if (IsMacAddress(viaIp)) (wifiRaw, wifiRefused) = (viaIp, false);
            else
            {
                string viaDumpsys = Parsers.ParseDumpsysWifiMac(await SafeAsync("dumpsys wifi"));
                if (IsMacAddress(viaDumpsys)) (wifiRaw, wifiRefused) = (viaDumpsys, false);
            }

            if (!wifiRefused) withheld.Remove("wlan0_address");
        }

        var (btRaw, btRefused) = await Ask("settings get secure bluetooth_address 2>/dev/null", "bluetooth_address");
        if (!IsMacAddress(btRaw))
        {
            string viaDumpsys = Parsers.ParseBluetoothManagerMac(await SafeAsync("dumpsys bluetooth_manager"));
            if (IsMacAddress(viaDumpsys)) (btRaw, btRefused) = (viaDumpsys, false);

            if (!btRefused) withheld.Remove("bluetooth_address");
        }

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
            DumpsysBatterystats = batterystats,
            ChargeFull = chargeFull,
            ChargeFullDesign = chargeFullDesign,

            BuildFingerprint = First("ro.build.fingerprint", "ro.vendor.build.fingerprint"),
            FlashLocked = First("ro.boot.flash.locked"),
            VbmetaDeviceState = First("ro.boot.vbmeta.device_state"),
            VerifiedBootState = First("ro.boot.verifiedbootstate"),
            WarrantyBit = First("ro.boot.warranty_bit", "ro.warranty_bit"),

            Imei1 = imei1,
            Imei2 = imei2,

            BatteryCycleCount = Parsers.ParseAndroidBatteryCycleCount(battery),

            WifiMacAddress = ReadMac(wifiRaw, wifiRefused),
            BluetoothMacAddress = ReadMac(btRaw, btRefused),

            Withheld = withheld,
        };
    }

    /// <summary>
    /// Asks each spelling of the IMEI read in turn and returns the first answer
    /// that decodes to an IMEI. The name is remembered as withheld only when a
    /// handset refused every route: an answer that ran but carried no IMEI (a
    /// second slot with no card behind it) is an absent value, not a refusal.
    /// </summary>
    private async Task<string> ReadImeiAsync(string[] commands, string withheldName, List<string> withheld)
    {
        bool anyRefused = false;

        foreach (string command in commands)
        {
            var (stdout, refused) = _guardedShell is null
                ? (await SafeAsync(command), false)
                : await _guardedShell(command);

            anyRefused |= refused;

            string imei = Parsers.ParseAndroidImei(stdout);
            if (imei.Length > 0) return imei;
        }

        if (anyRefused) withheld.Add(withheldName);
        return "";
    }

    /// <summary>
    /// True when the answer is a MAC address and nothing else. A blocked read
    /// leaves the word null behind, and a stack trace can arrive on the same
    /// stream, so both are kept out of the field rather than reported as an
    /// address a grader would then compare.
    /// </summary>
    public static bool IsMacAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        string text = value.Trim();
        if (text.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = text.Split(':');
        if (parts.Length != 6) return false;

        return parts.All(p => p.Length == 2 && p.All(Uri.IsHexDigit));
    }

    /// <summary>The address to store, or empty when the handset did not give one.</summary>
    private static string ReadMac(string raw, bool refused) =>
        refused || !IsMacAddress(raw) ? "" : raw.Trim().ToUpperInvariant();

    /// <summary>
    /// True when the shell answered with a refusal rather than with a value.
    /// Some builds write the refusal to stdout instead of stderr, so the wording
    /// is checked as well as the exit code. A <c>service call</c> that comes back
    /// with a binder error is a refusal too even though the shell exited zero.
    /// </summary>
    private static bool IsRefusal(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return true;

        string text = stdout.Trim();
        return text.StartsWith("Permission denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SecurityException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Exception occurred while executing", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("cat:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || Parsers.IsParcelError(text);
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

        // Prefer IMEI1 as the primary identifier, fall back to serial
        string primaryIdentifier = !string.IsNullOrWhiteSpace(facts.Imei1) ? facts.Imei1 : serial;

        // A dual SIM handset without a second card answers the primary IMEI for
        // the second slot as well. Storing it twice would read as two IMEIs,
        // which is a claim about the phone that is not true.
        string imei2 = string.Equals(facts.Imei2, facts.Imei1, StringComparison.Ordinal) ? "" : facts.Imei2;

        var data = new DeviceData
        {
            DeviceId = facts.Serial,
            ProductType = $"Android ({displayModel})",
            Model = string.IsNullOrWhiteSpace(displayModel) ? "Android Device" : displayModel,
            Identifier = primaryIdentifier,
            MotherboardSerialNumber = serial,
            IosVersion = string.IsNullOrWhiteSpace(facts.AndroidVersion) ? "Android" : $"Android {facts.AndroidVersion}",
            Imei2 = imei2,
            WifiMacAddress = facts.WifiMacAddress,
            BluetoothMacAddress = facts.BluetoothMacAddress,
        };

        data.Color = Mappers.MapAndroidColor(facts.Color);
        data.Storage = Mappers.MapAndroidStorage(facts.Storage, facts.DataBytes);
        data.Memory = Mappers.MapAndroidMemory(facts.Memory);

        // Condition and charge level are different things. dumpsys only carries the
        // charge level and a status code, so the condition comes from the capacity
        // counters and the status code is what is left over. The sysfs counters are
        // closed to the shell on several builds; the capacity the platform learned
        // is still readable in batterystats, and it stands in for the counter that
        // was refused so the health percentage is not lost with it.
        int level = Parsers.ParseAndroidChargeLevel(facts.DumpsysBattery);
        if (level > 0) data.BatteryLevel = level;

        int learnedCapacity = Parsers.ParseAndroidBatteryCapacity(facts.DumpsysBatterystats);
        data.BatteryCurrentCapacity = learnedCapacity > 0
            ? learnedCapacity
            : ToMilliampHours(ReadCapacity(facts.ChargeFull));
        data.BatteryDesignCapacity = ToMilliampHours(ReadCapacity(facts.ChargeFullDesign));

        string chargeFull = learnedCapacity > 0 && !long.TryParse(facts.ChargeFull.Trim(), out _)
            ? learnedCapacity.ToString(CultureInfo.InvariantCulture)
            : facts.ChargeFull;

        int condition = Parsers.ParseAndroidBatteryCondition(chargeFull, facts.ChargeFullDesign);
        data.BatteryHealth = condition > 0
            ? $"{condition}%"
            : Parsers.ParseAndroidBatteryStatus(facts.DumpsysBattery);

        // Extended battery metrics from dumpsys
        data.BatteryCycleCount = facts.BatteryCycleCount;

        data.WithheldReads.AddRange(facts.Withheld);

        return data;
    }

    /// <summary>A positive sysfs counter as an integer, or zero when it is absent or garbled.</summary>
    private static int ReadCapacity(string? raw) =>
        long.TryParse((raw ?? "").Trim(), out long value) && value is > 0 and <= int.MaxValue ? (int)value : 0;

    /// <summary>
    /// The capacity counters under /sys are in microamp-hours and the learned
    /// capacity from batterystats is already in milliamp-hours. A raw counter in
    /// the micro range is scaled down so the report carries one unit whichever
    /// read answered.
    /// </summary>
    private static int ToMilliampHours(int raw) => raw > 20_000 ? raw / 1000 : raw;
}
