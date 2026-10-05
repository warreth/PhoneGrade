using System.Globalization;
using System.Text;

namespace PhoneGrade.Core;

/// <summary>
/// The raw numbers as CSV, for a spreadsheet.
///
/// The shape is one row per field rather than one row per device, because this is
/// the format a shop pastes into their own sheet, and a row per device with forty
/// columns does not survive being pasted. Every row carries the device and the
/// section it belongs to, so a spreadsheet filter can still regroup it.
/// </summary>
public static class CsvReport
{
    /// <summary>Reads as a header, writes as rows. Comma, semicolon and tab all open it.</summary>
    private static readonly string[] Headings = ["Section", "Field", "Value"];

    public static string Write(DeviceData data)
    {
        var csv = new StringBuilder();
        Row(csv, Headings);

        foreach ((string section, string field, string value) in Fields(data))
            Row(csv, [section, field, value]);

        return csv.ToString();
    }

    private static IEnumerable<(string, string, string)> Fields(DeviceData data)
    {
        foreach ((string field, string value) in new[]
        {
            ("Identifier", data.Identifier),
            ("Device id", data.DeviceId),
            ("Model", data.Model),
            ("Product type", data.ProductType),
            ("Color", data.Color),
            ("Storage", data.Storage),
            ("Memory", data.Memory),
            ("OS version", data.IosVersion ?? ""),
            ("FMI verification", data.FmiVerificationSource),
        })
            yield return ("Device", field, value);

        foreach ((string field, string value) in new[]
        {
            ("Condition", data.BatteryHealth),
            ("Condition is low", LabelFields.From(data).BatteryIsLow ? "yes" : "no"),
            ("Charge", data.BatteryLevel?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ("Cycles", data.BatteryCycleCount?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ("Design capacity mAh", data.BatteryDesignCapacity.ToString(CultureInfo.InvariantCulture)),
            ("Current capacity mAh", data.BatteryCurrentCapacity.ToString(CultureInfo.InvariantCulture)),
            ("Voltage mV", data.BatteryVoltage.ToString(CultureInfo.InvariantCulture)),
            ("Temperature 0.1C", data.BatteryTemperature.ToString(CultureInfo.InvariantCulture)),
            ("Serial number", data.BatterySerialNumber),
            ("Factory serial number", data.OriginalBatterySerialNumber),
        })
            yield return ("Battery", field, value);

        foreach ((string field, string value) in new[]
        {
            ("Wi-Fi MAC", data.WifiMacAddress),
            ("Bluetooth MAC", data.BluetoothMacAddress),
            ("Cellular address", data.CellularAddress),
            ("IMEI2", data.Imei2),
        })
            yield return ("Network", field, value);

        foreach ((string field, string value) in new[]
        {
            ("Activation lock", data.ActivationLock?.ToString() ?? ""),
            ("Carrier lock iOS", Lock(data.CarrierLockIOS?.IsCarrierLocked)),
            ("Carrier iOS", data.CarrierLockIOS?.CarrierName ?? ""),
            ("Carrier lock Android", Lock(data.CarrierLockAndroid?.IsCarrierLocked)),
            ("Carrier Android", data.CarrierLockAndroid?.CarrierName ?? ""),
            ("SIM state", data.CarrierLockAndroid?.SIMState ?? ""),
            ("Factory reset protection", data.FactoryResetProtection?.ToString() ?? ""),
            ("Blacklisted", data.Blacklist is null ? "" : data.Blacklist.IsBlacklisted ? "yes" : "no"),
            ("Blacklist reason", data.Blacklist?.Reason ?? ""),
            ("Blacklist source", data.Blacklist?.Source ?? ""),
        })
            yield return ("Security", field, value);

        foreach ((string field, string value) in new[]
        {
            ("Display", data.DisplaySerialNumber),
            ("Cover glass", data.CoverGlassSerialNumber),
            ("Front camera", data.FrontCameraSerialNumber),
            ("Rear camera", data.RearCameraSerialNumber),
            ("Motherboard", data.MotherboardSerialNumber),
            ("Touch ID / Face ID", data.TouchIdFaceIdSerialNumber),
        })
            yield return ("Component serial", field, value);

        foreach (ComponentStatus check in data.ComponentChecks)
            yield return ("Check",
                check.Name,
                string.Join("; ", new[]
                {
                    $"read={check.SerialRead ?? ""}",
                    $"factory={check.SerialOriginal ?? ""}",
                    $"status={check.Status}",
                    check.Description ?? "",
                    check.Details ?? "",
                }.Where(part => !part.EndsWith('='))));

        if (data.InteractiveTests?.Tests is { } tests)
            foreach (InteractiveTestResult test in tests)
                yield return ("Test",
                    test.Name,
                    string.Join("; ", new[]
                    {
                        $"status={test.Status}",
                        $"durationMs={test.DurationMs.ToString(CultureInfo.InvariantCulture)}",
                        test.Notes ?? "",
                    }));

        foreach (string withheld in data.WithheldReads)
            yield return ("Not read", withheld, "");

        yield return ("Grading", "Grade", data.Quality);
        yield return ("Grading", "Invoice method", data.PayMethod);
    }

    private static string Lock(bool? locked) => locked switch
    {
        true => "locked",
        false => "unlocked",
        null => "",
    };

    /// <summary>
    /// One row. A value with a comma, a quote or a newline in it is quoted and its
    /// quotes doubled, which is the whole of RFC 4180 and the reason a serial
    /// containing a quote does not split a row in half in Excel.
    /// </summary>
    private static void Row(StringBuilder csv, IReadOnlyList<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0) csv.Append(',');
            csv.Append(Cell(cells[i]));
        }

        csv.Append("\r\n");
    }

    private static string Cell(string value)
    {
        bool needsQuotes = value.Contains(',') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}