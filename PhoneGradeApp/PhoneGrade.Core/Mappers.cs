using System.Globalization;
using System.Text.RegularExpressions;

namespace PhoneGrade.Core;

/// <summary>
/// Maps libimobiledevice keys to label text: model names (iPhone14,2 → "13 Pro"),
/// enclosure colors (raw or hex) and storage bucketing (64 GB, 128 GB, …).
/// </summary>
public static partial class Mappers
{
    private static readonly Dictionary<string, string> Models = new()
    {
        ["iPhone1,1"] = "iPhone", ["iPhone1,2"] = "3G", ["iPhone2,1"] = "3GS",
        ["iPhone3,1"] = "4", ["iPhone3,2"] = "4", ["iPhone3,3"] = "4", ["iPhone4,1"] = "4S",
        ["iPhone5,1"] = "5", ["iPhone5,2"] = "5", ["iPhone5,3"] = "5C", ["iPhone5,4"] = "5C",
        ["iPhone6,1"] = "5S", ["iPhone6,2"] = "5S", ["iPhone7,1"] = "6Plus", ["iPhone7,2"] = "6",
        ["iPhone8,1"] = "6s", ["iPhone8,2"] = "6sPlus", ["iPhone8,4"] = "SE",
        ["iPhone9,1"] = "7", ["iPhone9,2"] = "7Plus", ["iPhone9,3"] = "7", ["iPhone9,4"] = "7Plus",
        ["iPhone10,1"] = "8", ["iPhone10,2"] = "8Plus", ["iPhone10,3"] = "X",
        ["iPhone10,4"] = "8", ["iPhone10,5"] = "8Plus", ["iPhone10,6"] = "X",
        ["iPhone11,2"] = "XS", ["iPhone11,4"] = "XSMax", ["iPhone11,6"] = "XSMax", ["iPhone11,8"] = "XR",
        ["iPhone12,1"] = "11", ["iPhone12,3"] = "11Pro", ["iPhone12,5"] = "11ProMax", ["iPhone12,8"] = "SE2",
        ["iPhone13,1"] = "12Mini", ["iPhone13,2"] = "12", ["iPhone13,3"] = "12Pro", ["iPhone13,4"] = "12ProMax",
        ["iPhone14,2"] = "13Pro", ["iPhone14,3"] = "13ProMax", ["iPhone14,4"] = "13Mini", ["iPhone14,5"] = "13",
        ["iPhone14,6"] = "SE3", ["iPhone14,7"] = "14", ["iPhone14,8"] = "14Plus",
        ["iPhone15,2"] = "14Pro", ["iPhone15,3"] = "14ProMax",
        ["iPhone15,4"] = "15", ["iPhone15,5"] = "15Plus", ["iPhone16,1"] = "15Pro", ["iPhone16,2"] = "15ProMax",
        ["iPhone17,3"] = "16", ["iPhone17,4"] = "16Plus", ["iPhone17,1"] = "16Pro", ["iPhone17,2"] = "16ProMax",
        ["iPhone17,5"] = "16e",
        ["iPad1,1"] = "iPad", ["iPad2,1"] = "iPad2", ["iPad2,2"] = "iPad2", ["iPad2,3"] = "iPad2", ["iPad2,4"] = "iPad2",
        ["iPad2,5"] = "iPadMini", ["iPad2,6"] = "iPadMini", ["iPad2,7"] = "iPadMini",
        ["iPad3,1"] = "iPad3", ["iPad3,2"] = "iPad3", ["iPad3,3"] = "iPad3",
        ["iPad3,4"] = "iPad4", ["iPad3,5"] = "iPad4", ["iPad3,6"] = "iPad4",
        ["iPad4,1"] = "iPadAir", ["iPad4,2"] = "iPadAir", ["iPad4,3"] = "iPadAir",
        ["iPad4,4"] = "iPadMini2", ["iPad4,5"] = "iPadMini2", ["iPad4,6"] = "iPadMini2",
        ["iPad4,7"] = "iPadMini3", ["iPad4,8"] = "iPadMini3", ["iPad4,9"] = "iPadMini3",
        ["iPad5,1"] = "iPadMini4", ["iPad5,2"] = "iPadMini4", ["iPad5,3"] = "iPadAir2", ["iPad5,4"] = "iPadAir2",
        ["iPad6,3"] = "iPadPro9.7", ["iPad6,4"] = "iPadPro9.7", ["iPad6,7"] = "iPadPro12.9", ["iPad6,8"] = "iPadPro12.9",
        ["iPad6,11"] = "iPad5", ["iPad6,12"] = "iPad5", ["iPad7,1"] = "iPadPro12.9(2)", ["iPad7,2"] = "iPadPro12.9(2)",
        ["iPad7,3"] = "iPadPro10.5", ["iPad7,4"] = "iPadPro10.5", ["iPad7,5"] = "iPad6", ["iPad7,6"] = "iPad6",
        ["iPad7,11"] = "iPad7", ["iPad7,12"] = "iPad7", ["iPad8,1"] = "iPadPro11", ["iPad8,2"] = "iPadPro11",
        ["iPad8,3"] = "iPadPro11", ["iPad8,4"] = "iPadPro11", ["iPad8,5"] = "iPadPro12.9(3)", ["iPad8,6"] = "iPadPro12.9(3)",
        ["iPad8,7"] = "iPadPro12.9(3)", ["iPad8,8"] = "iPadPro12.9(3)", ["iPad8,9"] = "iPadAir3", ["iPad8,10"] = "iPadAir3",
        ["iPad11,1"] = "iPadMini5", ["iPad11,2"] = "iPadMini5", ["iPad11,3"] = "iPadAir4", ["iPad11,4"] = "iPadAir4",
        ["iPad11,6"] = "iPad8", ["iPad11,7"] = "iPad8", ["iPad12,1"] = "iPad8", ["iPad12,2"] = "iPad8",
        ["iPad13,1"] = "iPadAir4", ["iPad13,2"] = "iPadAir4", ["iPad13,4"] = "iPadPro11(2)", ["iPad13,5"] = "iPadPro11(2)",
        ["iPad13,6"] = "iPadPro11(2)", ["iPad13,7"] = "iPadPro11(2)", ["iPad13,8"] = "iPadPro12.9(4)", ["iPad13,9"] = "iPadPro12.9(4)",
        ["iPad13,10"] = "iPadPro12.9(4)", ["iPad13,11"] = "iPadPro12.9(4)", ["iPad13,16"] = "iPadAir5", ["iPad13,17"] = "iPadAir5",
        ["iPad13,18"] = "iPad10", ["iPad13,19"] = "iPad10",
        ["iPad14,1"] = "iPadMini6", ["iPad14,2"] = "iPadMini6", ["iPad14,3"] = "iPadAir11", ["iPad14,4"] = "iPadAir11",
        ["iPad14,5"] = "iPadPro11(3)", ["iPad14,6"] = "iPadPro11(3)", ["iPad14,7"] = "iPadAirM2", ["iPad14,8"] = "iPadAirM2",
        ["iPad14,9"] = "iPadPro13M4", ["iPad14,10"] = "iPadPro13M4", ["iPad15,7"] = "iPadPro11M4", ["iPad15,8"] = "iPadPro11M4",
        ["iPad16,1"] = "iPad11", ["iPad16,2"] = "iPad11", ["iPad16,3"] = "iPadMini7", ["iPad16,4"] = "iPadMini7",
        ["iPad16,5"] = "iPadAirM3", ["iPad16,6"] = "iPadAirM3", ["iPad16,7"] = "iPadPro13M5", ["iPad16,8"] = "iPadPro13M5",
        ["iPod1,1"] = "iPodTouch", ["iPod2,1"] = "iPodTouch2", ["iPod3,1"] = "iPodTouch3", ["iPod4,1"] = "iPodTouch4",
        ["iPod5,1"] = "iPodTouch5", ["iPod7,1"] = "iPodTouch6", ["iPod9,1"] = "iPodTouch7",
    };

