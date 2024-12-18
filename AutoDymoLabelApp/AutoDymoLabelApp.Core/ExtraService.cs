namespace Mappings
{
// Yes i could do this using an abstract class, but im not gonna add any more mappings soo
public class ModelMapper
{
    // Define the dictionary directly in the program
    private readonly Dictionary<string, string> modelMapping = new()
    {
        { "iPhone1,1", "iPhone" },
        { "iPhone1,2", "3G" },
        { "iPhone2,1", "3GS" },
        { "iPhone3,1", "4" },
        { "iPhone3,2", "4" },
        { "iPhone3,3", "4" },
        { "iPhone4,1", "4S" },
        { "iPhone5,1", "5" },
        { "iPhone5,2", "5" },
        { "iPhone5,3", "5C" },
        { "iPhone5,4", "5C" },
        { "iPhone6,1", "5S" },
        { "iPhone6,2", "5S" },
        { "iPhone7,1", "6Plus" },
        { "iPhone7,2", "6" },
        { "iPhone8,1", "6s" },
        { "iPhone8,2", "6sPlus" },
        { "iPhone8,4", "SE" },
        { "iPhone9,1", "7" },
        { "iPhone9,2", "7Plus" },
        { "iPhone9,3", "7" },
        { "iPhone9,4", "7Plus" },
        { "iPhone10,1", "8" },
        { "iPhone10,2", "8Plus" },
        { "iPhone10,3", "X" },
        { "iPhone10,4", "8" },
        { "iPhone10,5", "8Plus" },
        { "iPhone10,6", "X" },
        { "iPhone11,2", "XS" },
        { "iPhone11,4", "XSMax" },
        { "iPhone11,6", "XSMax" },
        { "iPhone11,8", "XR" },
        { "iPhone12,1", "11" },
        { "iPhone12,3", "11Pro" },
        { "iPhone12,5", "11ProMax" },
        { "iPhone12,8", "SE2" },
        { "iPhone13,1", "12Mini" },
        { "iPhone13,2", "12" },
        { "iPhone13,3", "12Pro" },
        { "iPhone13,4", "12ProMax" },
        { "iPhone14,2", "13Pro" },
        { "iPhone14,3", "13ProMax" },
        { "iPhone14,4", "13Mini" },
        { "iPhone14,5", "13" },
        { "iPhone14,6", "SE3" },
        { "iPhone14,7", "14" },
        { "iPhone14,8", "14Plus" },
        { "iPhone15,2", "14Pro" },
        { "iPhone15,3", "14ProMax" },
        { "iPhone15,4", "15" },
        { "iPhone15,5", "15Plus" },
        { "iPhone16,1", "15Pro" },
        { "iPhone16,2", "15ProMax" },
        { "iPad1,1", "iPad" },
        { "iPad2,1", "iPad 2" },
        { "iPad2,2", "iPad 2" },
        { "iPad2,3", "iPad 2" },
        { "iPad2,4", "iPad 2" },
        { "iPad2,5", "iPad mini" },
        { "iPad2,6", "iPad mini" },
        { "iPad2,7", "iPad mini" },
        { "iPad3,1", "iPad (3rd gen)" },
        { "iPad3,2", "iPad (3rd gen)" },
        { "iPad3,3", "iPad (3rd gen)" },
        { "iPad3,4", "iPad (4th gen)" },
        { "iPad3,5", "iPad (4th gen)" },
        { "iPad3,6", "iPad (4th gen)" },
        { "iPad4,1", "iPad Air" },
        { "iPad4,2", "iPad Air" },
        { "iPad4,3", "iPad Air" },
        { "iPad4,4", "iPad mini 2" },
        { "iPad4,5", "iPad mini 2" },
        { "iPad4,6", "iPad mini 2" },
        { "iPad4,7", "iPad mini 3" },
        { "iPad4,8", "iPad mini 3" },
        { "iPad4,9", "iPad mini 3" },
        { "iPad5,1", "iPad mini 4" },
        { "iPad5,2", "iPad mini 4" },
        { "iPad5,3", "iPad Air 2" },
        { "iPad5,4", "iPad Air 2" },
        { "iPod1,1", "iPod touch" },
        { "iPod2,1", "iPod touch (2nd gen)" },
        { "iPod3,1", "iPod touch (3rd gen)" },
        { "iPod4,1", "iPod touch (4th gen)" },
        { "iPod5,1", "iPod touch (5th gen)" },
        { "iPod7,1", "iPod touch (6th gen)" },
        { "iPod9,1", "iPod touch (7th gen)" }
    };

    // Method to map the product type to the corresponding model
    public string MapModel(string modelIn)
    {
        return modelMapping.TryGetValue(modelIn, out string? model) ? model : "Unknown Model";
    }
}

public class ColorMapper
{
    // Define the dictionary directly in the program
    private readonly Dictionary<string, string> colorMapping = new()
    {
        { "#3b3b3c", "Zwart" },
        { "#ffffff", "Wit" },
        { "#ff3b30", "Rood" },
        { "#ff9500", "Oranje" },
        { "#ffcc00", "Geel" },
        { "#4cd964", "Groen" },
        { "#5ac8fa", "Blauw" },
        { "#007aff", "Lichtblauw" },
        { "#5856d6", "Paars" },
        { "#ff2d55", "Roze" },
        { "#8e8e93", "Grijs" },
        { "#c69c6d", "Goud" },
        { "#d0d1d2", "Zilver" },
        { "1", "Zwart" },
        { "2", "Wit" },
        { "3", "Goud" },
        { "4", "Roze" },
        { "5", "Grijs" },
        { "6", "Rood" },
        { "7", "Geel" },
        { "8", "Oranje" },
        { "9", "Blauw" },
        { "17", "Paars" },
        { "18", "Groen" }
    };

    // Method to map a color code to its corresponding color name
    public string MapColor(string colorIn)
    {
        return colorMapping.TryGetValue(colorIn, out string? color) ? color : "Unknown Color";
    }
}

}

