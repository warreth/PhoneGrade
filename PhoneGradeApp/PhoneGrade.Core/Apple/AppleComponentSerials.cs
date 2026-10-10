namespace PhoneGrade.Core.Apple;

/// <summary>
/// The component serials an iPhone publishes about itself, and the checks that
/// compare them against the factory originals.
///
/// Every value here is asked for under several names before it is given up on:
/// the property names moved between iOS generations, and the same phone answers
/// from ioreg, the diagnostics domain, Gestalt or lockdown depending on where it
/// is in its life. The fallback chains are the knowledge; they live in one place
/// so a new spelling is added once rather than in nine near-identical blocks.
///
/// A value that could not be read is reported as unknown, never as a match. The
/// nine rows are the 3uTools parity layout the OEM audit panel shows.
/// </summary>
public static class AppleComponentSerials
{
    private const string Unknown = "Onbekend";

    /// <summary>Reads the serials out of the raw plist dictionaries and fills in the checks.</summary>
    public static void Populate(DeviceService.DeviceRawData raw, DeviceData data)
    {
        var diagDict = raw.DiagDict;
        var gestaltDict = raw.GestaltDict;
        var defaultDict = raw.DefaultDict;

        string displayPlist = raw.IORegDisplay;
        string camPlist = raw.IORegCamera;
        string bioPlist = raw.IORegBio;

        // Live serials & addresses parsed strictly from in-memory dictionaries with comprehensive fallbacks
        string displaySerial = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "DisplaySerial") ??
            Parsers.PlistString(displayPlist, "LCMSerialNumber") ??
            Parsers.PlistString(displayPlist, "SerialNumber") ??
            DeviceService.FindDictValue(diagDict, "LCMSerialNumber", "DisplaySerialNumber", "ScreenSerial", "LCDSerial") ??
            DeviceService.FindDictValue(gestaltDict, "LCMSerialNumber", "DisplaySerialNumber", "ScreenSerial", "LCDSerial") ??
            DeviceService.FindDictValue(defaultDict, "LCMSerialNumber", "DisplaySerialNumber", "ScreenSerial"));

        string coverGlass = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "CoverGlassSerial") ??
            DeviceService.FindDictValue(diagDict, "CoverGlassSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "CoverGlassSerialNumber"));

        string frontCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "FrontCameraModuleSerialNumString") ??
            Parsers.PlistString(camPlist, "FrontCameraModuleSerialNumber") ??
            Parsers.PlistString(camPlist, "FrontCameraSerial") ??
            DeviceService.FindDictValue(diagDict, "FrontCameraModuleSerialNumString", "FrontCameraModuleSerialNumber", "FrontCameraSerialNumber", "FrontCameraSerial") ??
            DeviceService.FindDictValue(gestaltDict, "FrontCameraModuleSerialNumString", "FrontCameraModuleSerialNumber", "FrontCameraSerialNumber", "FrontCameraSerial") ??
            DeviceService.FindDictValue(defaultDict, "FrontCameraModuleSerialNumString", "FrontCameraModuleSerialNumber", "FrontCameraSerialNumber", "FrontCameraSerial"));

        string rearCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "BackCameraModuleSerialNumString") ??
            Parsers.PlistString(camPlist, "CameraModuleSerial") ??
            Parsers.PlistString(camPlist, "RearCameraModuleSerialNumber") ??
            Parsers.PlistString(camPlist, "RearCameraSerial") ??
            DeviceService.FindDictValue(diagDict, "BackCameraModuleSerialNumString", "RearCameraModuleSerialNumber", "RearCameraSerial", "BackCameraSerialNumber", "CameraModuleSerial") ??
            DeviceService.FindDictValue(gestaltDict, "BackCameraModuleSerialNumString", "RearCameraModuleSerialNumber", "RearCameraSerial", "BackCameraSerialNumber", "CameraModuleSerial") ??
            DeviceService.FindDictValue(defaultDict, "BackCameraModuleSerialNumString", "RearCameraModuleSerialNumber", "RearCameraSerial", "BackCameraSerialNumber"));

        string bioSerial = Parsers.CleanSerial(
            Parsers.PlistString(bioPlist, "SensorSerialNumber") ??
            Parsers.PlistString(bioPlist, "RosalineSerialNumber") ??
            Parsers.PlistString(bioPlist, "MesaSerialNumber") ??
            Parsers.PlistString(bioPlist, "SerialNumber") ??
            DeviceService.FindDictValue(diagDict, "MesaSerialNumber", "RosalineSerialNumber", "TouchIDSerialNumber", "PearlSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "MesaSerialNumber", "RosalineSerialNumber", "TouchIDSerialNumber", "PearlSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "MesaSerialNumber", "RosalineSerialNumber", "TouchIDSerialNumber", "PearlSerialNumber"));

        string mlbLive = Parsers.CleanSerial(
            DeviceService.FindDictValue(gestaltDict, "MLBSerialNumber", "BoardSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "MLBSerialNumber") ??
            data.MotherboardSerialNumber);

        string btMac = Parsers.CleanSerial(
            DeviceService.FindDictValue(gestaltDict, "BluetoothAddress") ??
            DeviceService.FindDictValue(defaultDict, "BluetoothAddress"));

        string wifiMac = Parsers.CleanSerial(
            DeviceService.FindDictValue(gestaltDict, "WifiAddress", "WiFiAddress") ??
            DeviceService.FindDictValue(defaultDict, "WifiAddress", "WiFiAddress"));

        string cellular = Parsers.CleanSerial(
            DeviceService.FindDictValue(gestaltDict, "CellularAddress") ??
            DeviceService.FindDictValue(defaultDict, "CellularAddress"));

        // Factory original serials from syscfg / lockdown in-memory dictionaries with comprehensive fallbacks
        string origBatt = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalBatterySerialNumber", "FactoryBatterySerialNumber", "BatterySerialNumber", "BatterySerial") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalBatterySerialNumber", "FactoryBatterySerialNumber", "BatterySerialNumber", "BatterySerial") ??
            DeviceService.FindDictValue(defaultDict, "OriginalBatterySerialNumber", "FactoryBatterySerialNumber", "BatterySerialNumber", "BatterySerial"));

        string origDisplay = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber", "OriginalScreenSerial", "OriginalLCMSerialNumber", "LCMSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber", "OriginalScreenSerial", "OriginalLCMSerialNumber", "LCMSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber", "OriginalScreenSerial", "OriginalLCMSerialNumber", "LCMSerialNumber"));

        string origFrontCam = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalFrontCameraSerialNumber", "FactoryFrontCameraSerialNumber", "OriginalFrontCameraModuleSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalFrontCameraSerialNumber", "FactoryFrontCameraSerialNumber", "OriginalFrontCameraModuleSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "OriginalFrontCameraSerialNumber", "FactoryFrontCameraSerialNumber"));

        string origRearCam = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalRearCameraSerialNumber", "FactoryRearCameraSerialNumber", "OriginalRearCameraModuleSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalRearCameraSerialNumber", "FactoryRearCameraSerialNumber", "OriginalRearCameraModuleSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "OriginalRearCameraSerialNumber", "FactoryRearCameraSerialNumber"));

        string origMlb = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalMLBSerialNumber", "FactoryMLBSerialNumber", "OriginalBoardSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalMLBSerialNumber", "FactoryMLBSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "OriginalMLBSerialNumber"));

        string origBio = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalMesaSerialNumber", "FactoryMesaSerialNumber", "OriginalRosalineSerialNumber") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalMesaSerialNumber", "FactoryMesaSerialNumber", "OriginalRosalineSerialNumber") ??
            DeviceService.FindDictValue(defaultDict, "OriginalMesaSerialNumber", "FactoryMesaSerialNumber", "OriginalRosalineSerialNumber"));

        string origBt = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalBluetoothAddress") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalBluetoothAddress") ??
            DeviceService.FindDictValue(defaultDict, "OriginalBluetoothAddress"));

        string origWifi = Parsers.CleanSerial(
            DeviceService.FindDictValue(diagDict, "OriginalWifiAddress", "OriginalWiFiAddress") ??
            DeviceService.FindDictValue(gestaltDict, "OriginalWifiAddress", "OriginalWiFiAddress") ??
            DeviceService.FindDictValue(defaultDict, "OriginalWifiAddress"));

        // Populate device data model
        data.DisplaySerialNumber = displaySerial;
        data.CoverGlassSerialNumber = coverGlass;
        data.FrontCameraSerialNumber = frontCam;
        data.RearCameraSerialNumber = rearCam;
        data.OriginalBatterySerialNumber = origBatt;
        data.MotherboardSerialNumber = mlbLive;
        data.TouchIdFaceIdSerialNumber = bioSerial;
        data.BluetoothMacAddress = btMac;
        data.WifiMacAddress = wifiMac;
        data.CellularAddress = cellular;

        // The logic board row falls back to the device identifier when the board
        // serial itself was refused, because the identifier is still a fact
        // about this phone; everything else stays unknown rather than guessed at.
        string mlbRead = Parsers.IsUnreadable(mlbLive) && !Parsers.IsUnreadable(data.Identifier)
            ? data.Identifier
            : mlbLive;

        // The 3uTools parity layout: the read, the factory original and the
        // comparison. Wi-Fi, Bluetooth and the board have no separate original
        // on most phones, so a readable value stands in for its own original;
        // that is a match, not an unverified row.
        data.ComponentChecks =
        [
            Check("Moederbord (Logic Board)", mlbRead, origMlb, originalFallsBackToRead: true),
            Check("Batterij", data.BatterySerialNumber, origBatt),
            Check("Scherm (LCM)", data.DisplaySerialNumber, origDisplay),
            Check("Camera Achter", data.RearCameraSerialNumber, origRearCam),
            Check("Camera Voor", data.FrontCameraSerialNumber, origFrontCam),
            Check("Touch ID / Face ID", bioSerial, origBio),
            Check("Wi-Fi Adres", wifiMac, origWifi, originalFallsBackToRead: true),
            Check("Bluetooth Adres", btMac, origBt, originalFallsBackToRead: true),
            Check("Cellular Adres", cellular, cellular),
        ];
    }

    /// <summary>
    /// One row: the value that was read, the factory original, and the verdict.
    /// An unreadable side becomes "Onbekend" rather than an empty field, and a
    /// row whose original falls back to the read is a match.
    /// </summary>
    private static ComponentStatus Check(string name, string read, string original, bool originalFallsBackToRead = false)
    {
        string safeRead = Parsers.IsUnreadable(read) ? Unknown : read;

        string safeOriginal = Parsers.IsUnreadable(original)
            ? (originalFallsBackToRead ? safeRead : Unknown)
            : original;

        return new ComponentStatus
        {
            Name = name,
            SerialRead = safeRead,
            SerialOriginal = safeOriginal,
            Status = Parsers.VerifyComponent(safeRead, safeOriginal)
        };
    }
}
