using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Event arguments for when ADB authorization is required.
/// </summary>
public sealed class AdbRequiredEventArgs : EventArgs
{
    /// <summary>The manufacturer name (e.g., "Samsung", "Honor"), never empty.</summary>
    public string Manufacturer { get; }

    /// <summary>
    /// The model as the operating system reported it for this device, or an
    /// empty string when the OS only described the hardware. Never guessed at:
    /// an empty value is what tells the guide to fall back to the brand.
    /// </summary>
    public string ModelName { get; }

    /// <summary>The USB device that triggered this event.</summary>
    public UsbDeviceInfo DeviceInfo { get; }

    public AdbRequiredEventArgs(string manufacturer, string modelName, UsbDeviceInfo deviceInfo)
    {
        Manufacturer = manufacturer;
        ModelName = modelName;
        DeviceInfo = deviceInfo;
    }
}

/// <summary>
/// Watches the USB event stream for phones and decides when the operator needs
/// the how-to: an Android device is on the cable and adb is not talking to it
/// yet.
///
/// The gate is the whole point of the class. An iPad on the same cable reports
/// an Apple vendor ID and an Apple description, gets no brand out of
/// <see cref="AndroidBrandDetector"/>, and never reaches adb at all - so
/// <see cref="AdbRequired"/> cannot fire for it, whatever adb happens to say.
/// </summary>
public sealed class AdbDeviceDirector : IDisposable
{
    private readonly IUsbEventMonitor _monitor;
    private readonly IAdbDeviceList _devices;
    private readonly Func<UsbDeviceInfo, string> _deviceName;
    private readonly Func<IEnumerable<UsbDeviceInfo>> _connectedDevices;
    private readonly TimeSpan _enumerationDelay;
    private readonly TimeSpan _nameRetryDelay;
    private readonly int _nameAttempts;
    private readonly object _lock = new();
    private readonly HashSet<string> _seenDevices = new();
    private readonly HashSet<string> _raisedDevices = new();
    private bool _disposed;

    /// <summary>
    /// Raised when an Android device is connected via USB but not authorized in ADB.
    /// </summary>
    public event EventHandler<AdbRequiredEventArgs>? AdbRequired;

    /// <summary>
    /// Raised when a device this class asked about is unplugged, so the guide it
    /// opened has a reason to close again.
    /// </summary>
    public event EventHandler? AdbCleared;

    public AdbDeviceDirector()
        : this(UsbMonitorFactory.Instance, new AdbDeviceList(), null, TimeSpan.FromSeconds(1)) { }

    /// <summary>
    /// Test seam: the phone, the adb answer, the OS-reported name and the list
    /// of connected devices are all handed in, so the routing and the naming
    /// can both be proven without hardware.
    ///
    /// The naming budget is handed in too. <paramref name="nameAttempts"/> reads
    /// are made when the OS answers with nothing, <paramref name="nameRetryDelay"/>
    /// apart, which in production is the beat Windows takes to publish the
    /// portable device node for a phone that has just arrived.
    /// </summary>
    public AdbDeviceDirector(
        IUsbEventMonitor monitor,
        IAdbDeviceList devices,
        Func<UsbDeviceInfo, string>? deviceName = null,
        TimeSpan? enumerationDelay = null,
        Func<IEnumerable<UsbDeviceInfo>>? connectedDevices = null,
        int nameAttempts = 3,
        TimeSpan? nameRetryDelay = null)
    {
        _monitor = monitor;
        _devices = devices;
        _deviceName = deviceName ?? ReadNameFromOs;
        _connectedDevices = connectedDevices ?? EnumerateConnectedDevices;
        _enumerationDelay = enumerationDelay ?? TimeSpan.FromSeconds(1);
        _nameAttempts = nameAttempts < 1 ? 1 : nameAttempts;
        _nameRetryDelay = nameRetryDelay ?? TimeSpan.FromMilliseconds(400);
        _monitor.DeviceConnected += OnDeviceConnected;
        _monitor.DeviceDisconnected += OnDeviceDisconnected;
    }

    /// <summary>
    /// Starts monitoring for Android device connections.
    /// </summary>
    public void Start()
    {
        if (!_monitor.IsMonitoring)
        {
            _monitor.Start();
        }
    }

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    public void Stop()
    {
        if (_monitor.IsMonitoring)
        {
            _monitor.Stop();
        }
    }

    /// <summary>True when adb lists at least one phone it can talk to.</summary>
    public Task<bool> IsAuthorizedAsync() => HasAuthorizedDevice(_devices.ReadAsync());

    private static async Task<bool> HasAuthorizedDevice(Task<AdbDeviceListState> read)
    {
        AdbDeviceListState state = await read;
        return state.Ran && state.AnyAuthorized;
    }

    /// <summary>
    /// The Android phone already on the cable, if there is one.
    ///
    /// A kiosk usually has the phone plugged in before this process starts, and
    /// a device that was there first never raises a connection event. Without
    /// this the guide would open for such a phone with nothing but the generic
    /// fallback to go on, which is the case where naming the phone matters
    /// most. Returns an empty manufacturer when nothing recognisable is there.
    /// </summary>
    public (string Manufacturer, string Model) IdentifyConnectedDevice()
    {
        try
        {
            foreach (UsbDeviceInfo device in _connectedDevices())
            {
                string manufacturer = AndroidBrandDetector.Detect(device);
                if (manufacturer.Length == 0) continue;

                return (manufacturer, ReadModelWhenPublished(device));
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.UsbDetector, $"Connected device lookup failed: {ex.Message}");
        }

        return ("", "");
    }

