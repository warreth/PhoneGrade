using System;
using System.Management;
using System.Runtime.Versioning;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Windows USB event monitor using WMI (Win32_PnPEntity).
/// Subscribes to __InstanceCreationEvent and __InstanceDeletionEvent for USB devices.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiUsbMonitor : IUsbEventMonitor
{
    public event EventHandler<UsbDeviceInfo>? DeviceConnected;
    public event EventHandler<UsbDeviceInfo>? DeviceDisconnected;

    public bool IsMonitoring { get; private set; }

    private ManagementEventWatcher? _creationWatcher;
    private ManagementEventWatcher? _deletionWatcher;

    public void Start()
    {
        if (IsMonitoring) return;

        if (!OperatingSystem.IsWindows())
        {
            SystemEventLogger.Info(LogSource.System, "WmiUsbMonitor: Not on Windows, monitoring not started.");
            return;
        }

        Stop(); // Clean up any previous attempt

        try
        {
            // Query for USB device creation events using Win32_PnPEntity with USB\ in DeviceID
            var creationQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.DeviceID LIKE 'USB\\\\%'");
            _creationWatcher = new ManagementEventWatcher(creationQuery);
            _creationWatcher.EventArrived += OnDeviceConnected;
            _creationWatcher.Start();

            var deletionQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.DeviceID LIKE 'USB\\\\%'");
            _deletionWatcher = new ManagementEventWatcher(deletionQuery);
            _deletionWatcher.EventArrived += OnDeviceDisconnected;
            _deletionWatcher.Start();

            IsMonitoring = true;
            SystemEventLogger.Info(LogSource.System, "Windows USB event monitoring started via WMI (Win32_PnPEntity).");
        }
        catch (Exception ex)
        {
            Stop();
            IsMonitoring = false;
            SystemEventLogger.Error(LogSource.System, $"Failed to start WMI USB event monitoring: {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            if (_creationWatcher != null)
            {
                _creationWatcher.Stop();
                _creationWatcher.EventArrived -= OnDeviceConnected;
                _creationWatcher.Dispose();
                _creationWatcher = null;
            }

            if (_deletionWatcher != null)
            {
                _deletionWatcher.Stop();
                _deletionWatcher.EventArrived -= OnDeviceDisconnected;
                _deletionWatcher.Dispose();
                _deletionWatcher = null;
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.System, $"Failed to stop WMI USB event monitoring: {ex.Message}");
        }
        finally
        {
            IsMonitoring = false;
        }
    }

    private void OnDeviceConnected(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is ManagementBaseObject target)
            {
                var deviceInfo = ExtractUsbDeviceInfo(target);
                if (deviceInfo != null)
                {
                    DeviceConnected?.Invoke(this, deviceInfo.Value);
                }
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"WMI connected event error: {ex.Message}");
        }
    }

    private void OnDeviceDisconnected(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is ManagementBaseObject target)
            {
                var deviceInfo = ExtractUsbDeviceInfo(target);
                if (deviceInfo != null)
                {
                    DeviceDisconnected?.Invoke(this, deviceInfo.Value);
                }
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"WMI disconnected event error: {ex.Message}");
        }
    }

    private static UsbDeviceInfo? ExtractUsbDeviceInfo(ManagementBaseObject target)
    {
        try
        {
            // Name is the one the OS actually shows an operator. Description on
            // a USB interface is the generic "USB Composite Device" on every
            // phone ever made, which identifies nothing.
            string name = target["Name"]?.ToString() ?? target["Description"]?.ToString() ?? target["Caption"]?.ToString() ?? "";

            return FromDeviceId(target["DeviceID"]?.ToString(), name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The name Windows has for a device with this vendor ID, read back from
    /// the PnP tree rather than from the interface that raised the event.
    ///
    /// The interface calls itself "USB Composite Device" whatever is plugged
    /// in; the portable device sitting underneath it is the node Windows names
    /// after the phone ("HONOR 600 Lite (LNA-NX1)"). The vendor ID is the one
    /// part both share, so that is what the lookup keys on.
    ///
    /// An empty string means Windows had no better answer than the hardware,
    /// which is a valid answer: the caller falls back to the brand.
    /// </summary>
    public static string ReadDeviceName(ushort vendorId)
    {
        if (vendorId == 0 || !OperatingSystem.IsWindows()) return "";

        try
        {
            string pattern = $"%VID_{vendorId:X4}%";
            string portable = "";
            string best = "";
            int bestScore = -1;

            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '{pattern}'");

            foreach (ManagementBaseObject entry in searcher.Get())
            {
                string name = entry["Name"]?.ToString() ?? "";
                string id = entry["DeviceID"]?.ToString() ?? "";
                if (name.Length == 0) continue;

                // The portable-device node is the one Windows fills in from the
                // phone itself, so it wins outright when it is there.
                if (id.StartsWith("SWD\\WPDBUSENUM", StringComparison.OrdinalIgnoreCase))
                {
                    portable = name;
                    continue;
                }

                int score = AndroidDeviceName.FromReportedText(name).Length;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = name;
                }
            }

            return portable.Length > 0 ? portable : (bestScore > 0 ? best : "");
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"WMI device name lookup failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Every USB device Windows currently has a PnP entry for.
    ///
    /// The creation events only cover devices that arrived after this process
    /// started. A kiosk usually has the phone already on the cable, and for
    /// that phone there is no event to wait for, so the tree is read directly
    /// when the brand and model are needed.
    /// </summary>
    public static IReadOnlyList<UsbDeviceInfo> ListConnectedDevices()
    {
        var devices = new List<UsbDeviceInfo>();
        if (!OperatingSystem.IsWindows()) return devices;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%'");

            foreach (ManagementBaseObject entry in searcher.Get())
            {
                UsbDeviceInfo? device = FromDeviceId(
                    entry["DeviceID"]?.ToString(),
                    entry["Name"]?.ToString() ?? entry["Description"]?.ToString() ?? "");
                if (device != null) devices.Add(device.Value);
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"WMI device enumeration failed: {ex.Message}");
        }

        return devices;
    }

    /// <summary>Parses "USB\VID_XXXX&PID_YYYY\..." into a device, or null.</summary>
    private static UsbDeviceInfo? FromDeviceId(string? deviceId, string name)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !deviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            return null;

        ushort vid = 0, pid = 0;
        foreach (string part in deviceId.Split('\\', '&'))
        {
            if (part.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                ushort.TryParse(part[4..], System.Globalization.NumberStyles.HexNumber, null, out vid);
            else if (part.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
                ushort.TryParse(part[4..], System.Globalization.NumberStyles.HexNumber, null, out pid);
        }

        if (vid == 0) return null;

        return new UsbDeviceInfo(vid, pid, deviceId, name);
    }

    public void Dispose() => Stop();
}