    private static readonly Dictionary<string, string> Colors = new()
    {
        ["#3b3b3c"] = "Zwart", ["#ffffff"] = "Wit", ["#ff3b30"] = "Rood", ["#ff9500"] = "Oranje",
        ["#ffcc00"] = "Goud", ["#4cd964"] = "Groen", ["#5ac8fa"] = "Blauw", ["#007aff"] = "Lichtblauw",
        ["#5856d6"] = "Paars", ["#ff2d55"] = "Roze", ["#8e8e93"] = "Grijs", ["#c69c6d"] = "Goud",
        ["#d0d1d2"] = "Zilver", ["1"] = "Zwart", ["2"] = "Wit", ["3"] = "Goud", ["4"] = "Roze",
        ["5"] = "Grijs", ["6"] = "Rood", ["7"] = "Goud", ["8"] = "Oranje", ["9"] = "Blauw",
        ["17"] = "Paars", ["18"] = "Groen",
        
        // Common raw Apple color strings
        ["black"] = "Zwart", ["white"] = "Wit", ["gold"] = "Goud", ["silver"] = "Zilver",
        ["rose gold"] = "Rosé Goud", ["space gray"] = "Spacegrijs", ["space grey"] = "Spacegrijs",
        ["midnight green"] = "Middernachtgroen", ["pacific blue"] = "Pacifisch Blauw",
        ["graphite"] = "Grafiet", ["sierra blue"] = "Sierra Blauw", ["alpine green"] = "Alpengroen",
        ["midnight"] = "Middernacht", ["starlight"] = "Sterrenlicht", ["blue"] = "Blauw",
        ["purple"] = "Paars", ["red"] = "Rood", ["green"] = "Groen", ["yellow"] = "Goud",
        ["geel"] = "Goud",
        ["pink"] = "Roze", ["coral"] = "Koraal", ["product(red)"] = "Rood",

        // Plain colour words. Android vendors usually publish a marketing name
        // rather than Apple's raw strings, and those are ordinary words, so they
        // resolve here instead of in a per-vendor list.
        ["obsidian"] = "Obsidiaan", ["porcelain"] = "Porselein", ["hazel"] = "Hazel",
        ["rose"] = "Rosé", ["charcoal"] = "Houtskool", ["mint"] = "Mint", ["navy"] = "Navy",
        ["sage"] = "Sage", ["olive"] = "Olijf", ["cream"] = "Crème", ["beige"] = "Beige",
        ["brown"] = "Bruin", ["orange"] = "Oranje", ["teal"] = "Blauwgroen",
        ["lilac"] = "Lila", ["lavender"] = "Lavendel", ["violet"] = "Violet",
        ["titanium"] = "Titaan", ["sand"] = "Zand", ["graphite"] = "Grafiet",
        ["transparent"] = "Transparant", ["clear"] = "Transparant"
    };

