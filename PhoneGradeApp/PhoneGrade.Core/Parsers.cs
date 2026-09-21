using System.Text;
using System.Text.RegularExpressions;

namespace PhoneGrade.Core;

/// <summary>Parses plist (idevicediagnostics ioregentry output) and ideviceinfo key-value output.</summary>
public static class Parsers
{
    /// <summary>Parses an Apple XML property list string into a flat key-value dictionary.</summary>
    public static Dictionary<string, string> ParsePlistXml(string xml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(xml)) return result;

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            ParseDictElement(doc.Root?.Element("dict"), result);
        }
        catch
        {
            // Fall back to regex/key-value parsing if XML is malformed
            return ParseKeyValues(xml);
        }

        return result;
    }

    private static void ParseDictElement(System.Xml.Linq.XElement? dictElem, Dictionary<string, string> dict)
    {
        if (dictElem == null) return;

        var elements = dictElem.Elements().ToList();
        for (int i = 0; i < elements.Count - 1; i++)
        {
            if (elements[i].Name.LocalName == "key")
            {
                string key = elements[i].Value.Trim();
                var valElem = elements[i + 1];

                if (valElem.Name.LocalName == "true")
                {
                    dict[key] = "true";
                }
                else if (valElem.Name.LocalName == "false")
                {
                    dict[key] = "false";
                }
                else if (valElem.Name.LocalName == "dict")
                {
                    // Recursively extract nested dicts with prefix
                    var subDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ParseDictElement(valElem, subDict);
                    foreach (var (sk, sv) in subDict)
                    {
                        dict[$"{key}.{sk}"] = sv;
                        if (!dict.ContainsKey(sk)) dict[sk] = sv;
                    }
                }
                else
                {
                    dict[key] = valElem.Value.Trim();
                }
            }
        }
    }

    /// <summary>Extracts an integer value following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static int? PlistInt(string plist, string key)
    {
        if (string.IsNullOrEmpty(plist)) return null;
        var m = KeyRegex(key).Match(plist);
        if (!m.Success) return null;
        var v = ValueIntRegex().Match(plist[m.Index..]);
        return v.Success && int.TryParse(v.Groups[1].Value, out int result) ? result : null;
    }

    /// <summary>Extracts a string value following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static string? PlistString(string plist, string key)
    {
        if (string.IsNullOrEmpty(plist)) return null;
        var m = KeyRegex(key).Match(plist);
        if (!m.Success) return null;
        var v = ValueStringRegex().Match(plist[m.Index..]);
        return v.Success ? v.Groups[1].Value.Trim() : null;
    }

    /// <summary>Extracts base64 or data string following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static string? PlistData(string plist, string key)
    {
        if (string.IsNullOrEmpty(plist)) return null;
        var m = KeyRegex(key).Match(plist);
        if (!m.Success) return null;
        var v = ValueDataRegex().Match(plist[m.Index..]);
        return v.Success ? v.Groups[1].Value.Replace("\r", "").Replace("\n", "").Trim() : null;
    }

    /// <summary>Parses "Key: value" lines from ideviceinfo -q domain output.</summary>
    public static string? KeyValue(string output, string key)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var m = LineRegex(key).Match(output);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Extracts all key-value pairs from ideviceinfo / syscfg formatted output.</summary>
    public static Dictionary<string, string> ParseKeyValues(string output)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output)) return dict;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            int idx = line.IndexOf(':');
            if (idx > 0)
            {
                string k = line[..idx].Trim();
                string v = line[(idx + 1)..].Trim();
                dict[k] = v;
            }
            else
            {
                int eqIdx = line.IndexOf('=');
                if (eqIdx > 0)
                {
                    string k = line[..eqIdx].Trim();
                    string v = line[(eqIdx + 1)..].Trim();
                    dict[k] = v;
                }
            }
        }
        return dict;
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

    /// <summary>Cleans and normalizes serial numbers, decoding ASCII hex or base64 if needed.</summary>
    public static string CleanSerial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string cleaned = raw.Trim();

        // 1. Check if raw is Base64 encoded:
        if (cleaned.Length >= 4 && cleaned.Length % 4 == 0 && !cleaned.Contains(' '))
        {
            bool hasPadding = cleaned.EndsWith("=");
            try
            {
                var bytes = Convert.FromBase64String(cleaned);
                if (bytes.Length > 0)
                {
                    // Check if bytes are clean printable ASCII
                    if (bytes.All(b => b >= 32 && b <= 126))
                    {
                        string decoded = Encoding.ASCII.GetString(bytes).Trim();
                        if (IsValidSerial(decoded))
                        {
                            return decoded;
                        }
                    }
                    else if (hasPadding)
                    {
                        // Explicitly padded binary Base64 data (e.g., THjDhg==) -> decode to Hex string
                        return Convert.ToHexString(bytes);
                    }
                }
            }
            catch
            {
                // Not valid Base64
            }
        }

        // 2. Check if raw is Hex encoded ASCII bytes (e.g. 16+ hex chars)
        if (cleaned.Length >= 16 && cleaned.Length % 2 == 0 && cleaned.All(c => "0123456789abcdefABCDEF".Contains(c)))
        {
            try
            {
                var bytes = Convert.FromHexString(cleaned);
                if (bytes.Length >= 6 && bytes.All(b => b >= 32 && b <= 126))
                {
                    string decoded = Encoding.ASCII.GetString(bytes).Trim();
                    if (IsValidSerial(decoded))
                    {
                        return decoded;
                    }
                }
            }
            catch
            {
                // Not valid Hex
            }
        }

        return cleaned;
    }

    /// <summary>Checks whether a string resembles an alphanumeric Apple component serial number.</summary>
    public static bool IsValidSerial(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 6) return false;
        return s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    /// <summary>Checks whether a value represents an empty, missing, or error response.</summary>
    public static bool IsUnreadable(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim();
        return t.Equals("NO OUTPUT", StringComparison.OrdinalIgnoreCase)
            || t.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            || t.Equals("ONBEKEND", StringComparison.OrdinalIgnoreCase)
            || t.Equals("NOID", StringComparison.OrdinalIgnoreCase)
            || t.Equals("NOCOLOR", StringComparison.OrdinalIgnoreCase)
            || t.Equals("NOBATT", StringComparison.OrdinalIgnoreCase)
            || t.Equals("NOSTORAGE", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || t.Equals("null", StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Compares live read serial against original factory serial.</summary>
    public static ComponentStatusType VerifyComponent(string? liveSerial, string? factorySerial)
    {
        string live = (liveSerial ?? "").Trim();
        string factory = (factorySerial ?? "").Trim();

        if (IsUnreadable(live) || IsUnreadable(factory))
        {
            return ComponentStatusType.Unknown;
        }

        if (IsMaskedOrProtected(live) || IsMaskedOrProtected(factory))
        {
            return ComponentStatusType.Untrusted;
        }

        return live.Equals(factory, StringComparison.OrdinalIgnoreCase)
            ? ComponentStatusType.Match
            : ComponentStatusType.Mismatch;
    }

    private static bool IsMaskedOrProtected(string s)
    {
        return s.Equals("[MASKED]", StringComparison.OrdinalIgnoreCase)
            || s.Equals("MASKED", StringComparison.OrdinalIgnoreCase)
            || s.Equals("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
            || s.Equals("PROTECTED", StringComparison.OrdinalIgnoreCase)
            || s.Equals("NOT_PAIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static Regex KeyRegex(string key) => new(Regex.Escape($"<key>{key}</key>"), RegexOptions.IgnoreCase);
    private static Regex ValueIntRegex() => new(@"<integer>\s*(-?\d+)\s*</integer>", RegexOptions.IgnoreCase);
    private static Regex ValueStringRegex() => new(@"<string>\s*([^<]*)\s*</string>", RegexOptions.IgnoreCase);
    private static Regex ValueDataRegex() => new(@"<data>\s*([^<]*)\s*</data>", RegexOptions.IgnoreCase);
    private static Regex LineRegex(string key) => new(Regex.Escape($"{key}:") + @"\s*(.+)", RegexOptions.IgnoreCase);
}
