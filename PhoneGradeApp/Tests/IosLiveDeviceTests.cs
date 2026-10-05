using PhoneGrade.Core;
using PhoneGrade.Core.SecurityServices;
using Xunit;

namespace PhoneGrade.Tests;

/// <summary>
/// Runs the iPhone readers against what an actual handset answered.
///
/// The captures under <c>Tests/Fixtures/live</c> come from an iPhone 8
/// (iPhone10,1) on iOS 16.7.10. Every expectation here is read off that
/// capture, not invented: the model, the iOS version, the storage bytes, the
/// battery counters and the camera keys are the ones the phone printed.
///
/// A test in here that fails means the reader no longer understands a real
/// phone, which is the failure no amount of hand written sample output catches.
/// </summary>
public class IosLiveDeviceTests
{
    private static readonly string[] DeviceDomains =
    {
        "default", "com-apple-mobile-gestalt", "com-apple-mobile-diagnostics",
        "com-apple-disk-usage", "com-apple-purplebuddy", "com-apple-fmip",
    };

    // The captures carry substituted identifiers rather than the ones the handset
    // printed. Each keeps the shape the parser reads: fifteen digits for an IMEI,
    // seventeen characters for a board serial. A scrubbed value is still a value
    // the parser has to get right, which is the whole point of the capture.
    private const string Imei = "350000000000001";
    private const string Mlb = "F2LX9ABCDEFGH3KL9";
    private const string DeviceSerial = "F4GWW1CWJC7F";
    private const string BatterySerial = "F5D82362CHJHXY9AR";
    private const string RearCameraSerial = "F6D82332YH7HCDF2A";
    private const string FrontCameraSerial = "G8R823167NHQHC98";

    /// <summary>The raw data container the iOS reader works from, rebuilt from the capture.</summary>
    private static DeviceService.DeviceRawData RawData()
    {
        string domain(string name) => LiveFixture.Read(LiveFixture.Iphone8, name);
        string node(string name) => LiveFixture.Read(LiveFixture.Iphone8, name);

        var raw = new DeviceService.DeviceRawData
        {
            DefaultXml = domain("default"),
            GestaltXml = domain("com-apple-mobile-gestalt"),
            DiagXml = domain("com-apple-mobile-diagnostics"),
            DiskXml = domain("com-apple-disk-usage"),
            PurpleBuddyXml = domain("com-apple-purplebuddy"),
            FmipXml = domain("com-apple-fmip"),
            IORegDisplay = node("appleclcd"),
            IORegCamera = node("appleh10camin"),
            IORegBio = node("applebiometricsensor"),
            IORegBattery = node("applesmartbattery"),
        };

        raw.DefaultDict = Parsers.ParsePlistXml(raw.DefaultXml);
        raw.GestaltDict = Parsers.ParsePlistXml(raw.GestaltXml);
        raw.DiagDict = Parsers.ParsePlistXml(raw.DiagXml);
        raw.DiskDict = Parsers.ParsePlistXml(raw.DiskXml);
        raw.PurpleBuddyDict = Parsers.ParsePlistXml(raw.PurpleBuddyXml);
        raw.FmipDict = Parsers.ParsePlistXml(raw.FmipXml);
        return raw;
    }