    /// <summary>ProductType (iPhone14,2) → friendly model. Unknown ProductTypes fall back to the raw value.</summary>
    public static string MapModel(string productType)
    {
        string trimmed = productType.Trim();
        return Models.TryGetValue(trimmed, out var m) ? m : trimmed.Length > 0 ? trimmed : "Onbekend";
    }

    /// <summary>Formats the model for UI display with family prefix (e.g., 'iPhone 8' instead of raw '8').</summary>
    public static string FormatDisplayModel(string? model, string? productType = null)
    {
        if (string.IsNullOrWhiteSpace(model) || model == "Onbekend") return "Onbekend Toestel";
        if (model.StartsWith("iPhone") || model.StartsWith("iPad") || model.StartsWith("iPod")) return model;

        // An Android model is already the marketing name ("Google Pixel 8 Pro").
        // Prefixing it with "iPhone" turned a Pixel into an iPhone.
        if (IsAndroidProductType(productType)) return model;

        if (productType != null && productType.StartsWith("iPad"))
            return $"iPad {model}";

        return $"iPhone {model}";
    }

    /// <summary>True for the ProductType the Android collector writes, e.g. "Android (Google Pixel 8 Pro)".</summary>
    public static bool IsAndroidProductType(string? productType) =>
        productType is not null && productType.StartsWith("Android", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The only brands a plain title case gets wrong, because they are written as
    /// acronyms. Everything else title cases correctly from the lowercased string
    /// Android reports, so there is no reason to enumerate the rest.
    /// </summary>
    private static readonly HashSet<string> AndroidBrandAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "lg", "zte", "htc", "umx", "tcl", "bbk", "meizu", "gionee", "intex", "micromax",
    };

