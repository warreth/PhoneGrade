using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// macOS USB event monitor using IOKit (IOServiceAddMatchingNotification).
/// Receives real-time USB device connect/disconnect notifications.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacUsbMonitor : IUsbEventMonitor
{
    public event EventHandler<UsbDeviceInfo>? DeviceConnected;
    public event EventHandler<UsbDeviceInfo>? DeviceDisconnected;

    public bool IsMonitoring { get; private set; }

    // IOKit notification ports
    private IntPtr _notifyPort = IntPtr.Zero;
    private IntPtr _addedIterator = IntPtr.Zero;
    private IntPtr _removedIterator = IntPtr.Zero;

    // IOKit keeps the raw callback pointer, so the delegates have to outlive
    // Start(). A local delegate is collectible the moment Start returns, and
    // the next USB event would then call into freed thunks.
    private IOKit.IOServiceMatchingCallback? _addedCallback;
    private IOKit.IOServiceMatchingCallback? _removedCallback;

    private readonly object _lock = new();
    private bool _disposed;

    public void Start()
    {
        if (IsMonitoring) return;

        if (!OperatingSystem.IsMacOS())
        {
            SystemEventLogger.Info(LogSource.System, "MacUsbMonitor: Not on macOS, monitoring not started.");
            return;
        }

        try
        {
            // Create a notification port
            _notifyPort = IOKit.IONotificationPortCreate(IOKit.kIOMainPortDefault);
            if (_notifyPort == IntPtr.Zero)
            {
                SystemEventLogger.Error(LogSource.System, "Failed to create IOKit notification port");
                return;
            }

            // Add the notification port to the run loop
            var runLoopSource = IOKit.IONotificationPortGetRunLoopSource(_notifyPort);
            if (runLoopSource != IntPtr.Zero)
            {
                CoreFoundation.CFRunLoopAddSource(CoreFoundation.CFRunLoopGetCurrent(), runLoopSource, CoreFoundation.kCFRunLoopDefaultMode);
            }

            // Create matching dictionary for USB devices
            var matchingDict = IOKit.IOServiceMatching(IOKit.kIOUSBDeviceClassName);
            if (matchingDict == IntPtr.Zero)
            {
                SystemEventLogger.Error(LogSource.System, "Failed to create IOServiceMatching dictionary");
                Stop();
                return;
            }

            // Register for device added notifications
            _addedCallback = new IOKit.IOServiceMatchingCallback(OnDeviceAdded);
            var result = IOKit.IOServiceAddMatchingNotification(
                _notifyPort,
                IOKit.kIOFirstMatchNotification,
                matchingDict,
                _addedCallback,
                IntPtr.Zero,
                out _addedIterator);

            if (result != IOKit.kIOReturnSuccess)
            {
                SystemEventLogger.Error(LogSource.System, $"IOServiceAddMatchingNotification (added) failed: 0x{result:X}");
                Stop();
                return;
            }

            // Process any existing devices
            ProcessIterator(_addedIterator);

            // Register for device removed notifications
            _removedCallback = new IOKit.IOServiceMatchingCallback(OnDeviceRemoved);
            matchingDict = IOKit.IOServiceMatching(IOKit.kIOUSBDeviceClassName);
            result = IOKit.IOServiceAddMatchingNotification(
                _notifyPort,
                IOKit.kIOTerminatedNotification,
                matchingDict,
                _removedCallback,
                IntPtr.Zero,
                out _removedIterator);

            if (result != IOKit.kIOReturnSuccess)
            {
                SystemEventLogger.Error(LogSource.System, $"IOServiceAddMatchingNotification (removed) failed: 0x{result:X}");
                Stop();
                return;
            }

            // Process any terminated devices
            ProcessIterator(_removedIterator);

            IsMonitoring = true;
            SystemEventLogger.Info(LogSource.System, "macOS USB event monitoring started via IOKit.");
        }
        catch (Exception ex)
        {
            Stop();
            IsMonitoring = false;
            SystemEventLogger.Error(LogSource.System, $"Failed to start IOKit USB event monitoring: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed) return;

            try
            {
                if (_addedIterator != IntPtr.Zero)
                {
                    IOKit.IOObjectRelease(_addedIterator);
                    _addedIterator = IntPtr.Zero;
                }

                if (_removedIterator != IntPtr.Zero)
                {
                    IOKit.IOObjectRelease(_removedIterator);
                    _removedIterator = IntPtr.Zero;
                }

                if (_notifyPort != IntPtr.Zero)
                {
                    var runLoopSource = IOKit.IONotificationPortGetRunLoopSource(_notifyPort);
                    if (runLoopSource != IntPtr.Zero)
                    {
                        CoreFoundation.CFRunLoopRemoveSource(CoreFoundation.CFRunLoopGetCurrent(), runLoopSource, CoreFoundation.kCFRunLoopDefaultMode);
                    }
                    IOKit.IONotificationPortDestroy(_notifyPort);
                    _notifyPort = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                SystemEventLogger.Error(LogSource.System, $"Failed to stop IOKit USB event monitoring: {ex.Message}");
            }
            finally
            {
                IsMonitoring = false;
                _addedCallback = null;
                _removedCallback = null;
            }
        }
    }

    private void OnDeviceAdded(IntPtr refCon, IntPtr iterator)
    {
        try
        {
            ProcessIterator(iterator, isAdded: true);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"IOKit device added callback error: {ex.Message}");
        }
    }

    private void OnDeviceRemoved(IntPtr refCon, IntPtr iterator)
    {
        try
        {
            ProcessIterator(iterator, isAdded: false);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"IOKit device removed callback error: {ex.Message}");
        }
    }

    private void ProcessIterator(IntPtr iterator, bool isAdded = true)
    {
        while (true)
        {
            IntPtr service = IOKit.IOIteratorNext(iterator);
            if (service == IntPtr.Zero) break;

            try
            {
                var deviceInfo = ExtractUsbDeviceInfo(service);
                if (deviceInfo != null)
                {
                    if (isAdded)
                        DeviceConnected?.Invoke(this, deviceInfo.Value);
                    else
                        DeviceDisconnected?.Invoke(this, deviceInfo.Value);
                }
            }
            finally
            {
                IOKit.IOObjectRelease(service);
            }
        }
    }

    private UsbDeviceInfo? ExtractUsbDeviceInfo(IntPtr service)
    {
        try
        {
            // Get vendor ID
            var vendorIdObj = IOKit.IORegistryEntryCreateCFProperty(service, CFSTR("idVendor"), IntPtr.Zero, 0);
            ushort vid = 0;
            if (vendorIdObj != IntPtr.Zero)
            {
                if (CoreFoundation.CFNumberGetValue(vendorIdObj, CoreFoundation.kCFNumberSInt16Type, out long v))
                    vid = (ushort)v;
                CoreFoundation.CFRelease(vendorIdObj);
            }

            // Get product ID
            var productIdObj = IOKit.IORegistryEntryCreateCFProperty(service, CFSTR("idProduct"), IntPtr.Zero, 0);
            ushort pid = 0;
            if (productIdObj != IntPtr.Zero)
            {
                if (CoreFoundation.CFNumberGetValue(productIdObj, CoreFoundation.kCFNumberSInt16Type, out long p))
                    pid = (ushort)p;
                CoreFoundation.CFRelease(productIdObj);
            }

            // Get device location/path
            var locationObj = IOKit.IORegistryEntryCreateCFProperty(service, CFSTR("locationID"), IntPtr.Zero, 0);
            string location = "";
            if (locationObj != IntPtr.Zero)
            {
                if (CoreFoundation.CFNumberGetValue(locationObj, CoreFoundation.kCFNumberSInt32Type, out long loc))
                    location = $"0x{loc:X}";
                CoreFoundation.CFRelease(locationObj);
            }

            // Get product name
            var productNameObj = IOKit.IORegistryEntryCreateCFProperty(service, CFSTR("USB Product Name"), IntPtr.Zero, 0);
            string productName = "";
            if (productNameObj != IntPtr.Zero)
            {
                var ptr = CoreFoundation.CFStringGetCStringPtr(productNameObj, CoreFoundation.kCFStringEncodingUTF8);
                productName = ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
                CoreFoundation.CFRelease(productNameObj);
            }

            // Get vendor name
            var vendorNameObj = IOKit.IORegistryEntryCreateCFProperty(service, CFSTR("USB Vendor Name"), IntPtr.Zero, 0);
            string vendorName = "";
            if (vendorNameObj != IntPtr.Zero)
            {
                var ptr = CoreFoundation.CFStringGetCStringPtr(vendorNameObj, CoreFoundation.kCFStringEncodingUTF8);
                vendorName = ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? "" : "";
                CoreFoundation.CFRelease(vendorNameObj);
            }

            string description = $"{vendorName} {productName}".Trim();

            return new UsbDeviceInfo(vid, pid, location, description);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Stop() checks _disposed itself, so it has to run before the flag
            // is set; the other way round leaves the IOKit port and iterators
            // unreleased.
            Stop();
            _disposed = true;
        }
    }

    // P/Invoke declarations for IOKit
    private static class IOKit
    {
        public const string LibraryName = "/System/Library/Frameworks/IOKit.framework/IOKit";
        
        public const int kIOReturnSuccess = 0;

        // Notification types are names, not numbers: IOServiceAddMatchingNotification
        // takes a C string (io_name_t) and the IOKit headers define these two as
        // "IOServiceFirstMatch" and "IOServiceTerminate". Passing 1 or 2 marshals
        // the integer as a char pointer and takes the process down.
        public const string kIOFirstMatchNotification = "IOServiceFirstMatch";
        public const string kIOTerminatedNotification = "IOServiceTerminate";
        public const string kIOUSBDeviceClassName = "IOUSBDevice";

        public delegate void IOServiceMatchingCallback(IntPtr refCon, IntPtr iterator);

        [DllImport(LibraryName, EntryPoint = "IONotificationPortCreate")]
        public static extern IntPtr IONotificationPortCreate(IntPtr masterPort);

        [DllImport(LibraryName, EntryPoint = "IONotificationPortDestroy")]
        public static extern void IONotificationPortDestroy(IntPtr notifyPort);

        [DllImport(LibraryName, EntryPoint = "IONotificationPortGetRunLoopSource")]
        public static extern IntPtr IONotificationPortGetRunLoopSource(IntPtr notifyPort);

        [DllImport(LibraryName, EntryPoint = "IOServiceAddMatchingNotification")]
        public static extern int IOServiceAddMatchingNotification(
            IntPtr notifyPort,
            [MarshalAs(UnmanagedType.LPStr)] string notificationType,
            IntPtr matching,
            IOServiceMatchingCallback callback,
            IntPtr refCon,
            out IntPtr iterator);

        [DllImport(LibraryName, EntryPoint = "IOServiceMatching")]
        public static extern IntPtr IOServiceMatching(string name);

        [DllImport(LibraryName, EntryPoint = "IOIteratorNext")]
        public static extern IntPtr IOIteratorNext(IntPtr iterator);

        [DllImport(LibraryName, EntryPoint = "IOObjectRelease")]
        public static extern void IOObjectRelease(IntPtr objectRef);

        [DllImport(LibraryName, EntryPoint = "IORegistryEntryCreateCFProperty")]
        public static extern IntPtr IORegistryEntryCreateCFProperty(
            IntPtr entry,
            IntPtr key,
            IntPtr allocator,
            int options);

        public static IntPtr kIOMainPortDefault => IntPtr.Zero;
    }

    // P/Invoke declarations for CoreFoundation
    private static class CoreFoundation
    {
        public const string LibraryName = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        public const int kCFNumberSInt16Type = 3;
        public const int kCFNumberSInt32Type = 4;
        public const int kCFStringEncodingUTF8 = 0x08000100;

        // The run loop mode is the CFString named "kCFRunLoopDefaultMode". The
        // framework exports a constant, but reading an exported symbol from
        // managed code is fragile, and CFString comparison is by contents, so a
        // created string of the same name is the same mode.
        public static IntPtr kCFRunLoopDefaultMode => CFSTR("kCFRunLoopDefaultMode");

        [DllImport(LibraryName, EntryPoint = "CFRunLoopGetCurrent")]
        public static extern IntPtr CFRunLoopGetCurrent();

        [DllImport(LibraryName, EntryPoint = "CFRunLoopAddSource")]
        public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

        [DllImport(LibraryName, EntryPoint = "CFRunLoopRemoveSource")]
        public static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

        [DllImport(LibraryName, EntryPoint = "CFNumberGetValue")]
        public static extern bool CFNumberGetValue(IntPtr number, int type, out long value);

        [DllImport(LibraryName, EntryPoint = "CFStringGetCStringPtr")]
        public static extern IntPtr CFStringGetCStringPtr(IntPtr str, int encoding);

        [DllImport(LibraryName, EntryPoint = "CFStringCreateWithCString")]
        public static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, int encoding);

        [DllImport(LibraryName, EntryPoint = "CFRelease")]
        public static extern void CFRelease(IntPtr cf);
    }

    // Helper to create CFString constants.
    //
    // IORegistryEntryCreateCFProperty takes each registry key as a CFString, and the
    // stub that returned IntPtr.Zero here meant every one of those properties came
    // back null: no vendor, product, location or name on macOS. The strings are
    // cached on purpose, one creation per name per process, because they are read
    // on every USB event and the key handed to IOKit has to stay alive anyway.
    private static readonly Dictionary<string, IntPtr> CFStringCache = new();

    private static IntPtr CFSTR(string str)
    {
        lock (CFStringCache)
        {
            if (CFStringCache.TryGetValue(str, out IntPtr cached))
            {
                return cached;
            }

            IntPtr created = CoreFoundation.CFStringCreateWithCString(
                IntPtr.Zero, str, CoreFoundation.kCFStringEncodingUTF8);
            if (created == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"CFStringCreateWithCString refused '{str}'");
            }

            CFStringCache[str] = created;
            return created;
        }
    }
}