namespace Parsing
{
public static class ParsingBatteryHealth
{
    public static bool ParseBatteryHealth(string plistOutput, out double batteryHealth)
    {
        batteryHealth = 0; // Initialize the out parameter

        double? calculatedHealth = CalculateBatteryHealth(plistOutput);

        if (calculatedHealth.HasValue)
        {
            batteryHealth = calculatedHealth.Value; // Set the out parameter
            return true; // Return success
        }

        return false; // Return failure if no valid health data
    }
    private static double? CalculateBatteryHealth(string plistOutput)
    {
        int? appleRawMaxCapacity = null;
        int? designCapacity = null;
        int? maxCapacity = null;
        int? nominalChargeCapacity = null;

        // Split the plist output into lines for parsing
        var lines = plistOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        // Iterate through lines to extract values
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("<key>AppleRawMaxCapacity</key>"))
                appleRawMaxCapacity = ExtractIntegerValue(lines, i + 1);
            else if (lines[i].Contains("<key>DesignCapacity</key>"))
                designCapacity = ExtractIntegerValue(lines, i + 1);
            else if (lines[i].Contains("<key>MaxCapacity</key>"))
                maxCapacity = ExtractIntegerValue(lines, i + 1);
            else if (lines[i].Contains("<key>NominalChargeCapacity</key>"))
                nominalChargeCapacity = ExtractIntegerValue(lines, i + 1);
        }

        // Use the best available capacity values to calculate battery health
        if (designCapacity.HasValue && designCapacity.Value > 0)
        {
            double batteryHealth = 0;

            if (appleRawMaxCapacity.HasValue)
                batteryHealth = (double)appleRawMaxCapacity.Value / designCapacity.Value * 100;
            else if (maxCapacity.HasValue)
                batteryHealth = (double)maxCapacity.Value / designCapacity.Value * 100;
            else if (nominalChargeCapacity.HasValue)
                batteryHealth = (double)nominalChargeCapacity.Value / designCapacity.Value * 100;

            // Cap the battery health percentage at 100%
            return Math.Min(batteryHealth, 100.0);
        }

        return null; // Return null if unable to calculate
    }

    private static int? ExtractIntegerValue(string[] lines, int index)
    {
        if (index < lines.Length && lines[index].Contains("<integer>"))
        {
            var start = lines[index].IndexOf("<integer>") + "<integer>".Length;
            var end = lines[index].IndexOf("</integer>");
            if (start >= 0 && end > start)
            {
                if (int.TryParse(lines[index].Substring(start, end - start), out int value))
                    return value;
            }
        }
        return null;
    }

}
public static class ParsingDeviceIdentifier
{
    public static bool ParseDeviceIdentifier(string imei, string serial, out string outDeviceIdentifier)
    {
        if (DeviceHasIMEI(imei))
        {
            outDeviceIdentifier = imei; 
            return true;
        }
        else
        {
            outDeviceIdentifier = serial; //if no imei, then return the serial
            return true;
        }
    }
    private static bool DeviceHasIMEI(string imei)
    {

        // Check if the output contains a valid IMEI
        if (imei.Contains("NO OUTPUT") || imei.Contains("NOIMEI")) { return false; } // No IMEI found

        else { return true; } // Device has an IMEI
    }
}
public static class ParsingStorage
{
    public static bool ParseStorage(string plistOutput, out string totalDiskCapacityGB)
    {
        totalDiskCapacityGB = "NO STORAGE";

        if (string.IsNullOrWhiteSpace(plistOutput))
            return false;

        // Split the output into lines
        var lines = plistOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Check for "TotalDiskCapacity" in the line
            if (line.Contains("TotalDiskCapacity"))
            {
                // Remove "TotalDiskCapacity: " and parse the numeric value
                string capacityString = line.Replace("TotalDiskCapacity: ", "").Trim();
                if (long.TryParse(capacityString, out long totalDiskCapacity))
                {
                    // Convert capacity to GB
                    totalDiskCapacityGB = (totalDiskCapacity / 1000000000).ToString();
                    return true;
                }
                else
                {
                    totalDiskCapacityGB = "Couldnt parse";
                }
            }
        }
        return false;
    }
}
}