using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Usb;

/// <summary>
/// Linux USB event monitor using libudev (udev_monitor).
/// Receives real-time USB device connect/disconnect notifications via netlink.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxUsbMonitor : IUsbEventMonitor
{
    public event EventHandler<UsbDeviceInfo>? DeviceConnected;
    public event EventHandler<UsbDeviceInfo>? DeviceDisconnected;

    public bool IsMonitoring { get; private set; }

    private IntPtr _udev = IntPtr.Zero;
    private IntPtr _monitor = IntPtr.Zero;
    private Thread? _monitorThread;
    private volatile bool _running;
    private readonly object _lock = new();

    public void Start()
    {
        if (IsMonitoring) return;

        if (!OperatingSystem.IsLinux())
        {
            SystemEventLogger.Info(LogSource.System, "LinuxUsbMonitor: Not on Linux, monitoring not started.");
            return;
        }

        try
        {
            // Create udev context
            _udev = LibUdev.udev_new();
            if (_udev == IntPtr.Zero)
            {
                SystemEventLogger.Error(LogSource.System, "Failed to create udev context");
                return;
            }

            // Create monitor
            _monitor = LibUdev.udev_monitor_new_from_netlink(_udev, "udev");
            if (_monitor == IntPtr.Zero)
            {
                SystemEventLogger.Error(LogSource.System, "Failed to create udev monitor");
                Stop();
                return;
            }

            // Filter for USB subsystem
            int result = LibUdev.udev_monitor_filter_add_match_subsystem_devtype(_monitor, "usb", null);
            if (result < 0)
            {
                SystemEventLogger.Error(LogSource.System, $"Failed to add USB subsystem filter: {result}");
                Stop();
                return;
            }

            // Enable receiving events
            result = LibUdev.udev_monitor_enable_receiving(_monitor);
            if (result < 0)
            {
                SystemEventLogger.Error(LogSource.System, $"Failed to enable udev monitor receiving: {result}");
                Stop();
                return;
            }

            // Get file descriptor for polling
            int fd = LibUdev.udev_monitor_get_fd(_monitor);
            if (fd < 0)
            {
                SystemEventLogger.Error(LogSource.System, "Failed to get udev monitor fd");
                Stop();
                return;
            }

            _running = true;
            _monitorThread = new Thread(() => MonitorLoop(fd))
            {
                IsBackground = true,
                Name = "LinuxUsbMonitor"
            };
            _monitorThread.Start();

            IsMonitoring = true;
            SystemEventLogger.Info(LogSource.System, "Linux USB event monitoring started via libudev.");
        }
        catch (Exception ex)
        {
            Stop();
            IsMonitoring = false;
            SystemEventLogger.Error(LogSource.System, $"Failed to start libudev USB event monitoring: {ex.Message}");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;

            if (_monitorThread != null)
            {
                if (!_monitorThread.Join(TimeSpan.FromSeconds(2)))
                {
                    SystemEventLogger.Warning(LogSource.System, "Linux USB monitor thread did not exit cleanly");
                }
                _monitorThread = null;
            }

            if (_monitor != IntPtr.Zero)
            {
                LibUdev.udev_monitor_unref(_monitor);
                _monitor = IntPtr.Zero;
            }

            if (_udev != IntPtr.Zero)
            {
                LibUdev.udev_unref(_udev);
                _udev = IntPtr.Zero;
            }

            IsMonitoring = false;
        }
    }

    private void MonitorLoop(int fd)
    {
        var pollFds = new LibUdev.pollfd { fd = fd, events = LibUdev.POLLIN };

        while (_running)
        {
            try
            {
                int ret = LibUdev.poll(ref pollFds, 1, 1000); // 1 second timeout
                
                if (ret > 0 && (pollFds.revents & LibUdev.POLLIN) != 0)
                {
                    IntPtr device = LibUdev.udev_monitor_receive_device(_monitor);
                    if (device != IntPtr.Zero)
                    {
                        try
                        {
                            ProcessDevice(device);
                        }
                        finally
                        {
                            LibUdev.udev_device_unref(device);
                        }
                    }
                }
                else if (ret < 0)
                {
                    SystemEventLogger.Debug(LogSource.System, $"udev poll error: {Marshal.GetLastWin32Error()}");
                    break;
                }
            }
            catch (Exception ex) when (_running)
            {
                SystemEventLogger.Debug(LogSource.System, $"udev monitor loop error: {ex.Message}");
            }
        }
    }

    private void ProcessDevice(IntPtr device)
    {
        try
        {
            string? action = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_action(device));
            if (string.IsNullOrEmpty(action)) return;

            // Get vendor and product IDs
            string? vidStr = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_sysattr_value(device, "idVendor"));
            string? pidStr = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_sysattr_value(device, "idProduct"));

            if (!ushort.TryParse(vidStr, System.Globalization.NumberStyles.HexNumber, null, out ushort vid) ||
                !ushort.TryParse(pidStr, System.Globalization.NumberStyles.HexNumber, null, out ushort pid))
            {
                return; // Not a device with VID/PID
            }

            // Get device path
            string? devPath = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_devpath(device)) ?? "";
            
            // Get product and vendor names
            string? product = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_property_value(device, "ID_MODEL"));
            string? vendor = Marshal.PtrToStringAnsi(LibUdev.udev_device_get_property_value(device, "ID_VENDOR"));
            string description = $"{vendor} {product}".Trim();

            var deviceInfo = new UsbDeviceInfo(vid, pid, devPath, description);

            if (action == "add" || action == "bind")
            {
                DeviceConnected?.Invoke(this, deviceInfo);
            }
            else if (action == "remove" || action == "unbind")
            {
                DeviceDisconnected?.Invoke(this, deviceInfo);
            }
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.System, $"udev device processing error: {ex.Message}");
        }
    }

    public void Dispose() => Stop();

    private static class LibUdev
    {
        public const string LibraryName = "libudev.so.1";

        public const short POLLIN = 0x001;

        [StructLayout(LayoutKind.Sequential)]
        public struct pollfd
        {
            public int fd;
            public short events;
            public short revents;
        }

        [DllImport(LibraryName, EntryPoint = "udev_new")]
        public static extern IntPtr udev_new();

        [DllImport(LibraryName, EntryPoint = "udev_unref")]
        public static extern void udev_unref(IntPtr udev);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_new_from_netlink")]
        public static extern IntPtr udev_monitor_new_from_netlink(IntPtr udev, string name);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_unref")]
        public static extern void udev_monitor_unref(IntPtr monitor);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_filter_add_match_subsystem_devtype")]
        public static extern int udev_monitor_filter_add_match_subsystem_devtype(IntPtr monitor, string subsystem, string? devtype);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_enable_receiving")]
        public static extern int udev_monitor_enable_receiving(IntPtr monitor);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_get_fd")]
        public static extern int udev_monitor_get_fd(IntPtr monitor);

        [DllImport(LibraryName, EntryPoint = "udev_monitor_receive_device")]
        public static extern IntPtr udev_monitor_receive_device(IntPtr monitor);

        [DllImport(LibraryName, EntryPoint = "udev_device_unref")]
        public static extern void udev_device_unref(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "udev_device_get_action")]
        public static extern IntPtr udev_device_get_action(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "udev_device_get_sysattr_value")]
        public static extern IntPtr udev_device_get_sysattr_value(IntPtr device, string name);

        [DllImport(LibraryName, EntryPoint = "udev_device_get_devpath")]
        public static extern IntPtr udev_device_get_devpath(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "udev_device_get_property_value")]
        public static extern IntPtr udev_device_get_property_value(IntPtr device, string name);

        [DllImport("libc", EntryPoint = "poll")]
        public static extern int poll(ref pollfd fds, int nfds, int timeout);
    }
}