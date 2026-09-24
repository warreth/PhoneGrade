using System;
using System.Runtime.InteropServices;

namespace PhoneGrade.Core;

/// <summary>
/// Watches for USB insertion and removal so the kiosk can react the instant a cable
/// is pulled instead of waiting for the next polling tick.
/// The WMI implementation is Windows only. On other platforms the class stays inert
/// and the regular polling loop remains the single source of truth.
/// </summary>
public static class UsbEventWatcher
{
    public static event EventHandler? UsbDeviceConnected;
    public static event EventHandler? UsbDeviceDisconnected;

    /// <summary>True when a native OS watcher is running on this platform.</summary>
    public static bool IsNativeMonitoringActive { get; private set; }

    public static void StartMonitoring()
    {
        if (IsNativeMonitoringActive) return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SystemEventLogger.Info(LogSource.System,
                "USB event monitoring is Windows only. Falling back to the polling loop.");
            return;
        }

#if WINDOWS
        StartWindowsMonitoring();
#else
        SystemEventLogger.Info(LogSource.System,
            "USB event monitoring was not built for this target. Falling back to the polling loop.");
#endif
    }

    public static void StopMonitoring()
    {
#if WINDOWS
        StopWindowsMonitoring();
#endif
        IsNativeMonitoringActive = false;
    }

    /// <summary>Raises the connected event. Exposed for tests that simulate a plug event.</summary>
    public static void RaiseConnected() => UsbDeviceConnected?.Invoke(null, EventArgs.Empty);

    /// <summary>Raises the disconnected event. Exposed for tests that simulate an unplug event.</summary>
    public static void RaiseDisconnected() => UsbDeviceDisconnected?.Invoke(null, EventArgs.Empty);

#if WINDOWS
    private static System.Management.ManagementEventWatcher? _insertWatcher;
    private static System.Management.ManagementEventWatcher? _removeWatcher;

    private static void StartWindowsMonitoring()
    {
        try
        {
            var insertQuery = new System.Management.WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _insertWatcher = new System.Management.ManagementEventWatcher(insertQuery);
            _insertWatcher.EventArrived += (_, _) => RaiseConnected();
            _insertWatcher.Start();

            var removeQuery = new System.Management.WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _removeWatcher = new System.Management.ManagementEventWatcher(removeQuery);
            _removeWatcher.EventArrived += (_, _) => RaiseDisconnected();
            _removeWatcher.Start();

            IsNativeMonitoringActive = true;
            SystemEventLogger.Info(LogSource.System, "Windows USB event monitoring started via WMI.");
        }
        catch (Exception ex)
        {
            IsNativeMonitoringActive = false;
            SystemEventLogger.Error(LogSource.System, $"Failed to start USB event monitoring: {ex.Message}");
        }
    }

    private static void StopWindowsMonitoring()
    {
        try
        {
            _insertWatcher?.Stop();
            _insertWatcher?.Dispose();
            _insertWatcher = null;

            _removeWatcher?.Stop();
            _removeWatcher?.Dispose();
            _removeWatcher = null;
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.System, $"Failed to stop USB event monitoring: {ex.Message}");
        }
    }
#endif
}