    [Fact]
    public void TheDefaultDomainComesBackAsAPropertyList()
    {
        var defaultDict = Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "default"));

        // 90-odd keys on this handset. A count in the nineties means the whole
        // property list was read; one or two means the XML was thrown away and
        // the reader fell back to scraping text out of it.
        Assert.True(defaultDict.Count > 80,
            $"the default domain yielded {defaultDict.Count} keys, which means the XML was not read");

        Assert.Equal("iPhone10,1", defaultDict["ProductType"]);
        Assert.Equal("16.7.10", defaultDict["ProductVersion"]);
        Assert.Equal(Imei, defaultDict["InternationalMobileEquipmentIdentity"]);
        Assert.Equal(Mlb, defaultDict["MLBSerialNumber"]);
        Assert.Equal("02:00:00:00:00:02", defaultDict["BluetoothAddress"]);
        Assert.Equal("02:00:00:00:00:01", defaultDict["WiFiAddress"]);
    }

    /// <summary>
    /// iOS 16 answers the three component domains with an empty property list and
    /// a warning on stderr. That is the phone saying it will not answer, so the
    /// reader has to end up with nothing to work with. Turning the warning into a
    /// key is what makes a refused question look like an answer.
    /// </summary>
    [Theory]
    [InlineData("com-apple-mobile-gestalt")]
    [InlineData("com-apple-mobile-diagnostics")]
    [InlineData("com-apple-fmip")]
    public void ADomainThePhoneRefusesReadsAsNothing(string domain)
    {
        string raw = LiveFixture.Read(LiveFixture.Iphone8, domain);
        Assert.Contains("WARNING", raw, StringComparison.Ordinal);   // the phone really did refuse

        var parsed = Parsers.ParsePlistXml(raw);
        Assert.Empty(parsed);
    }

    [Fact]
    public void TheDiskUsageDomainIsReadForItsCapacity()
    {
        var disk = Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "com-apple-disk-usage"));

        Assert.Equal("64000000000", disk["TotalDiskCapacity"]);
        // Free space moves between captures, so only its shape is pinned down.
        Assert.True(long.Parse(disk["AmountDataAvailable"]) > 0);
    }

    [Fact]
    public void TheStorageOfThisHandsetIs64Gb()
    {
        var disk = Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "com-apple-disk-usage"));
        long bytes = long.Parse(disk["TotalDiskCapacity"]);

        Assert.Equal("64GB", Mappers.MapStorage(bytes));
    }

    [Fact]
    public void TheIdentifierIsTheImeiAndNotTheSerial()
    {
        var defaultDict = Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "default"));

        string identifier = Parsers.ParseIdentifier(
            defaultDict["InternationalMobileEquipmentIdentity"],
            defaultDict["SerialNumber"]);

        Assert.Equal(Imei, identifier);
        Assert.Equal(DeviceSerial, defaultDict["SerialNumber"]);
    }

    /// <summary>
    /// The battery node reports <c>MaxCapacity</c> as a percentage and
    /// <c>AppleRawMaxCapacity</c> as the charge the battery really holds. Reading
    /// the percentage as if it were a charge gives 6 percent health on a battery
    /// that holds 71, so the two have to stay apart.
    /// </summary>
    [Fact]
    public void TheBatteryHealthIsReadFromTheChargeAndNotFromThePercentage()
    {
        string battery = LiveFixture.Read(LiveFixture.Iphone8, "applesmartbattery");

        Assert.Equal(1279, Parsers.PlistInt(battery, "AppleRawMaxCapacity"));
        Assert.Equal(1810, Parsers.PlistInt(battery, "DesignCapacity"));
        Assert.Equal(100, Parsers.PlistInt(battery, "MaxCapacity"));

        // 1279 of 1810 is 70.7 percent, printed without decimals.
        Assert.Equal("71", Parsers.ParseBatteryHealth(battery));
    }

    [Fact]
    public void TheBatterySerialIsTheBatteryNodeAndNotWhateverComesAfterIt()
    {
        string battery = LiveFixture.Read(LiveFixture.Iphone8, "applesmartbattery");

        Assert.Equal(BatterySerial, Parsers.PlistString(battery, "Serial"));
        Assert.Equal("1780", Parsers.PlistInt(battery, "CycleCount")?.ToString());
    }

    /// <summary>
    /// A key whose value sits under a different element type must read as absent.
    /// <c>DesignCapacity</c> is an integer here, but a handset that wrote it as
    /// text would otherwise hand back the first number that appears anywhere after
    /// the key, which is a value from an unrelated part of the tree.
    /// </summary>
    [Fact]
    public void AnIntegerThatIsWrittenAsTextReadsAsAbsentRatherThanAsTheNextNumber()
    {
        string plist =
            "<plist><dict>" +
            "<key>DesignCapacity</key><string>not a number</string>" +
            "<key>SomeOtherKey</key><integer>4242</integer>" +
            "</dict></plist>";

        Assert.Null(Parsers.PlistInt(plist, "DesignCapacity"));
    }

    [Fact]
    public void AStringThatIsWrittenAsDataReadsAsAbsentRatherThanAsTheNextString()
    {
        string plist =
            "<plist><dict>" +
            "<key>CameraModuleSerial</key><data>ECkhoI8+WIE=</data>" +
            "<key>SomethingElse</key><string>not-the-serial</string>" +
            "</dict></plist>";

        Assert.Null(Parsers.PlistString(plist, "CameraModuleSerial"));
    }

    /// <summary>
    /// This handset publishes the camera serials under keys ending in
    /// <c>SerialNumString</c>. The reader looked for other spellings, so both
    /// cameras read as blank on a phone that reports both of them perfectly well.
    /// </summary>
    [Fact]
    public void BothCameraSerialsAreReadableFromThisHandset()
    {
        string camera = LiveFixture.Read(LiveFixture.Iphone8, "appleh10camin");

        Assert.Equal(RearCameraSerial, Parsers.PlistString(camera, "BackCameraModuleSerialNumString"));
        Assert.Equal(FrontCameraSerial, Parsers.PlistString(camera, "FrontCameraModuleSerialNumString"));

        // The binary <data> blobs under the same components are not serials and
        // must not be mistaken for them.
        Assert.Equal("ECkhoI8+WIE=", Parsers.PlistData(camera, "BackCameraSerialNumber"));
    }

    /// <summary>
    /// The phone reports <c>kCTSIMSupportSIMStatusNotInserted</c>. Neither of the
    /// two words the reader looks for is in that, so a handset with no SIM at all
    /// was reported as having one.
    /// </summary>
    [Fact]
    public async Task APhoneWithoutASimIsNotReportedAsHavingOne()
    {
        var defaultDict = Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "default"));

        Assert.Equal("kCTSIMSupportSIMStatusNotInserted", defaultDict["SIMStatus"]);
        Assert.Equal("kCTSIMSupportSIMTrayInsertedNoSIM", defaultDict["SIMTrayStatus"]);

        var carrier = await ActivationLockService.DetectCarrierLockAsync("captured", RawData());
        Assert.False(carrier.SIMPresent);
        Assert.Null(carrier.IsCarrierLocked);
    }

    /// <summary>
    /// None of the three Find My sources answers on this iOS version, and
    /// <c>ActivationState</c> is <c>Activated</c>. There is no evidence either way,
    /// so the answer has to stay undecided: claiming the phone is unlocked on the
    /// strength of a question nobody answered is how a locked phone gets sold.
    /// </summary>
    [Fact]
    public async Task AnUnansweredActivationLockStaysUndecided()
    {
        Assert.Equal("Activated",
            Parsers.ParsePlistXml(LiveFixture.Read(LiveFixture.Iphone8, "default"))["ActivationState"]);

        var status = await ActivationLockService.DetectAsync("captured", RawData());
        Assert.Equal(ActivationLockService.ActivationLockStatus.Unknown, status);
    }

    /// <summary>
    /// Apple serials are twelve to seventeen characters of letters and digits.
    /// Twelve is also a valid base64 length, so a reader that tries base64 on
    /// anything of that length will rewrite a real serial into something else.
    /// </summary>
    [Theory]
    [InlineData("F4GWW1CWJC7F")]        // SerialNumber of the captured handset
    [InlineData("F5D82362CHJHXY9AR")]    // battery serial of the captured handset
    [InlineData("F2LX9ABCDEFGH3KL9")]    // MLB serial of the captured handset
    [InlineData("F6D82332YH7HCDF2A")]    // rear camera serial
    public void AnAppleSerialComesBackUnchanged(string serial)
    {
        Assert.Equal(serial, Parsers.CleanSerial(serial));
    }

    /// <summary>
    /// The display node on this handset exists but carries no serial at all, and
    /// the biometric node is absent entirely. Both answers have to stay empty: a
    /// reader that walks past the end of the node and grabs the next value in the
    /// dump reports a display serial the phone never gave.
    /// </summary>
    [Fact]
    public void AnAbsentSerialIsNotReplacedByTheNextValueInTheDump()
    {
        string display = LiveFixture.Read(LiveFixture.Iphone8, "appleclcd");
        // The runner reports a node that printed nothing as NO OUTPUT.
        Assert.Equal("NO OUTPUT", LiveFixture.Read(LiveFixture.Iphone8, "applebiometricsensor"));

        Assert.Null(Parsers.PlistString(display, "DisplaySerial"));
        Assert.Null(Parsers.PlistString(display, "LCMSerialNumber"));
        Assert.Null(Parsers.PlistString(display, "SerialNumber"));

        Assert.Equal(ComponentStatusType.Unknown, Parsers.VerifyComponent("", ""));
    }
}