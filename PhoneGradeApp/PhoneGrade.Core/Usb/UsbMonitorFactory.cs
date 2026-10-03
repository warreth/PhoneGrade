using System;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Factory for creating the platform-specific USB event monitor.
/// Returns a singleton instance per process.
/// </summary>
public static class UsbMonitorFactory
{
    private static IUsbEventMonitor? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the singleton USB event monitor for the current platform.
    /// </summary>
    public static IUsbEventMonitor Instance
    {
        get
        {
            if (_instance != null) return _instance;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                if (OperatingSystem.IsWindows())
                {
                    _instance = new WmiUsbMonitor();
                }
                else if (OperatingSystem.IsMacOS())
                {
                    _instance = new MacUsbMonitor();
                }
                else if (OperatingSystem.IsLinux())
                {
                    _instance = new LinuxUsbMonitor();
                }
                else
                {
                    _instance = new NullUsbMonitor();
                    SystemEventLogger.Info(LogSource.System, "USB event monitoring not supported on this platform.");
                }

                return _instance;
            }
        }
    }

    /// <summary>
    /// Resets the singleton (for testing).
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}

/// <summary>
/// Null object implementation for unsupported platforms.
/// </summary>
#pragma warning disable CS0067
internal sealed class NullUsbMonitor : IUsbEventMonitor
{
    public event EventHandler<UsbDeviceInfo>? DeviceConnected;
    public event EventHandler<UsbDeviceInfo>? DeviceDisconnected;

    public bool IsMonitoring => false;

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
#pragma warning restore CS0067