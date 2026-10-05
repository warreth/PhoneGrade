using System;
using System.Threading.Tasks;
using PhoneGrade.Core.Usb;
using Xunit;

namespace PhoneGrade.Tests;

// ============ Which phone is on the cable, and what it is called ============
//
// Everything the USB debugging overlay says starts here. Two rules matter more
// than the rest: an Android phone must be identified even when the vendor ID
// belongs to somebody else, and an iPad must never be identified at all. The
// second rule cannot be expressed by an allow list alone, because "Apple is not
// on the list" and "Apple is forbidden" behave the same until the day a device
// shows up whose description mentions both.

public class AndroidBrandDetectorTests
{
    [Fact]
    public void TheVendorId_WinsOverTheDescription()
    {
        // Every platform reports the vendor ID for every device, so it is the
        // one input all three agree on. A description that disagrees is simply
        // the wrong half of the record.
        var device = new UsbDeviceInfo(0x04E8, 0x6860, @"USB\VID_04E8&PID_6860", "SAMSUNG Mobile USB Serial Port");

        Assert.Equal("Samsung", AndroidBrandDetector.Detect(device));
    }

    [Fact]
    public void AnUnknownVendorId_FallsBackToTheWordsInTheDescription()
    {
        // Phone makers took vendor IDs of their own only recently. Until then,
        // and for everything they rebrand, the description is the only place the
        // brand is written out.
        var device = new UsbDeviceInfo(0x1234, 0x5678, @"USB\VID_1234&PID_5678", "HONOR 600 Lite (LNA-NX1)");

        Assert.Equal("Honor", AndroidBrandDetector.Detect(device));
    }

    [Fact]
    public async Task AnIPad_IsNeverAPhone_AndNeverEvenReachesAdb()
    {
        // The one device this overlay must not open for. Apple hands the same
        // vendor ID to every iPhone, iPad and iPod, so the ID alone rules it out
        // without needing to know which of the three it is.
        var ipad = new UsbDeviceInfo(0x05AC, 0x12A8, @"USB\VID_05AC&PID_12A8", "Apple iPad");

        Assert.Equal("", AndroidBrandDetector.Detect(ipad));
    }

    [Fact]
    public void AnAppleVendorId_IsRejected_EvenWhenTheDescriptionNamesAnAndroidBrand()
    {
        // A card reader that reuses a phone's VID, or a dock that reports both.
        // If the description were consulted first this would come back as a
        // phone, which is exactly the mix-up the deny list exists to prevent.
        var suspicious = new UsbDeviceInfo(0x05AC, 0x8600, @"USB\VID_05AC&PID_8600", "SAMSUNG Mobile USB Serial Port");

        Assert.Equal("", AndroidBrandDetector.Detect(suspicious));
    }

    [Fact]
    public void AnAppleDescription_IsRejected_EvenWhenTheVendorIdIsUnknown()
    {
        var ipad = new UsbDeviceInfo(0x1234, 0x5678, @"USB\VID_1234&PID_5678", "iPad13,18");

        Assert.Equal("", AndroidBrandDetector.Detect(ipad));
    }

    [Fact]
    public void AnAppleWordAnywhereInTheDescription_RejectsTheWholeDevice()
    {
        // "iPad" beside a word from the brand table is the precise collision
        // this guard exists for: substring matching would call it a phone.
        Assert.Equal("", AndroidBrandDetector.FromDescription("samsung ipad adapter"));
        Assert.Equal("", AndroidBrandDetector.FromDescription("Apple iPhone"));
        Assert.Equal("", AndroidBrandDetector.FromDescription("iPod"));
    }

