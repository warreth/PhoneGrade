using System;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Interface for platform-specific USB event monitoring.
/// Implementations use native OS APIs to receive real-time device connect/disconnect events.
/// </summary>
public interface IUsbEventMonitor : IDisposable
{
    /// <summary>Raised when a USB device is connected.</summary>
    event EventHandler<UsbDeviceInfo> DeviceConnected;

    /// <summary>Raised when a USB device is disconnected.</summary>
    event EventHandler<UsbDeviceInfo> DeviceDisconnected;

    /// <summary>True when the native monitor is actively watching.</summary>
    bool IsMonitoring { get; }

    /// <summary>Starts monitoring for USB events.</summary>
    void Start();

    /// <summary>Stops monitoring for USB events.</summary>
    void Stop();
}