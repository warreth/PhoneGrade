using System.Text.RegularExpressions;

namespace AutoDymoLabel.Core;

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
        ["#ffcc00"] = "Geel", ["#4cd964"] = "Groen", ["#5ac8fa"] = "Blauw", ["#007aff"] = "Lichtblauw",
        ["#5856d6"] = "Paars", ["#ff2d55"] = "Roze", ["#8e8e93"] = "Grijs", ["#c69c6d"] = "Goud",
        ["#d0d1d2"] = "Zilver", ["1"] = "Zwart", ["2"] = "Wit", ["3"] = "Goud", ["4"] = "Roze",
        ["5"] = "Grijs", ["6"] = "Rood", ["7"] = "Geel", ["8"] = "Oranje", ["9"] = "Blauw",
        ["17"] = "Paars", ["18"] = "Groen",
    };

    /// <summary>ProductType (iPhone14,2) → friendly model. Unknown ProductTypes fall back to the raw value.</summary>
    public static string MapModel(string productType)
    {
        string trimmed = productType.Trim();
        return Models.TryGetValue(trimmed, out var m) ? m : trimmed.Length > 0 ? trimmed : "NOMODEL";
    }

    /// <summary>DeviceEnclosureColor raw value or hex → Dutch color name.</summary>
    public static string MapColor(string raw)
    {
        string trimmed = raw.Trim().ToLowerInvariant();
        return Colors.TryGetValue(trimmed, out var c) ? c : "NOCOLOR";
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