    [Fact]
    public void APeripheralNobodyAskedAbout_ComesBackEmpty()
    {
        // Answering nothing is the point. A keyboard, a dongle or a device
        // nobody has catalogued must not be handed to a guide about phones.
        var keyboard = new UsbDeviceInfo(0x046D, 0xC31C, @"USB\VID_046D&PID_C31C", "Logitech USB Keyboard");

        Assert.Equal("", AndroidBrandDetector.Detect(keyboard));
        Assert.Equal("", AndroidBrandDetector.Detect(new UsbDeviceInfo(0x1234, 0x5678, "x", "")));
        Assert.Equal("", AndroidBrandDetector.FromDescription(""));
        Assert.Equal("", AndroidBrandDetector.FromDescription(null));
        Assert.Equal("", AndroidBrandDetector.FromDescription("   "));
    }

    [Fact]
    public void TheVendorWhoOnlyRecentlyTookAnIdOfTheirOwn_IsRecognised()
    {
        // Honor split from Huawei and took a vendor ID of its own. Without it
        // the vendor ID lookup returns nothing and the overlay falls back to
        // generic steps for a phone that has its own menus.
        Assert.Equal("Honor", AndroidBrandDetector.Detect(new UsbDeviceInfo(0x339B, 0x0001, "x", "")));
        Assert.Equal("ZTE", AndroidBrandDetector.Detect(new UsbDeviceInfo(0x19D2, 0x0001, "x", "")));
        Assert.Equal("Meizu", AndroidBrandDetector.Detect(new UsbDeviceInfo(0x2A45, 0x0001, "x", "")));
        Assert.Equal("Hisense", AndroidBrandDetector.Detect(new UsbDeviceInfo(0x109B, 0x0001, "x", "")));
    }

    [Fact]
    public void TheDenyListAndTheAllowList_DoNotOverlap()
    {
        // A vendor in both would be a rule that reads one way in the source and
        // another at runtime, because the deny list is consulted first.
        foreach (ushort vendorId in VendorIdDictionary.NonAndroidVendors)
        {
            Assert.False(VendorIdDictionary.IsAndroidVendor(vendorId),
                $"0x{vendorId:X4} is listed as both an Android vendor and not one");
        }

        Assert.True(VendorIdDictionary.IsKnownNonAndroidVendor(0x05AC));
        Assert.False(VendorIdDictionary.IsKnownNonAndroidVendor(0x04E8));
    }
}

// ============ What the OS called the phone ============

public class AndroidDeviceNameTests
{
    [Fact]
    public void TheFactoryModelCode_IsNotTheName()
    {
        // "HONOR 600 Lite (LNA-NX1)" is what Windows puts on the portable device
        // node. The bracketed part is how the factory tracks it and means nothing
        // to anyone holding the phone.
        Assert.Equal("HONOR 600 Lite", AndroidDeviceName.FromReportedText("HONOR 600 Lite (LNA-NX1)"));
    }

    [Fact]
    public void TheBrandStaysInTheName()
    {
        // The brand appears nowhere else on the overlay: the heading is built
        // from this string alone. Dropping it turns "Voor uw Xiaomi 13:" into
        // "Voor uw 13:", which names a fragment of a phone.
        Assert.Equal("Xiaomi 13", AndroidDeviceName.FromReportedText("Xiaomi 13"));
        Assert.Equal("HONOR 600 Lite", AndroidDeviceName.FromReportedText("HONOR 600 Lite"));
    }

    [Fact]
    public void UnderscoresFromUdev_BecomeSpaces()
    {
        // udev writes spaces in a model name as underscores, and an overlay that
        // greets an operator with "Redmi_Note_12" is showing them a device file.
        Assert.Equal("Redmi Note 12", AndroidDeviceName.FromReportedText("Redmi_Note_12"));
    }

    [Fact]
    public void ADescriptionOfHardware_IsNotAModel()
    {
        // The interface that raises the USB event is called "USB Composite
        // Device" whatever is plugged in. Headed by that, the overlay tells an
        // operator nothing about which phone is on the cable, so the caller is
        // better off falling back to the brand.
        Assert.Equal("", AndroidDeviceName.FromReportedText("USB Composite Device"));
        Assert.Equal("", AndroidDeviceName.FromReportedText("SAMSUNG Mobile USB Serial Port"));
        Assert.Equal("", AndroidDeviceName.FromReportedText("Android Composite ADB Interface"));
        Assert.Equal("", AndroidDeviceName.FromReportedText("Unknown"));
        Assert.Equal("", AndroidDeviceName.FromReportedText("Composite"));
    }

