using System;
using System.Management;
using System.Runtime.Versioning;

namespace PhoneGrade.Core;

/// <summary>
/// Watches for USB insertion and removal so the kiosk can react the instant a cable
/// is pulled instead of waiting for the next polling tick.
/// The WMI implementation only runs on Windows. On other platforms the class stays
/// inert and the regular polling loop remains the single source of truth.
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

        if (!OperatingSystem.IsWindows())
        {
            SystemEventLogger.Info(LogSource.System,
                "USB event monitoring is Windows only. Falling back to the polling loop.");
            return;
        }

        StartWindowsMonitoring();
    }

    public static void StopMonitoring()
    {
        if (OperatingSystem.IsWindows())
        {
            StopWindowsMonitoring();
        }
        IsNativeMonitoringActive = false;
    }

    /// <summary>Raises the connected event. Exposed for tests that simulate a plug event.</summary>
    public static void RaiseConnected() => UsbDeviceConnected?.Invoke(null, EventArgs.Empty);

    /// <summary>Raises the disconnected event. Exposed for tests that simulate an unplug event.</summary>
    public static void RaiseDisconnected() => UsbDeviceDisconnected?.Invoke(null, EventArgs.Empty);

    private static ManagementEventWatcher? _insertWatcher;
    private static ManagementEventWatcher? _removeWatcher;

    /// <summary>
    /// Starts the two WMI watchers. Only reached when the runtime reports Windows;
    /// the attribute keeps the platform analyzer quiet without a WINDOWS define.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void StartWindowsMonitoring()
    {
        // A previous attempt can fail after the insert watcher is already running.
        // Clearing those first is what keeps a retry from orphaning a live watcher
        // by overwriting the field that was holding it.
        StopWindowsMonitoring();

        try
        {
            var insertQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _insertWatcher = new ManagementEventWatcher(insertQuery);
            _insertWatcher.EventArrived += (_, _) => RaiseConnected();
            _insertWatcher.Start();

            var removeQuery = new WqlEventQuery(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _removeWatcher = new ManagementEventWatcher(removeQuery);
            _removeWatcher.EventArrived += (_, _) => RaiseDisconnected();
            _removeWatcher.Start();

            IsNativeMonitoringActive = true;
            SystemEventLogger.Info(LogSource.System, "Windows USB event monitoring started via WMI.");
        }
        catch (Exception ex)
        {
            // Leave nothing half-started: a partially running watcher would fire
            // events with no way to stop it, since the flag says monitoring is off.
            StopWindowsMonitoring();
            IsNativeMonitoringActive = false;
            SystemEventLogger.Error(LogSource.System, $"Failed to start USB event monitoring: {ex.Message}");
        }
    }

    /// <summary>Stops and disposes both WMI watchers. Safe to call when none are running.</summary>
    [SupportedOSPlatform("windows")]
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
}
