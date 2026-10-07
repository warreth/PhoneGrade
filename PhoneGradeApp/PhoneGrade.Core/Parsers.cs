using System.Globalization;
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

        // A property list that did not read is a property list that was refused.
        // Scraping "Key: value" lines out of it turns the refusal into data, so
        // the text reader is only for output that was never a property list.
        if (!LooksLikePropertyList(xml))
            return ParseKeyValues(xml);

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            ParseDictElement(doc.Root?.Element("dict"), result);
        }
        catch (System.Xml.XmlException)
        {
            // The document opens like a property list but does not close as one,
            // usually because something was written after it. Nothing can be read
            // out of it reliably, and an empty answer says so honestly.
            return result;
        }

        return result;
    }

    /// <summary>True when the text is meant to be a property list rather than key/value lines.</summary>
    private static bool LooksLikePropertyList(string text)
    {
        foreach (string line in text.Split('\n', 8))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<plist", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<!DOCTYPE plist", StringComparison.OrdinalIgnoreCase);
        }
        return false;
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
        var raw = ValueFollowingKey(plist, key, "integer");
        if (raw is null) return null;
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    /// <summary>Extracts a string value following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static string? PlistString(string plist, string key)
    {
        var raw = ValueFollowingKey(plist, key, "string");
        return raw?.Trim();
    }

    /// <summary>Extracts base64 or data string following a &lt;key&gt;Name&lt;/key&gt; entry in plist output.</summary>
    public static string? PlistData(string plist, string key)
    {
        var raw = ValueFollowingKey(plist, key, "data");
        return raw?.Replace("\r", "").Replace("\n", "").Trim();
    }

    /// <summary>
    /// The contents of the element that sits directly under a key, when it is of
    /// the element type asked for. Null when the key is not there, or when the key
    /// carries a different kind of value.
    ///
    /// Reading only the element right under the key is what keeps a key with the
    /// wrong value type from borrowing the next matching element further down the
    /// document. An ioregistry dump runs to thousands of lines, so a key holding
    /// text would otherwise come back with a battery capacity, a chip id or a
    /// counter belonging to a different part of the phone.
    /// </summary>
    private static string? ValueFollowingKey(string plist, string key, string element)
    {
        if (string.IsNullOrEmpty(plist)) return null;

        var m = KeyRegex(key).Match(plist);
        if (!m.Success) return null;

        int start = m.Index + m.Length;
        while (start < plist.Length && char.IsWhiteSpace(plist[start])) start++;

        string opening = "<" + element + ">";
        if (start + opening.Length > plist.Length) return null;
        if (!plist.AsSpan(start, opening.Length).SequenceEqual(opening)) return null;

        string closing = "</" + element + ">";
        int end = plist.IndexOf(closing, start + opening.Length, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : plist[(start + opening.Length)..end];
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

    /// <summary>
    /// Battery condition percentage for Android from the capacity counters under
    /// /sys/class/power_supply/battery: what the battery can still hold against
    /// what it held when new. Returns 0 when either counter is missing or
    /// nonsensical, so the caller can fall back to the health status.
    /// </summary>
    public static int ParseAndroidBatteryCondition(string? chargeFull, string? chargeFullDesign)
    {
        if (!long.TryParse((chargeFull ?? "").Trim(), out long full)) return 0;
        if (!long.TryParse((chargeFullDesign ?? "").Trim(), out long design)) return 0;
        if (full <= 0 || design <= 0) return 0;

        var pct = (int)Math.Round((double)full / design * 100);
        return Math.Clamp(pct, 1, 100);
    }

    /// <summary>
    /// Charge level in percent from `dumpsys battery`, or 0 when it cannot be read.
    /// Anchored to the start of a line because the same output carries a separate
    /// "Capacity level:" line that a loose pattern would happily match instead.
    /// </summary>
    public static int ParseAndroidChargeLevel(string? dumpsysBattery)
    {
        var match = Regex.Match(dumpsysBattery ?? "", @"^\s*level:\s*(\d+)", RegexOptions.Multiline);
        return match.Success && int.TryParse(match.Groups[1].Value, out int level)
            ? Math.Clamp(level, 0, 100)
            : 0;
    }

    /// <summary>
    /// The health status Android reports for the battery, as a stable token. The
    /// status code is a fixed list from the BatteryManager, not a percentage, so it
    /// is kept apart from the condition.
    ///
    /// These tokens are data, not text for the operator: they travel into the CSV,
    /// the report and the label, so they cannot be a word in one language. The
    /// shell puts them into the operator's language through the battery
    /// converter, which is the only place that decides wording.
    /// </summary>
    public static string ParseAndroidBatteryStatus(string? dumpsysBattery)
    {
        var match = Regex.Match(dumpsysBattery ?? "", @"^\s*health:\s*(\d+)", RegexOptions.Multiline);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int code)) return "NOBATT";

        return code switch
        {
            1 => "Unknown",
            2 => "Good",
            3 => "Overheated",
            4 => "Defective",
            5 => "Overvoltage",
            6 => "StorageFault",
            7 => "TooCold",
            // Codes 8 and up exist on newer platforms and mean states this build
            // cannot name. Reporting the battery as unreadable would raise a false
            // warning on a healthy phone, so an unnamed state is carried as unknown.
            _ => "Unknown",
        };
    }

    /// <summary>Identifier for the label: IMEI if the device has one (iPhones), else serial number.</summary>
    public static string ParseIdentifier(string imeiOutput, string serialOutput)
    {
        string imei = (imeiOutput ?? "").Trim();
        bool hasImei = imei.Length >= 14 && imei.All(char.IsDigit);
        string serial = (serialOutput ?? "").Trim();
        return hasImei ? imei : serial.Length > 0 ? serial : "NOID";
    }

    /// <summary>
    /// Extracts IMEI from the Parcel hex-dump returned by <c>service call iphonesubinfo 1</c>
    /// (or 2 for the second SIM). The output format is:
    /// <c>Result: Parcel( 0x00000000: 00000000 0000000f 00350033 00360039 ... )</c>
    /// where each line has an address prefix, then hex words. The first 8 bytes are a Parcel header
    /// (two 32-bit integers: flags and length), followed by the UTF-16BE encoded IMEI digits.
    /// Returns empty string when the output cannot be parsed.
    /// </summary>
    public static string ParseAndroidImei(string? serviceCallOutput)
    {
        if (string.IsNullOrWhiteSpace(serviceCallOutput)) return "";

        // Match hex words (4 hex digits) that appear after a colon on each line.
        // This avoids matching the address prefixes like "0x00000000:".
        var hexGroups = new List<string>();
        foreach (string line in serviceCallOutput.Split('\n'))
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;
            string dataPart = line[(colonIdx + 1)..];
            foreach (Match m in Regex.Matches(dataPart, @"([0-9a-fA-F]{4})"))
            {
                hexGroups.Add(m.Groups[1].Value);
            }
        }

        if (hexGroups.Count < 6) return ""; // Need at least header (4 groups) + some data

        // Skip the first 4 groups (8 bytes = Parcel header: two uint32)
        // The 5th group onwards contains the UTF-16BE string data
        var dataGroups = hexGroups.Skip(4).ToList();

        var bytes = new List<byte>();
        foreach (string hex in dataGroups)
        {
            byte high = Convert.ToByte(hex.Substring(0, 2), 16);
            byte low = Convert.ToByte(hex.Substring(2, 2), 16);
            bytes.Add(high);
            bytes.Add(low);
        }

        if (bytes.Count == 0) return "";

        try
        {
            // Decode as UTF-16BE (big endian)
            string decoded = Encoding.BigEndianUnicode.GetString(bytes.ToArray());

            // Extract only digits (the IMEI is numeric)
            var digits = new string(decoded.Where(char.IsDigit).ToArray());

            // Valid IMEI is 14-16 digits (15 is standard, 16 with check digit)
            if (digits.Length >= 14 && digits.Length <= 16)
                return digits;
        }
        catch
        {
            // Ignore decoding errors
        }

        return "";
    }

    /// <summary>
    /// Battery cycle count from <c>dumpsys battery</c>, or null when the handset
    /// does not report one.
    ///
    /// A stock Android carries no cycle count at all, and a few manufacturers add
    /// one under their own name. Null and zero are kept apart on purpose: a report
    /// that says zero cycles says the battery has never been used, which is a
    /// claim about a number the handset never sent.
    /// </summary>
    public static int? ParseAndroidBatteryCycleCount(string? dumpsysBattery)
    {
        if (string.IsNullOrWhiteSpace(dumpsysBattery)) return null;

        // The field name differs per manufacturer, so the known spellings are
        // tried in turn rather than assuming one of them.
        var patterns = new[]
        {
            @"^\s*cycle count:\s*(\d+)",
            @"^\s*charge_cycle:\s*(\d+)",
            @"^\s*battery_cycle:\s*(\d+)",
            @"^\s*cycle:\s*(\d+)"
        };

        foreach (string pattern in patterns)
        {
            var match = Regex.Match(dumpsysBattery, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                return count;
        }

        return null;
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

    /// <summary>
    /// Usable /data size in bytes from the output of <c>df -k /data</c>, or 0 when
    /// it cannot be read. Only the filesystem that holds user data is of interest:
    /// the other mounts in the table are far smaller and would bucket wrongly.
    ///
    /// The row is found by mount point, not by position, and not by an exact match
    /// on "/data". A handset with per-user encryption mounts the same filesystem at
    /// /data/user/0, which is what a Pixel 8 Pro reports.
    /// </summary>
    public static long ParseAndroidDataBytes(string? dfOutput)
    {
        if (string.IsNullOrWhiteSpace(dfOutput)) return 0;

        foreach (string rawLine in dfOutput.Split('\n'))
        {
            var columns = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2) continue;

            // The header row has no leading slash, so it cannot match either.
            string mount = columns[^1];
            if (!mount.StartsWith("/data", StringComparison.Ordinal)) continue;

            if (long.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long blocks))
                return blocks > 0 ? blocks * 1024 : 0;
        }

        return 0;
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