    [Fact]
    public void NothingReported_IsNotAModel()
    {
        Assert.Equal("", AndroidDeviceName.FromReportedText(null));
        Assert.Equal("", AndroidDeviceName.FromReportedText(""));
        Assert.Equal("", AndroidDeviceName.FromReportedText("   "));
    }

    [Fact]
    public void ASingleStrayCharacter_IsNotAModel()
    {
        // A name of one character is punctuation left over after the brackets
        // and the brand were removed, not something to put in a heading.
        Assert.Equal("", AndroidDeviceName.FromReportedText(")"));
        Assert.Equal("", AndroidDeviceName.FromReportedText("-"));
    }

    [Fact]
    public void PunctuationLeftAroundTheName_IsTrimmed()
    {
        // The bracket replacement and the surrounding punctuation can leave a
        // name dangling on a dash, and "Voor uw -:" is what that would read as.
        Assert.Equal("Pixel 7", AndroidDeviceName.FromReportedText("- Pixel 7 -"));
        Assert.Equal("Galaxy S21", AndroidDeviceName.FromReportedText("Galaxy S21,"));

        // The brackets are what marks it as a factory code rather than a name,
        // and with nothing outside them there is no name left: the caller falls
        // back to the brand instead of heading the overlay with a code.
        Assert.Equal("", AndroidDeviceName.FromReportedText("(LNA-NX1)"));
    }
}

// ============ Deciding when the operator needs the how-to ============

public class AdbDeviceDirectorTests
{
    private const ushort SamsungVid = 0x04E8;
    private const ushort AppleVid = 0x05AC;

