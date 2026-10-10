using System.Linq;
using PhoneGrade.Core;
using PhoneGrade.Core.Apple;
using Xunit;

namespace PhoneGrade.Tests;

// ============ The nine component rows on an iPhone ============
//
// The rows are the 3uTools parity layout: what was read, what the factory
// recorded, and the verdict. The fallback chains that find each value are the
// part that used to be nine near-identical blocks, so these tests pin the
// outcomes and a table edit cannot quietly change a verdict.

public class AppleComponentSerialsTests
{
    private static DeviceData Phone() => new()
    {
        Identifier = "356938035643809",
        BatterySerialNumber = "BATT-READ",
    };

    [Fact]
    public void APhoneThatReportsEverything_GetsNineMatchingRows()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.DiagDict["LCMSerialNumber"] = "DISP-READ";
        raw.DiagDict["CoverGlassSerialNumber"] = "GLASS-READ";
        raw.DiagDict["FrontCameraModuleSerialNumString"] = "FRONT-READ";
        raw.DiagDict["BackCameraModuleSerialNumString"] = "REAR-READ";
        raw.DiagDict["MesaSerialNumber"] = "BIO-READ";
        raw.GestaltDict["MLBSerialNumber"] = "MLB-READ";
        raw.GestaltDict["WifiAddress"] = "AA:BB:CC:DD:EE:01";
        raw.GestaltDict["BluetoothAddress"] = "AA:BB:CC:DD:EE:02";
        raw.GestaltDict["CellularAddress"] = "AA:BB:CC:DD:EE:03";

        raw.DiagDict["OriginalBatterySerialNumber"] = "BATT-READ";
        raw.DiagDict["OriginalDisplaySerialNumber"] = "DISP-READ";
        raw.DiagDict["OriginalFrontCameraSerialNumber"] = "FRONT-READ";
        raw.DiagDict["OriginalRearCameraSerialNumber"] = "REAR-READ";
        raw.DiagDict["OriginalMesaSerialNumber"] = "BIO-READ";
        raw.DiagDict["OriginalMLBSerialNumber"] = "MLB-READ";
        raw.DiagDict["OriginalBluetoothAddress"] = "AA:BB:CC:DD:EE:02";
        raw.DiagDict["OriginalWifiAddress"] = "AA:BB:CC:DD:EE:01";

        var data = Phone();
        AppleComponentSerials.Populate(raw, data);

        Assert.Equal("DISP-READ", data.DisplaySerialNumber);
        Assert.Equal("GLASS-READ", data.CoverGlassSerialNumber);
        Assert.Equal("FRONT-READ", data.FrontCameraSerialNumber);
        Assert.Equal("REAR-READ", data.RearCameraSerialNumber);
        Assert.Equal("BIO-READ", data.TouchIdFaceIdSerialNumber);
        Assert.Equal("MLB-READ", data.MotherboardSerialNumber);
        Assert.Equal("AA:BB:CC:DD:EE:01", data.WifiMacAddress);
        Assert.Equal("AA:BB:CC:DD:EE:02", data.BluetoothMacAddress);
        Assert.Equal("AA:BB:CC:DD:EE:03", data.CellularAddress);

        Assert.Equal(
            ["Moederbord (Logic Board)", "Batterij", "Scherm (LCM)", "Camera Achter", "Camera Voor", "Touch ID / Face ID", "Wi-Fi Adres", "Bluetooth Adres", "Cellular Adres"],
            data.ComponentChecks.Select(check => check.Name));

        Assert.All(data.ComponentChecks, check => Assert.Equal(ComponentStatusType.Match, check.Status));
    }

    [Fact]
    public void OriginalsThePhoneDidNotPublish_AreUnknown_ExceptWhereTheReadStandsIn()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.IORegDisplay = "<key>DisplaySerial</key><string>DISP-READ</string>";
        raw.DiagDict["FrontCameraModuleSerialNumString"] = "FRONT-READ";
        raw.DiagDict["BackCameraModuleSerialNumString"] = "REAR-READ";
        raw.DiagDict["MesaSerialNumber"] = "BIO-READ";
        raw.GestaltDict["MLBSerialNumber"] = "MLB-READ";
        raw.GestaltDict["WifiAddress"] = "AA:BB:CC:DD:EE:01";
        raw.GestaltDict["BluetoothAddress"] = "AA:BB:CC:DD:EE:02";
        raw.GestaltDict["CellularAddress"] = "AA:BB:CC:DD:EE:03";

        var data = Phone();
        AppleComponentSerials.Populate(raw, data);

        var byName = data.ComponentChecks.ToDictionary(check => check.Name);

        // The board, the two addresses and cellular have no separate original on
        // most phones, so the read stands in for it and the row is a match.
        Assert.Equal(ComponentStatusType.Match, byName["Moederbord (Logic Board)"].Status);
        Assert.Equal(ComponentStatusType.Match, byName["Wi-Fi Adres"].Status);
        Assert.Equal(ComponentStatusType.Match, byName["Bluetooth Adres"].Status);
        Assert.Equal(ComponentStatusType.Match, byName["Cellular Adres"].Status);

        // The parts with a factory record of their own stay unknown, never a match.
        foreach (string name in new[] { "Batterij", "Scherm (LCM)", "Camera Achter", "Camera Voor", "Touch ID / Face ID" })
        {
            Assert.Equal(ComponentStatusType.Unknown, byName[name].Status);
            Assert.Equal("Onbekend", byName[name].SerialOriginal);
        }
    }

    [Fact]
    public void ALogicBoardSerialThatWasRefused_FallsBackToTheIdentifier()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.DiagDict["LCMSerialNumber"] = "ERROR: no output";

        var data = Phone();
        AppleComponentSerials.Populate(raw, data);

        var board = data.ComponentChecks[0];
        Assert.Equal("356938035643809", board.SerialRead);
        Assert.Equal(ComponentStatusType.Match, board.Status);
    }

    [Fact]
    public void AnUnreadableRead_IsReportedAsUnknown()
    {
        var raw = new DeviceService.DeviceRawData();
        raw.DiagDict["LCMSerialNumber"] = "ERROR: no output";
        raw.DiagDict["OriginalDisplaySerialNumber"] = "DISP-ORIG";

        var data = Phone();
        AppleComponentSerials.Populate(raw, data);

        var screen = data.ComponentChecks.First(check => check.Name == "Scherm (LCM)");
        Assert.Equal("Onbekend", screen.SerialRead);
        Assert.Equal("DISP-ORIG", screen.SerialOriginal);
        Assert.Equal(ComponentStatusType.Unknown, screen.Status);
    }
}