    /// <summary>
    /// The model of the phone on this cable, reading again for a beat before
    /// giving up on it.
    ///
    /// Windows fills in the portable device node from the phone a moment after
    /// the USB interface that identifies it, so the read that happens to run
    /// first comes back with nothing at all. An answer that is present but is
    /// not a model ("USB Composite Device") is believed straight away: the OS
    /// has spoken and did not name the phone, and waiting would hold up the
    /// how-to for a name that is not coming.
    /// </summary>
    private string ReadModelWhenPublished(UsbDeviceInfo device)
    {
        string reported = "";

        for (int attempt = 0; attempt < _nameAttempts; attempt++)
        {
            reported = _deviceName(device);
            if (reported.Length > 0) break;

            if (attempt + 1 < _nameAttempts && _nameRetryDelay > TimeSpan.Zero)
                Thread.Sleep(_nameRetryDelay);
        }

        return AndroidDeviceName.FromReportedText(reported);
    }

    private static IEnumerable<UsbDeviceInfo> EnumerateConnectedDevices()
    {
        if (OperatingSystem.IsWindows()) return WmiUsbMonitor.ListConnectedDevices();
        if (OperatingSystem.IsLinux()) return ListLinuxDevices();
        return Array.Empty<UsbDeviceInfo>();
    }

    /// <summary>
    /// The USB devices sysfs currently exposes on Linux. Only real device
    /// nodes are read: "1-2:1.0" is an interface beneath a device and "usb1" is
    /// a root hub, and neither says anything about a phone.
    /// </summary>
    private static IEnumerable<UsbDeviceInfo> ListLinuxDevices()
    {
        const string root = "/sys/bus/usb/devices";
        if (!Directory.Exists(root)) return Array.Empty<UsbDeviceInfo>();

        var devices = new List<UsbDeviceInfo>();

        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(directory);
            if (name.Contains(':') || name.StartsWith("usb", StringComparison.OrdinalIgnoreCase)) continue;

            string? vendor = ReadSysfs(directory, "idVendor");
            string? product = ReadSysfs(directory, "idProduct");
            if (vendor is null || product is null) continue;
            if (!ushort.TryParse(vendor, System.Globalization.NumberStyles.HexNumber, null, out ushort vendorId)) continue;
            if (!ushort.TryParse(product, System.Globalization.NumberStyles.HexNumber, null, out ushort productId)) continue;

            devices.Add(new UsbDeviceInfo(vendorId, productId, directory, ReadSysfs(directory, "product") ?? ""));
        }

        return devices;
    }

    private static string? ReadSysfs(string directory, string file)
    {
        try
        {
            string path = Path.Combine(directory, file);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnDeviceConnected(object? sender, UsbDeviceInfo deviceInfo)
    {
        if (_disposed) return;

        // The brand is the gate: an Apple device, or one nothing recognises,
        // stops here and never reaches adb or the guide.
        string manufacturer = AndroidBrandDetector.Detect(deviceInfo);
        if (manufacturer.Length == 0) return;

        // Avoid duplicate processing for the same device instance
        lock (_lock)
        {
            if (!_seenDevices.Add(deviceInfo.DeviceInstanceId)) return;
        }

        // Check ADB state asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                // Give adb time to enumerate the new device before being asked.
                if (_enumerationDelay > TimeSpan.Zero) await Task.Delay(_enumerationDelay);
                await CheckAdbAuthorizationAsync(manufacturer, deviceInfo);
            }
            catch (Exception ex)
            {
                SystemEventLogger.Debug(LogSource.UsbDetector, $"ADB check failed for {deviceInfo}: {ex.Message}");
            }
        });
    }

    private void OnDeviceDisconnected(object? sender, UsbDeviceInfo deviceInfo)
    {
        if (_disposed) return;

        bool wasRaised;
        lock (_lock)
        {
            _seenDevices.Remove(deviceInfo.DeviceInstanceId);
            wasRaised = _raisedDevices.Remove(deviceInfo.DeviceInstanceId);
        }

        if (wasRaised) AdbCleared?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckAdbAuthorizationAsync(string manufacturer, UsbDeviceInfo deviceInfo)
    {
        if (_disposed) return;

        try
        {
            AdbDeviceListState state = await _devices.ReadAsync();

            // Something is already talking to a phone, so there is nothing to
            // explain. An unauthorized entry keeps it open: that is the state
            // the guide exists for.
            if (state.Ran && state.AnyAuthorized && !state.AnyUnauthorized) return;

            RaiseAdbRequired(manufacturer, deviceInfo);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.UsbDetector, $"ADB authorization check error: {ex.Message}");
            RaiseAdbRequired(manufacturer, deviceInfo);
        }
    }

    private void RaiseAdbRequired(string manufacturer, UsbDeviceInfo deviceInfo)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _raisedDevices.Add(deviceInfo.DeviceInstanceId);
        }

        string model = ReadModelWhenPublished(deviceInfo);

        AdbRequired?.Invoke(this, new AdbRequiredEventArgs(manufacturer, model, deviceInfo));
    }

    /// <summary>
    /// The name the OS has for the device. Windows hides the model behind the
    /// interface that raised the event, so it is read back from the PnP node
    /// that carries the portable device name; the other platforms put it in the
    /// description already.
    /// </summary>
    private static string ReadNameFromOs(UsbDeviceInfo device)
    {
        if (OperatingSystem.IsWindows())
        {
            string fromPnp = WmiUsbMonitor.ReadDeviceName(device.VendorId);
            if (fromPnp.Length > 0) return fromPnp;
        }

        return device.Description;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitor.DeviceConnected -= OnDeviceConnected;
        _monitor.DeviceDisconnected -= OnDeviceDisconnected;
    }
}
