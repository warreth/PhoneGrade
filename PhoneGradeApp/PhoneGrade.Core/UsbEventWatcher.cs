using System;
using System.Management;
using System.Threading;

namespace PhoneGrade.Core;

/// <summary>
/// Monitors native Windows WMI events for USB insertions and removals to trigger faster polling
/// and auto-reset disconnected device sessions reliably.
/// </summary>
public static class UsbEventWatcher
{
    private static ManagementEventWatcher? _insertWatcher;
    private static ManagementEventWatcher? _removeWatcher;
    private static SynchronizationContext? _syncContext;

    public static event EventHandler? UsbDeviceConnected;
    public static event EventHandler? UsbDeviceDisconnected;

    public static void StartMonitoring()
    {
        _syncContext = SynchronizationContext.Current;

        try
        {
            var insertQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _insertWatcher = new ManagementEventWatcher(insertQuery);
            _insertWatcher.EventArrived += (sender, e) =>
            {
                if (_syncContext != null)
                    _syncContext.Post(_ => UsbDeviceConnected?.Invoke(null, EventArgs.Empty), null);
                else
                    UsbDeviceConnected?.Invoke(null, EventArgs.Empty);
            };
            _insertWatcher.Start();

            var removeQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            _removeWatcher = new ManagementEventWatcher(removeQuery);
            _removeWatcher.EventArrived += (sender, e) =>
            {
                if (_syncContext != null)
                    _syncContext.Post(_ => UsbDeviceDisconnected?.Invoke(null, EventArgs.Empty), null);
                else
                    UsbDeviceDisconnected?.Invoke(null, EventArgs.Empty);
            };
            _removeWatcher.Start();

            SystemEventLogger.Info(LogSource.System, "Windows USB Event Monitoring gestart via WMI.");
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.System, $"Fout bij starten USB Event Monitoring: {ex.Message}");
        }
    }

    public static void StopMonitoring()
    {
        try
        {
            if (_insertWatcher != null)
            {
                _insertWatcher.Stop();
                _insertWatcher.Dispose();
                _insertWatcher = null;
            }

            if (_removeWatcher != null)
            {
                _removeWatcher.Stop();
                _removeWatcher.Dispose();
                _removeWatcher = null;
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.System, $"Fout bij stoppen USB Event Monitoring: {ex.Message}");
        }
    }
}