    /// <summary>Display name for an Android handset: "Google Pixel 8 Pro".</summary>
    public static string MapAndroidDisplayModel(string? brand, string? model)
    {
        string modelName = (model ?? "").Trim();
        string brandName = (brand ?? "").Trim();
        if (brandName.Length == 0) return modelName;
        if (modelName.Length == 0) return MapAndroidBrand(brandName);

        // Some handsets repeat the brand inside the model ("motorola one vision").
        // Drop the repeat rather than print it twice, and keep the proper spelling.
        string spelled = MapAndroidBrand(brandName);
        if (modelName.StartsWith(brandName, StringComparison.OrdinalIgnoreCase))
        {
            string rest = modelName[brandName.Length..].Trim();
            return rest.Length == 0 ? spelled : $"{spelled} {rest}";
        }

        return $"{spelled} {modelName}";
    }

    /// <summary>
    /// Android brand name with its proper capitalisation. <c>ro.product.brand</c>
    /// arrives lowercased, so "google" has to become "Google". Acronym brands are
    /// the exception a title case cannot handle; anything else is title cased
    /// per word, so a multi-word brand like "motorola mobility" also comes out right.
    /// </summary>
    public static string MapAndroidBrand(string? brand)
    {
        string trimmed = (brand ?? "").Trim();
        if (trimmed.Length == 0) return "";
        if (AndroidBrandAcronyms.Contains(trimmed)) return trimmed.ToUpperInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    /// <summary>DeviceEnclosureColor raw value or hex → Dutch color name.</summary>
    public static string MapColor(string raw)
    {
        string trimmed = raw.Trim().ToLowerInvariant();
        return Colors.TryGetValue(trimmed, out var c) ? c : "Onbekend";
    }

    // Android colour properties carry a three letter code, not a word. A title case
    // cannot expand an abbreviation, so these are the codes that need listing; any
    // other code is passed through unchanged rather than guessed at.
    private static readonly Dictionary<string, string> AndroidColorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WHT"] = "Wit", ["BLK"] = "Zwart", ["BLU"] = "Blauw", ["PNG"] = "Roze", ["RSE"] = "Rosé",
        ["ORG"] = "Oranje", ["GRN"] = "Groen", ["GRY"] = "Grijs", ["MNT"] = "Mint", ["HZL"] = "Hazel",
        ["OBS"] = "Obsidiaan", ["POR"] = "Porselein", ["RED"] = "Rood", ["BRN"] = "Bruin",
        ["GLD"] = "Goud", ["SLV"] = "Zilver", ["TAN"] = "Beige", ["PUR"] = "Paars", ["CRM"] = "Crème",
        ["BLU2"] = "Blauw", ["NVY"] = "Navy", ["SGE"] = "Sage", ["OLV"] = "Olijf", ["SKY"] = "Lichtblauw",
    };

