using System;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Information about a USB device from the native event system.
/// </summary>
public readonly record struct UsbDeviceInfo
{
    /// <summary>The USB vendor ID (VID).</summary>
    public readonly ushort VendorId;

    /// <summary>The USB product ID (PID).</summary>
    public readonly ushort ProductId;

    /// <summary>The device instance ID or path (platform-specific).</summary>
    public readonly string DeviceInstanceId;

    /// <summary>A human-readable description when available.</summary>
    public readonly string Description;

    public UsbDeviceInfo(ushort vendorId, ushort productId, string deviceInstanceId, string description = "")
    {
        VendorId = vendorId;
        ProductId = productId;
        DeviceInstanceId = deviceInstanceId;
        Description = description;
    }

    public override string ToString()
        => $"USB\\VID_{VendorId:X4}&PID_{ProductId:X4} ({Description})";
}