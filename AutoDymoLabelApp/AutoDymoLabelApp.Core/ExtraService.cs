namespace Mappings
{

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