    /// <summary>
    /// Android colour property → Dutch colour name, or the NOCOLOR placeholder when
    /// the device exposes no colour. A full word goes through the normal colour
    /// table first, so "Obsidian" and "black" both resolve without an entry here.
    /// </summary>
    public static string MapAndroidColor(string? raw)
    {
        string trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) return "NOCOLOR";

        if (Colors.TryGetValue(trimmed.ToLowerInvariant(), out string? known)) return known;
        if (AndroidColorCodes.TryGetValue(trimmed, out string? code)) return code;

        // An unrecognised three letter code is left visible rather than turned into
        // a guess: a wrong colour on the label is worse than an unfamiliar one.
        if (LooksLikeAbbreviation(trimmed)) return trimmed.ToUpperInvariant();

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    // Two to four letters with no digits and no separators, which is the shape
    // Android uses for its colour codes. "128GB" and "Micron" are not abbreviations.
    [GeneratedRegex(@"^[A-Za-z]{2,4}$")]
    private static partial Regex AbbreviationRegex();
    private static bool LooksLikeAbbreviation(string value) => AbbreviationRegex().IsMatch(value);

    // The storage and memory properties are comma separated records whose first
    // field is the capacity: "128GB,Samsung" and "12GiB,Micron,LPDDR5,ff07".
    [GeneratedRegex(@"\b(\d+)\s*(GB|TB|GiB|MB)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CapacityRegex();

    /// <summary>
    /// Android storage → marketing capacity. The property is preferred because it
    /// is the number the device is sold as; the df fallback buckets the usable size,
    /// which is a little lower because the system takes its own partitions first.
    /// </summary>
    public static string MapAndroidStorage(string? raw, long dataBytes)
    {
        string advertised = ExtractCapacity(raw);
        if (advertised.Length > 0) return advertised;

        return dataBytes > 0 ? MapStorage(dataBytes) : "NOSTORAGE";
    }

    /// <summary>Installed memory, or the NOMEMORY placeholder.</summary>
    public static string MapAndroidMemory(string? raw)
    {
        string capacity = ExtractCapacity(raw);
        return capacity.Length > 0 ? capacity : "NOMEMORY";
    }

    /// <summary>
    /// Pulls the capacity out of a hardware property value. "128GB,Samsung" gives
    /// "128GB"; the unit is normalised to the decimal form used on the label.
    /// </summary>
    private static string ExtractCapacity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var match = CapacityRegex().Match(raw);
        if (!match.Success) return "";

        if (!int.TryParse(match.Groups[1].Value, out int amount) || amount <= 0) return "";

        // GiB is what the device counts in; GB is what it is sold in. A 12GiB
        // module is a 12GB phone, so the binary unit is only a spelling difference.
        string unit = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "tb" => "TB",
            "mb" => "MB",
            _ => "GB",
        };

        // Some devices report terabytes as 1024GB rather than 1TB.
        return unit == "GB" && amount % 1024 == 0 ? $"{amount / 1024}TB" : $"{amount}{unit}";
    }

    /// <summary>TotalDiskCapacity bytes → nearest marketing bucket (64, 128, 256, 512 GB, 1/2 TB).</summary>
    public static string MapStorage(long totalBytes)
    {
        double gb = totalBytes / 1e9;
        int[] buckets = [32, 64, 128, 256, 512, 1024, 2048];
        foreach (int b in buckets)
            if (gb < b * 1.05) return b >= 1024 ? $"{b / 1024}TB" : $"{b}GB";
        return $"{Math.Round(gb)}GB";
    }

    // iphone model or ipad model, matched case-insensitively as substring anywhere in text
    [GeneratedRegex(@"(?i)\biphone\b|\bipad\b|\bipod\b")]
    private static partial Regex DeviceFamilyRegex();
    public static bool LooksLikeDevice(string s) => s.Length > 0 && DeviceFamilyRegex().IsMatch(s);
}
