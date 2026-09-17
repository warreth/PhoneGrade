using System.Diagnostics;

namespace AutoDymoLabel.Core;

/// <summary>High-level device operations via the bundled libimobiledevice tools.</summary>
public static class DeviceService
{
    /// <summary>All connected devices: UDID → "Name: Model" for display.</summary>
    public static async Task<Dictionary<string, string>> GetConnectedDevicesAsync()
    {
        var (udids, _, _) = await ListUdidsSafeAsync();
        if (udids.Length > 0)
        {
            var devices = new Dictionary<string, string>();
            foreach (var id in udids)
            {
                if (id.Length == 0) continue;
                string name = (await GetKeyAsync(id, "DeviceName")).Trim();
                string model = Mappers.MapModel(await GetKeyAsync(id, "ProductType"));
                devices[id] = string.IsNullOrWhiteSpace(name) ? model : $"{name} ({model})";
            }
            return devices;
        }

        // idevice_id unavailable or errored — fallback probe
        string probe = await GetKeyAsync("", "DeviceName");
        return probe.StartsWith("ERROR:") || probe == "NO OUTPUT" || string.IsNullOrWhiteSpace(probe)
            ? []
            : throw new InvalidOperationException("idevice_id missing but lockdownd reachable");
    }

    /// <summary>Reads a single value from the lockdown domain. Returns "ERROR: ..." on failure.</summary>
    public static async Task<string> GetKeyAsync(string udid, string key)
    {
        string udidArg = udid.Length > 0 ? $"-u {udid} " : "";
        var (output, _) = await ToolRunner.RunAsync("ideviceinfo", $"{udidArg}-k {key}");
        return output;
    }

    /// <summary>Reads domain-specific output from ideviceinfo. Returns empty string on failure.</summary>
    public static async Task<string> GetDomainAsync(string udid, string domain)
    {
        string udidArg = udid.Length > 0 ? $"-u {udid} " : "";
        var (output, _) = await ToolRunner.RunAsync("ideviceinfo", $"{udidArg}-q {domain}");
        return output.StartsWith("ERROR:") ? "" : output;
    }

    /// <summary>Device state summary used by the auto-flow and UI.</summary>
    public enum ConnectionState
    {
        Connected,
        NotTrusted,
        NotActivated,
        NotFound,
        ToolsMissing,
        DaemonStopped,
        PermissionDenied,
        DriverMissing
    }

    /// <summary>Detailed diagnostic report of usbmuxd / Apple Mobile Device Service.</summary>
    public record DaemonDiagnosis(bool IsRunning, string StatusMessage, string Remediation);

