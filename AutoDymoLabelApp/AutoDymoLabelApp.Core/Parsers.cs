using System.Text.RegularExpressions;

namespace AutoDymoLabel.Core;

/// <summary>Parses plist (idevicediagnostics ioregistry output) and ideviceinfo key-value output.</summary>
public static class Parsers
{
    /// <summary>Extracts an integer value following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static int? PlistInt(string plist, string key)
    {
        var m = KeyRegex(key).Match(plist);
        if (!m.Success) return null;
        var v = ValueRegex().Match(plist[m.Index..]);
        return v.Success && int.TryParse(v.Groups[1].Value, out int result) ? result : null;
    }

    /// <summary>Parses "Key: value" lines from ideviceinfo -q domain output.</summary>
    public static string? KeyValue(string output, string key)
    {
        var m = LineRegex(key).Match(output);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Battery health % from AppleSmartBattery/AppleARMPMUCharger plist output.
    /// Prefers AppleRawMaxCapacity/DesignCapacity, falls back through MaxCapacity and NominalChargeCapacity.</summary>
    public static string ParseBatteryHealth(string plist)
    {
        int? design = PlistInt(plist, "DesignCapacity");
        if (design is not > 0) return "NOBATT";
        int? current = PlistInt(plist, "AppleRawMaxCapacity")
                    ?? PlistInt(plist, "MaxCapacity")
                    ?? PlistInt(plist, "NominalChargeCapacity");
        if (current is not > 0) return "NOBATT";
        return $"{Math.Min((double)current / design.Value * 100, 100):F0}";
    }

    /// <summary>Identifier for the label: IMEI if the device has one (iPhones), else serial number.</summary>
    public static string ParseIdentifier(string imeiOutput, string serialOutput)
    {
        string imei = (imeiOutput ?? "").Trim();
        bool hasImei = imei.Length >= 14 && imei.All(char.IsDigit);
        string serial = (serialOutput ?? "").Trim();
        return hasImei ? imei : serial.Length > 0 ? serial : "NOID";
    }

    private static Regex KeyRegex(string key) => new(Regex.Escape($"<key>{key}</key>"), RegexOptions.IgnoreCase);
    private static Regex ValueRegex() => new(@"<integer>\s*(-?\d+)\s*</integer>", RegexOptions.IgnoreCase);
    private static Regex LineRegex(string key) => new(Regex.Escape($"{key}:") + @"\s*(.+)", RegexOptions.IgnoreCase);
}