    [Fact]
    public async Task AnUnauthorizedAndroidPhone_AsksForAuthorization()
    {
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, AnyAuthorized: false, AnyUnauthorized: true));
        using var director = NewDirector(monitor, adb, reported: "SAMSUNG Galaxy S21");

        AdbRequiredEventArgs raised = await RaiseFor(director, monitor, Android(SamsungVid, "usb1"));

        Assert.Equal("Samsung", raised.Manufacturer);
        Assert.Equal("SAMSUNG Galaxy S21", raised.ModelName);
        Assert.Equal(SamsungVid, raised.DeviceInfo.VendorId);
    }

    [Fact]
    public async Task TheModelIsReadFromTheNativePort_NotInvented()
    {
        // The whole point of reading the port: what the OS has to say is what
        // goes on screen, brackets and underscores and all.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = NewDirector(monitor, adb, reported: "HONOR 600 Lite (LNA-NX1)");

        AdbRequiredEventArgs raised = await RaiseFor(director, monitor, Android(0x339B, "usb1"));

        Assert.Equal("Honor", raised.Manufacturer);
        Assert.Equal("HONOR 600 Lite", raised.ModelName);
    }

    [Fact]
    public async Task WhenTheOsOnlyDescribesTheHardware_NoModelIsGuessedAt()
    {
        // An empty model is a real answer: it is what tells the overlay to
        // address the phone by its brand instead of printing the interface name.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = NewDirector(monitor, adb, reported: "USB Composite Device");

        AdbRequiredEventArgs raised = await RaiseFor(director, monitor, Android(SamsungVid, "usb1"));

        Assert.Equal("Samsung", raised.Manufacturer);
        Assert.Equal("", raised.ModelName);
    }

    [Fact]
    public void TheModelIsReadAgain_WhileTheOsIsStillPublishingIt()
    {
        // Windows fills in the portable device node from the phone a beat after
        // the USB interface that identifies it, so the read that happens to run
        // first answers with nothing. One read means a heading that stays on the
        // brand for the rest of the session over a phone whose model is on the
        // screen the OS shows for it.
        int reads = 0;
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = new AdbDeviceDirector(
            monitor, adb,
            deviceName: _ => ++reads < 3 ? "" : "HONOR 600 Lite (LNA-NX1)",
            enumerationDelay: TimeSpan.Zero,
            connectedDevices: () => new[] { Android(0x339B, "usb1") },
            nameAttempts: 4,
            nameRetryDelay: TimeSpan.Zero);

        var (brand, model) = director.IdentifyConnectedDevice();

        Assert.Equal("Honor", brand);
        Assert.Equal("HONOR 600 Lite", model);
        Assert.True(reads >= 3, $"the phone was only asked {reads} time(s)");
    }

    [Fact]
    public void ANameThatIsNotAModel_IsReadOnceAndBelieved()
    {
        // The other half of the same rule. "USB Composite Device" is the OS
        // having spoken and not named the phone, which is a finished answer;
        // waiting on it would hold the how-to open for a phone that will never
        // be called anything but hardware.
        int reads = 0;
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = new AdbDeviceDirector(
            monitor, adb,
            deviceName: _ => { reads++; return "USB Composite Device"; },
            enumerationDelay: TimeSpan.Zero,
            connectedDevices: () => new[] { Android(SamsungVid, "usb1") },
            nameAttempts: 4,
            nameRetryDelay: TimeSpan.Zero);

        var (brand, model) = director.IdentifyConnectedDevice();

        Assert.Equal("Samsung", brand);
        Assert.Equal("", model);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AnIPad_NeverAsks_AndNeverEvenReachesAdb()
    {
        // Two claims in one test. The overlay is not only silent for an iPad,
        // it never got as far as asking adb, so no amount of adb output about a
        // nearby phone can be blamed for keeping it quiet.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, AnyAuthorized: false, AnyUnauthorized: true));
        var raised = new TaskCompletionSource<AdbRequiredEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var director = NewDirector(monitor, adb);

        director.AdbRequired += (_, e) => raised.TrySetResult(e);

        monitor.Connect(new UsbDeviceInfo(AppleVid, 0x12A8, @"USB\VID_05AC&PID_12A8", "Apple iPad"));
        await Task.Delay(300);

        Assert.False(raised.Task.IsCompleted, "the overlay opened for an iPad");
        Assert.Equal(0, adb.ReadCount);
    }

    [Fact]
    public async Task APeripheralNobodyAskedAbout_NeverAsks()
    {
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        var raised = new TaskCompletionSource<AdbRequiredEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var director = NewDirector(monitor, adb);

        director.AdbRequired += (_, e) => raised.TrySetResult(e);

        monitor.Connect(new UsbDeviceInfo(0x046D, 0xC31C, @"USB\VID_046D&PID_C31C", "Logitech USB Keyboard"));
        await Task.Delay(300);

        Assert.False(raised.Task.IsCompleted, "the overlay opened for a keyboard");
        Assert.Equal(0, adb.ReadCount);
    }

    [Fact]
    public async Task APhoneAdbAlreadyTrusts_DoesNotAsk()
    {
        // adb can talk to a phone already, so there is nothing to explain. This
        // is the state the overlay closes on rather than opens in.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, AnyAuthorized: true, AnyUnauthorized: false));
        using var director = NewDirector(monitor, adb);

        var raised = new TaskCompletionSource<AdbRequiredEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        director.AdbRequired += (_, e) => raised.TrySetResult(e);

        monitor.Connect(Android(SamsungVid, "usb1"));
        await Task.Delay(300);

        Assert.False(raised.Task.IsCompleted);
        Assert.Equal(1, adb.ReadCount);
    }

    [Fact]
    public async Task UnpluggingThePhone_ClosesTheGuideItOpened()
    {
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = NewDirector(monitor, adb);

        var cleared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        director.AdbCleared += (_, _) => cleared.TrySetResult(true);

        UsbDeviceInfo phone = Android(SamsungVid, "usb1");
        await RaiseFor(director, monitor, phone);

        monitor.Disconnect(phone);
        Assert.True(await Within(cleared.Task), "the overlay stayed up for a phone that was unplugged");
    }

    [Fact]
    public async Task UnpluggingADeviceThatNeverAsked_ClearsNothing()
    {
        // Otherwise a keyboard being unplugged would close the how-to for the
        // phone still sitting on the bench.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = NewDirector(monitor, adb);

        var cleared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        director.AdbCleared += (_, _) => cleared.TrySetResult(true);

        UsbDeviceInfo keyboard = new(0x046D, 0xC31C, @"USB\VID_046D&PID_C31C", "Logitech USB Keyboard");
        monitor.Connect(keyboard);
        await Task.Delay(300);
        monitor.Disconnect(keyboard);
        await Task.Delay(200);

        Assert.False(cleared.Task.IsCompleted);
    }

    [Fact]
    public async Task TheSameDeviceConnectedTwice_AsksOnlyOnce()
    {
        // Windows reports the composite device and each of its interfaces, and a
        // monitor that replays an event would put the overlay up again while the
        // operator is reading it.
        var monitor = new FakeMonitor();
        var adb = new FakeAdbList(new AdbDeviceListState(Ran: true, false, true));
        using var director = NewDirector(monitor, adb);

        int count = 0;
        var raised = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        director.AdbRequired += (_, _) =>
        {
            System.Threading.Interlocked.Increment(ref count);
            raised.TrySetResult(true);
        };

        UsbDeviceInfo phone = Android(SamsungVid, "usb1");
        monitor.Connect(phone);

        // Wait for the first request rather than for a fixed span. Naming the phone
        // reads the PnP node on Windows, which takes longer than any constant here
        // can promise on a busy machine.
        Assert.True(await Within(raised.Task), "the director stayed silent for an Android phone");

        monitor.Connect(phone);

        // Give the replayed event the same room the first one was given. A second
        // request that arrives is the failure, not a slow first one.
        await Task.Delay(500);
        Assert.Equal(1, count);
    }

    // ---- helpers ----

    private static AdbDeviceDirector NewDirector(
        FakeMonitor monitor,
        FakeAdbList adb,
        string? reported = null)
        => new(monitor, adb, reported is null ? null : _ => reported, TimeSpan.Zero);

    private static UsbDeviceInfo Android(ushort vendorId, string instance)
        => new(vendorId, 0x6860, instance, "Android Composite ADB Interface");

    /// <summary>Connects a device and waits for the director to answer.</summary>
    private static async Task<AdbRequiredEventArgs> RaiseFor(
        AdbDeviceDirector director, FakeMonitor monitor, UsbDeviceInfo device)
    {
        var raised = new TaskCompletionSource<AdbRequiredEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        director.AdbRequired += (_, e) => raised.TrySetResult(e);

        monitor.Connect(device);

        Assert.True(await Within(raised.Task), "the director stayed silent for an Android phone");
        return await raised.Task;
    }

    private static async Task<bool> Within(Task task)
    {
        Task done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        return ReferenceEquals(done, task);
    }

    /// <summary>A USB event stream that only does what the test tells it to.</summary>
    private sealed class FakeMonitor : IUsbEventMonitor
    {
        public event EventHandler<UsbDeviceInfo>? DeviceConnected;
        public event EventHandler<UsbDeviceInfo>? DeviceDisconnected;

        public bool IsMonitoring { get; private set; }
        public void Start() => IsMonitoring = true;
        public void Stop() => IsMonitoring = false;
        public void Dispose() { }

        public void Connect(UsbDeviceInfo device) => DeviceConnected?.Invoke(this, device);
        public void Disconnect(UsbDeviceInfo device) => DeviceDisconnected?.Invoke(this, device);
    }

    /// <summary>The adb answer, handed in rather than looked up.</summary>
    private sealed class FakeAdbList : IAdbDeviceList
    {
        private readonly AdbDeviceListState _state;

        public FakeAdbList(AdbDeviceListState state) => _state = state;

        public int ReadCount { get; private set; }

        public Task<AdbDeviceListState> ReadAsync()
        {
            ReadCount++;
            return Task.FromResult(_state);
        }
    }
}