    /// <summary>Checks whether usbmuxd or Apple Mobile Device Service is running and responding.</summary>
    public static Task<DaemonDiagnosis> CheckDaemonStatusAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return Task.FromResult(CheckWindowsDaemonStatus());
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Task.FromResult(CheckMacDaemonStatus());
        }
        else
        {
            // Linux check: /var/run/usbmuxd socket or process
            bool socketExists = File.Exists("/var/run/usbmuxd");
            bool processRunning = Process.GetProcessesByName("usbmuxd").Length > 0;
            if (socketExists || processRunning)
                return Task.FromResult(new DaemonDiagnosis(true, "usbmuxd is actief", ""));

            return Task.FromResult(new DaemonDiagnosis(false, "usbmuxd daemon draait niet",
                "Start usbmuxd via systemctl: sudo systemctl start usbmuxd"));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static DaemonDiagnosis CheckWindowsServiceStatus()
    {
        string[] serviceNames = ["Apple Mobile Device Service", "usbmuxd"];
        bool anyFound = false;

        foreach (var svcName in serviceNames)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(svcName);
                anyFound = true;
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                    return new DaemonDiagnosis(true, $"{svcName} is actief", "");
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                    return new DaemonDiagnosis(false, $"{svcName} is gestopt",
                        $"Start de service via Services (services.msc) of run: net start \"{svcName}\"");
            }
            catch
            {
                // Service may not exist with this exact name, try next
            }
        }

        return new DaemonDiagnosis(false, "Apple Mobile Device Service niet gevonden", anyFound ? "Service gestopt" : "");
    }

    private static DaemonDiagnosis CheckWindowsDaemonStatus()
    {
        if (OperatingSystem.IsWindows())
        {
            var svcCheck = CheckWindowsServiceStatus();
            if (svcCheck.IsRunning) return svcCheck;
            if (!string.IsNullOrEmpty(svcCheck.Remediation)) return svcCheck;
        }

        // Process probe fallback for AppleMobileDeviceProcess or usbmuxd
        var procMatches = Process.GetProcesses().Where(p =>
        {
            try { return p.ProcessName.Contains("AppleMobileDevice") || p.ProcessName.Contains("usbmuxd"); }
            catch { return false; }
        }).ToArray();

        if (procMatches.Length > 0)
            return new DaemonDiagnosis(true, "Apple Mobile Device proces gevonden", "");

        return new DaemonDiagnosis(false, "Apple USB Driver / Mobile Device Service ontbreekt of is gestopt",
            "Installeer iTunes of Apple Devices (of stand-alone Apple Mobile Device Support) om de drivers te installeren.");
    }

    private static DaemonDiagnosis CheckMacDaemonStatus()
    {
        bool socketExists = File.Exists("/var/run/usbmuxd");
        bool processRunning = Process.GetProcessesByName("usbmuxd").Length > 0;

        if (socketExists || processRunning)
            return new DaemonDiagnosis(true, "usbmuxd is actief", "");

        return new DaemonDiagnosis(false, "usbmuxd socket /var/run/usbmuxd niet gevonden",
            "Controleer of de usbmuxd daemon draait of herstart via: sudo launchctl kickstart -k system/com.apple.usbmuxd");
    }

    public static async Task<ConnectionState> GetConnectionStateAsync(string? udid = null)
    {
        var (devices, _, diagState) = await ListUdidsSafeAsync();
        if (diagState != ConnectionState.Connected && devices.Length == 0)
            return diagState;

        if (devices.Length == 0) return ConnectionState.NotFound;
        if (udid != null && !devices.Contains(udid)) return ConnectionState.NotFound;

        string targetUdid = udid ?? devices[0];
        string info = await GetKeyAsync(targetUdid, "ProductType");
        if (info.Contains("Could not connect to lockdownd") || info.Contains("PasswordProtected") || info.Contains("PairingDialogResponsePending"))
            return ConnectionState.NotTrusted;
        if (info.StartsWith("ERROR:") || info == "NO OUTPUT")
            return ConnectionState.ToolsMissing;

        string activation = await GetKeyAsync(targetUdid, "ActivationState");
        return activation.Contains("Unactivated") ? ConnectionState.NotActivated : ConnectionState.Connected;
    }

    /// <summary>Lists UDIDs inspecting stdout and stderr, falling back to ideviceinfo -s on error.</summary>
    public static async Task<(string[] Udids, string Raw, ConnectionState DiagnosticState)> ListUdidsSafeAsync()
    {
        var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync("idevice_id", "-l");

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var list = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => s.Length > 0)
                             .ToArray();
            return (list, stdout, ConnectionState.Connected);
        }

        // Check if stderr indicates specific daemon or permission errors
        string combinedErr = (stderr + " " + stdout).Trim();
        if (combinedErr.Contains("Permission denied") || combinedErr.Contains("Operation not permitted"))
        {
            return ([], combinedErr, ConnectionState.PermissionDenied);
        }
        if (combinedErr.Contains("No device found") || combinedErr.Contains("No devices found"))
        {
            return ([], combinedErr, ConnectionState.NotFound);
        }
        if (combinedErr.Contains("usbmuxd") || combinedErr.Contains("Could not connect to usbmuxd") || combinedErr.Contains("Connection refused"))
        {
            var daemon = await CheckDaemonStatusAsync();
            return ([], combinedErr, daemon.IsRunning ? ConnectionState.DriverMissing : ConnectionState.DaemonStopped);
        }

        // Fallback probe using ideviceinfo -s when idevice_id exits with error or empty
        var (infoOut, infoErr, infoExit) = await ToolRunner.ExecuteAsync("ideviceinfo", "-s");
        if (infoExit == 0 && !string.IsNullOrWhiteSpace(infoOut))
        {
            string? fallbackUdid = Parsers.KeyValue(infoOut, "UniqueDeviceID");
            if (!string.IsNullOrWhiteSpace(fallbackUdid))
            {
                return ([fallbackUdid.Trim()], fallbackUdid.Trim(), ConnectionState.Connected);
            }
        }

        string fallbackErr = (infoErr + " " + combinedErr).Trim();
        if (fallbackErr.Contains("Could not connect to lockdownd"))
        {
            return ([], fallbackErr, ConnectionState.NotTrusted);
        }
        if (fallbackErr.Contains("usbmuxd") || fallbackErr.Contains("Could not connect to usbmuxd"))
        {
            return ([], fallbackErr, ConnectionState.DaemonStopped);
        }

        return ([], combinedErr.Length == 0 ? "NO OUTPUT" : combinedErr, ConnectionState.NotFound);
    }

    /// <summary>Collects all label + diagnostic data for one device.</summary>
    public static async Task<DeviceData> GetDeviceDataAsync(string udid)
    {
        string productType = (await GetKeyAsync(udid, "ProductType")).Trim();
        string imei = await GetKeyAsync(udid, "InternationalMobileEquipmentIdentity");
        string serial = await GetKeyAsync(udid, "SerialNumber");

        var data = new DeviceData
        {
            DeviceId = udid,
            ProductType = productType,
            Model = Mappers.MapModel(productType),
            Identifier = Parsers.ParseIdentifier(imei, serial),
            Color = Mappers.MapColor(await GetKeyAsync(udid, "DeviceEnclosureColor")),
            IosVersion = (await GetKeyAsync(udid, "ProductVersion")).Trim(),
            MotherboardSerialNumber = serial.StartsWith("ERROR:") ? "" : serial.Trim(),
        };

        data.Storage = await GetStorageAsync(udid);
        await PopulateBatteryMetricsAsync(udid, data);
        await PopulateHardwareSerialsAndChecksAsync(udid, data);

        return data;
    }

    /// <summary>Extracts extended battery metrics from ioregentry and com.apple.mobile.battery.</summary>
    public static async Task PopulateBatteryMetricsAsync(string udid, DeviceData data)
    {
        var (plist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleSmartBattery");
        if (plist.StartsWith("ERROR:") || string.IsNullOrWhiteSpace(plist))
        {
            (plist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleARMPMUCharger");
        }

        bool hasIoreg = !plist.StartsWith("ERROR:") && !string.IsNullOrWhiteSpace(plist);

        int? cycle = hasIoreg ? Parsers.PlistInt(plist, "CycleCount") : null;
        int? design = hasIoreg ? Parsers.PlistInt(plist, "DesignCapacity") : null;
        int? rawMax = hasIoreg ? Parsers.PlistInt(plist, "AppleRawMaxCapacity")
                              ?? Parsers.PlistInt(plist, "MaxCapacity")
                              ?? Parsers.PlistInt(plist, "NominalChargeCapacity") : null;
        string? battSerial = hasIoreg ? Parsers.PlistString(plist, "Serial")
                                     ?? Parsers.PlistString(plist, "BatterySerialNumber") : null;

        // Fall back to lockdown com.apple.mobile.battery domain if values missing
        if (cycle == null || design == null || rawMax == null || string.IsNullOrEmpty(battSerial))
        {
            string battDomain = await GetDomainAsync(udid, "com.apple.mobile.battery");
            if (!string.IsNullOrWhiteSpace(battDomain))
            {
                cycle ??= int.TryParse(Parsers.KeyValue(battDomain, "CycleCount"), out int c) ? c : null;
                design ??= int.TryParse(Parsers.KeyValue(battDomain, "DesignCapacity"), out int d) ? d : null;
                rawMax ??= int.TryParse(Parsers.KeyValue(battDomain, "AppleRawMaxCapacity")
                                     ?? Parsers.KeyValue(battDomain, "NominalChargeCapacity"), out int cur) ? cur : null;
                battSerial ??= Parsers.KeyValue(battDomain, "Serial")
                              ?? Parsers.KeyValue(battDomain, "BatterySerialNumber");
            }
        }

        data.BatteryCycleCount = cycle ?? 0;
        data.BatteryDesignCapacity = design ?? 0;
        data.BatteryCurrentCapacity = rawMax ?? 0;
        data.BatterySerialNumber = Parsers.CleanSerial(battSerial);

        if (hasIoreg)
        {
            string health = Parsers.ParseBatteryHealth(plist);
            data.BatteryHealth = health;
        }

        if (data.BatteryHealth == "NOBATT" && data.BatteryDesignCapacity > 0 && data.BatteryCurrentCapacity > 0)
        {
            data.BatteryHealth = $"{Math.Min((double)data.BatteryCurrentCapacity / data.BatteryDesignCapacity * 100, 100):F0}";
        }
    }

    /// <summary>Queries OEM component serials and performs verification comparisons.</summary>
    public static async Task PopulateHardwareSerialsAndChecksAsync(string udid, DeviceData data)
    {
        // 1. Diagnostics / chargethrough / factory serials via lockdown domains
        string diagDomain = await GetDomainAsync(udid, "com.apple.mobile.diagnostics");
        string chargeDictStr = await GetDomainAsync(udid, "com.apple.mobile.chargethrough");

        var diagDict = Parsers.ParseKeyValues(diagDomain);
        var chargeDict = Parsers.ParseKeyValues(chargeDictStr);

        // 2. Query ioregentry for display/LCD and camera details
        var (displayPlist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleCLCD2");
        if (displayPlist.StartsWith("ERROR:"))
        {
            (displayPlist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry IOMobileFramebuffer");
        }

        var (camPlist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleH10CamIn");
        if (camPlist.StartsWith("ERROR:"))
        {
            (camPlist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleH6CamIn");
        }

        // Live serials
        string displaySerial = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "DisplaySerial") ??
            Parsers.PlistString(displayPlist, "SerialNumber") ??
            FindDictValue(diagDict, "DisplaySerialNumber", "ScreenSerial", "LCDSerial"));

        string coverGlass = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "CoverGlassSerial") ??
            FindDictValue(diagDict, "CoverGlassSerialNumber"));

        string frontCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "FrontCameraSerial") ??
            FindDictValue(diagDict, "FrontCameraSerialNumber", "FrontCameraSerial"));

        string rearCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "RearCameraSerial") ??
            FindDictValue(diagDict, "RearCameraSerialNumber", "RearCameraSerial", "BackCameraSerialNumber"));

        // Factory original serials from syscfg / chargethrough / lockdown
        string origBatt = Parsers.CleanSerial(
            FindDictValue(chargeDict, "OriginalBatterySerialNumber", "BatterySerial", "OriginalSerial") ??
            FindDictValue(diagDict, "OriginalBatterySerialNumber", "FactoryBatterySerialNumber"));

        string origDisplay = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber", "OriginalScreenSerial"));

        string origFrontCam = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalFrontCameraSerialNumber", "FactoryFrontCameraSerialNumber"));

        string origRearCam = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalRearCameraSerialNumber", "FactoryRearCameraSerialNumber"));

        data.DisplaySerialNumber = displaySerial;
        data.CoverGlassSerialNumber = coverGlass;
        data.FrontCameraSerialNumber = frontCam;
        data.RearCameraSerialNumber = rearCam;
        data.OriginalBatterySerialNumber = origBatt;

        // Perform ComponentChecks
        var checks = new List<ComponentStatus>();

        // Battery Check
        checks.Add(new ComponentStatus
        {
            Name = "Batterij",
            SerialRead = data.BatterySerialNumber,
            SerialOriginal = data.OriginalBatterySerialNumber,
            Status = Parsers.VerifyComponent(data.BatterySerialNumber, data.OriginalBatterySerialNumber)
        });

        // Display Check
        checks.Add(new ComponentStatus
        {
            Name = "Scherm (LCM)",
            SerialRead = data.DisplaySerialNumber,
            SerialOriginal = origDisplay,
            Status = Parsers.VerifyComponent(data.DisplaySerialNumber, origDisplay)
        });

        // Front Camera Check
        checks.Add(new ComponentStatus
        {
            Name = "Camera Voor",
            SerialRead = data.FrontCameraSerialNumber,
            SerialOriginal = origFrontCam,
            Status = Parsers.VerifyComponent(data.FrontCameraSerialNumber, origFrontCam)
        });

        // Rear Camera Check
        checks.Add(new ComponentStatus
        {
            Name = "Camera Achter",
            SerialRead = data.RearCameraSerialNumber,
            SerialOriginal = origRearCam,
            Status = Parsers.VerifyComponent(data.RearCameraSerialNumber, origRearCam)
        });

        data.ComponentChecks = checks;
    }

    private static string? FindDictValue(Dictionary<string, string> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
        }
        return null;
    }

    private static async Task<string> GetStorageAsync(string udid)
    {
        var (output, _) = await ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.disk_usage");
        string? raw = Parsers.KeyValue(output, "TotalDiskCapacity");
        return long.TryParse(raw, out long bytes) ? Mappers.MapStorage(bytes) : "NOSTORAGE";
    }
}
