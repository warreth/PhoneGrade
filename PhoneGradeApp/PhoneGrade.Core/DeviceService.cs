using System.Collections.Concurrent;
using System.Diagnostics;

namespace PhoneGrade.Core;

/// <summary>High-level device operations via the bundled libimobiledevice tools.</summary>
public static class DeviceService
{
    /// <summary>All connected devices: UDID → "Name: Model" for display (iOS and Android).</summary>
    public static async Task<Dictionary<string, string>> GetConnectedDevicesAsync()
    {
        var (udids, _, _) = await ListUdidsSafeAsync();
        var devices = new Dictionary<string, string>();

        if (udids.Length > 0)
        {
            foreach (var id in udids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;

                // Check if this is an Android serial or iOS UDID
                // iOS UDIDs are 40 hex chars (SHA-1) or 24 hex chars (UDID format)
                bool looksLikeiOS = id.Length == 40 || (id.Length == 24 && id.All(c => "0123456789abcdefABCDEF-".Contains(c)));
                
                if (!looksLikeiOS && await IsAndroidDeviceAsync(id))
                {
                    string model = await GetAndroidPropAsync(id, "ro.product.model");
                    string brand = await GetAndroidPropAsync(id, "ro.product.brand");
                    string display = string.IsNullOrWhiteSpace(brand) ? model : $"{brand} {model}".Trim();
                    devices[id] = string.IsNullOrWhiteSpace(display) ? $"Android Device ({id})" : $"{display} (Android)";
                    // Android device identified
                }
                else
                {
                    // Retrieve basic info instantly (no ioregentry)
                    string quickInfo = await GetDomainAsync(id, "");
                    string name = (Parsers.KeyValue(quickInfo, "DeviceName") ?? "").Trim();
                    string productType = Parsers.KeyValue(quickInfo, "ProductType") ?? "";

                    string model = Mappers.MapModel(productType);
                    devices[id] = string.IsNullOrWhiteSpace(name) ? model : $"{name} ({model})";
                }
            }
            return devices;
        }

        // idevice_id was checked in ListUdidsSafeAsync. If no devices were found via idevice_id or adb,
        // do not run redundant fallback queries that flood log output.
        return devices;
    }

    /// <summary>Returns true if the identifier matches an iOS UDID pattern (40 hex chars or 24/25 chars with hyphen).</summary>
    public static bool LooksLikeIosUdid(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        string trimmed = id.Trim();
        return (trimmed.Length == 40 && trimmed.All(Uri.IsHexDigit)) ||
               (trimmed.Length >= 24 && trimmed.Length <= 25 && trimmed.All(c => Uri.IsHexDigit(c) || c == '-'));
    }

    /// <summary>Checks whether a given device identifier corresponds to an authorized Android device.</summary>
    public static async Task<bool> IsAndroidDeviceAsync(string id)
    {
        if (LooksLikeIosUdid(id)) return false;

        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("adb", $"-s {id} get-state");
            return exitCode == 0 && output.Trim() == "device";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads an Android system property via adb shell getprop.</summary>
    public static async Task<string> GetAndroidPropAsync(string serial, string propName)
    {
        try
        {
            var (output, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} shell getprop {propName}");
            return output.Trim();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Reads a single value from the lockdown domain. Returns "ERROR: ..." on failure.</summary>
    /// <summary>Reads a key from in-memory cached bulk XML dictionary (single-pass extraction, zero CLI spam).</summary>
    public static async Task<string> GetKeyAsync(string udid, string key)
    {
        if (string.IsNullOrWhiteSpace(udid)) return "";
        var raw = await GetBulkRawDataAsync(udid);
        
        // Search default lockdown dictionary first, then gestalt, then diagnostics
        string? val = FindDictValue(raw.DefaultDict, key) ?? 
                      FindDictValue(raw.GestaltDict, key) ?? 
                      FindDictValue(raw.DiagDict, key);
                      
        return val ?? "";
    }

    /// <summary>Reads domain-specific output from ideviceinfo. Returns empty string on failure.</summary>
    public static async Task<string> GetDomainAsync(string udid, string domain)
    {
        string udidArg = udid.Length > 0 ? $"-u {udid} " : "";
        string args = string.IsNullOrWhiteSpace(domain) ? udidArg.Trim() : $"{udidArg}-q {domain}";
        var (output, _) = await ToolRunner.RunAsync("ideviceinfo", args);
        return output.StartsWith("ERROR:") ? "" : output;
    }

    /// <summary>Container for all raw device data queried concurrently.</summary>
    public class DeviceRawData
    {
        public string DefaultXml { get; set; } = "";
        public string GestaltXml { get; set; } = "";
        public string DiagXml { get; set; } = "";
        public string DiskXml { get; set; } = "";
        public string PurpleBuddyXml { get; set; } = "";
        public string FmipXml { get; set; } = "";
        public string IORegDisplay { get; set; } = "";
        public string IORegCamera { get; set; } = "";
        public string IORegBio { get; set; } = "";
        public string IORegBattery { get; set; } = "";
        
        public Dictionary<string, string> DefaultDict { get; set; } = new();
        public Dictionary<string, string> GestaltDict { get; set; } = new();
        public Dictionary<string, string> DiagDict { get; set; } = new();
        public Dictionary<string, string> DiskDict { get; set; } = new();
        public Dictionary<string, string> PurpleBuddyDict { get; set; } = new();
        public Dictionary<string, string> FmipDict { get; set; } = new();
    }

    private static readonly ConcurrentDictionary<string, DeviceRawData> _rawCache = new();

    /// <summary>Performs all CLI queries concurrently and caches the parsed dictionaries.</summary>
    public static async Task<DeviceRawData> GetBulkRawDataAsync(string udid)
    {
        if (_rawCache.TryGetValue(udid, out var existing))
            return existing;

        var raw = new DeviceRawData();
        
        // Execute fast lockdown queries concurrently
        var tDefault = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -x");
        var tGestalt = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.mobile.gestalt -x");
        var tDiag = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.mobile.diagnostics -x");
        var tDisk = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.disk_usage -x");
        var tPb = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.purplebuddy -x");
        var tFmip = ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.fmip -x");

        // Execute slow ioregentry queries concurrently
        var tDisp = ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleCLCD2");
        var tCam = ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleH10CamIn");
        var tBio = ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleBiometricSensor");
        var tBatt = ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleSmartBattery");

        await Task.WhenAll(tDefault, tGestalt, tDiag, tDisk, tPb, tFmip, tDisp, tCam, tBio, tBatt);

        raw.DefaultXml = tDefault.Result.Output;
        raw.GestaltXml = tGestalt.Result.Output;
        raw.DiagXml = tDiag.Result.Output;
        raw.DiskXml = tDisk.Result.Output;
        raw.PurpleBuddyXml = tPb.Result.Output;
        raw.FmipXml = tFmip.Result.Output;
        raw.IORegDisplay = tDisp.Result.Output;
        raw.IORegCamera = tCam.Result.Output;
        raw.IORegBio = tBio.Result.Output;
        raw.IORegBattery = tBatt.Result.Output;

        // Parse XML directly
        raw.DefaultDict = Parsers.ParsePlistXml(raw.DefaultXml);
        raw.GestaltDict = Parsers.ParsePlistXml(raw.GestaltXml);
        raw.DiagDict = Parsers.ParsePlistXml(raw.DiagXml);
        raw.DiskDict = Parsers.ParsePlistXml(raw.DiskXml);
        raw.PurpleBuddyDict = Parsers.ParsePlistXml(raw.PurpleBuddyXml);
        raw.FmipDict = Parsers.ParsePlistXml(raw.FmipXml);

        _rawCache[udid] = raw;
        return raw;
    }


    /// <summary>Device state summary used by the auto-flow and UI.</summary>
    public enum ConnectionState
    {
        Connected,
        NotTrusted,
        Unauthorized,
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

        // Check if Android device
        if (!LooksLikeIosUdid(targetUdid))
        {
            if (await IsAndroidDeviceAsync(targetUdid))
            {
                return ConnectionState.Connected;
            }

            try
            {
                var (adbState, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {targetUdid} get-state");
                if (adbState.Contains("unauthorized"))
                {
                    return ConnectionState.Unauthorized;
                }
            }
            catch { }
        }

        // iOS checks
        string info = await GetKeyAsync(targetUdid, "ProductType");
        if (info.Contains("Could not connect to lockdownd") || info.Contains("PasswordProtected") || info.Contains("PairingDialogResponsePending"))
        {
            // iOS trust required
            return ConnectionState.NotTrusted;
        }
        if (info.StartsWith("ERROR:") || info == "NO OUTPUT")
            return ConnectionState.ToolsMissing;

        string activation = await GetKeyAsync(targetUdid, "ActivationState");
        if (activation.Contains("Unactivated"))
        {
            // iOS not activated
            return ConnectionState.NotActivated;
        }

        // iOS ready
        return ConnectionState.Connected;
    }

    /// <summary>Lists UDIDs (iOS and Android), inspecting stdout and stderr, with precise error diagnostics.</summary>
    public static async Task<(string[] Udids, string Raw, ConnectionState DiagnosticState)> ListUdidsSafeAsync()
    {
        // 1. Probe iOS devices via idevice_id
        var (stdout, stderr, exitCode) = await ToolRunner.ExecuteAsync("idevice_id", "-l");

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
        {
            var list = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => s.Length > 0)
                             .ToArray();
            if (list.Length > 0)
            {
                // Discovered iOS devices (logging suppressed to avoid polling spam)
                return (list, stdout, ConnectionState.Connected);
            }
        }

        // 2. Probe Android devices via adb devices
        try
        {
            var (adbOut, adbErr, adbExit) = await ToolRunner.ExecuteAsync("adb", "devices");
            if (adbExit == 0 && !string.IsNullOrWhiteSpace(adbOut))
            {
                var lines = adbOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(l => l.Trim())
                                  .Where(l => !l.StartsWith("List of devices") && l.Length > 0)
                                  .ToList();

                var androidDevices = new List<string>();
                bool hasUnauthorized = false;

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string serial = parts[0];
                        string state = parts[1];
                        if (state.Equals("device", StringComparison.OrdinalIgnoreCase))
                        {
                            androidDevices.Add(serial);
                        }
                        else if (state.Equals("unauthorized", StringComparison.OrdinalIgnoreCase))
                        {
                            hasUnauthorized = true;
                            // Unauthorized Android device detected
                        }
                    }
                }

                if (androidDevices.Count > 0)
                {
                    // Discovered Android devices (logging suppressed)
                    return (androidDevices.ToArray(), adbOut, ConnectionState.Connected);
                }

                if (hasUnauthorized)
                {
                    return ([], adbOut, ConnectionState.Unauthorized);
                }
            }
        }
        catch (Exception ex)
        {
            // ADB probe failed (suppressed)
        }

        // 3. Check if stderr indicates specific daemon or permission errors for iOS
        string combinedErr = (stderr + " " + stdout).Trim();
        if (combinedErr.Contains("Permission denied") || combinedErr.Contains("Operation not permitted"))
        {
            // Permission denied
            return ([], combinedErr, ConnectionState.PermissionDenied);
        }
        if (combinedErr.Contains("usbmuxd") || combinedErr.Contains("Could not connect to usbmuxd") || combinedErr.Contains("Connection refused"))
        {
            var daemon = await CheckDaemonStatusAsync();
            var state = daemon.IsRunning ? ConnectionState.DriverMissing : ConnectionState.DaemonStopped;
            // Daemon issue detected
            return ([], combinedErr, state);
        }

        // Fallback probe using ideviceinfo -s when idevice_id exits with error or empty
        var (infoOut, infoErr, infoExit) = await ToolRunner.ExecuteAsync("ideviceinfo", "-s");
        if (infoExit == 0 && !string.IsNullOrWhiteSpace(infoOut))
        {
            string? fallbackUdid = Parsers.KeyValue(infoOut, "UniqueDeviceID");
            if (!string.IsNullOrWhiteSpace(fallbackUdid))
            {
                // Fallback discovery via ideviceinfo
                return ([fallbackUdid.Trim()], fallbackUdid.Trim(), ConnectionState.Connected);
            }
        }

        string fallbackErr = (infoErr + " " + combinedErr).Trim();
        if (fallbackErr.Contains("Could not connect to lockdownd"))
        {
            // Waiting for trust
            return ([], fallbackErr, ConnectionState.NotTrusted);
        }
        if (fallbackErr.Contains("usbmuxd") || fallbackErr.Contains("Could not connect to usbmuxd"))
        {
            // usbmuxd unreachable
            return ([], fallbackErr, ConnectionState.DaemonStopped);
        }
        
        // Explicitly identify if the OS cannot find the executables (mac/linux/win variants)
        if (fallbackErr.Contains("No such file or directory") || 
            fallbackErr.Contains("The system cannot find the file specified") ||
            fallbackErr.Contains("not found") ||
            fallbackErr.Contains("failed to start process") ||
            combinedErr.Contains("No such file or directory") ||
            combinedErr.Contains("The system cannot find the file specified"))
        {
            // Tools missing
            return ([], fallbackErr, ConnectionState.ToolsMissing);
        }

        return ([], combinedErr.Length == 0 ? "NO OUTPUT" : combinedErr, ConnectionState.NotFound);
    }

    /// <summary>Extracts full device and security data for an Android device via ADB.</summary>
    public static async Task<DeviceData> GetAndroidDeviceDataAsync(string serial)
    {
        SystemEventLogger.Info(LogSource.UsbDetector, $"Reading Android device data for {serial}", serial);
        string model = await GetAndroidPropAsync(serial, "ro.product.model");
        string brand = await GetAndroidPropAsync(serial, "ro.product.brand");
        string androidVer = await GetAndroidPropAsync(serial, "ro.build.version.release");
        string hardwareSerial = await GetAndroidPropAsync(serial, "ro.serialno");
        string displayModel = string.IsNullOrWhiteSpace(brand) ? model : $"{brand} {model}".Trim();

        var data = new DeviceData
        {
            DeviceId = serial,
            ProductType = $"Android ({displayModel})",
            Model = string.IsNullOrWhiteSpace(displayModel) ? "Android Device" : displayModel,
            Identifier = string.IsNullOrWhiteSpace(hardwareSerial) ? serial : hardwareSerial,
            Color = "NOCOLOR",
            IosVersion = string.IsNullOrWhiteSpace(androidVer) ? "Android" : $"Android {androidVer}",
            MotherboardSerialNumber = hardwareSerial,
        };

        // Battery level from dumpsys battery
        try
        {
            var (battOut, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {serial} shell dumpsys battery");
            var match = System.Text.RegularExpressions.Regex.Match(battOut, @"level:\s*(\d+)");
            if (match.Success)
            {
                data.BatteryHealth = $"{match.Groups[1].Value}%";
            }
        }
        catch { }

        // Security checks
        data.CarrierLockAndroid = await SecurityServices.FrpLockService.DetectCarrierLockAsync(serial);

        SystemEventLogger.Info(LogSource.UsbDetector, $"Android data collected: {data.Model}, Battery: {data.BatteryHealth}", serial);
        return data;
    }

    /// <summary>Collects all label + diagnostic data for one device (optimized: single ideviceinfo call).</summary>
    public static async Task<DeviceData> GetDeviceDataAsync(string udid)
    {
        if (await IsAndroidDeviceAsync(udid))
        {
            return await GetAndroidDeviceDataAsync(udid);
        }

        // HIGH PERFORMANCE: Bulk parallel extraction of all domains
        var raw = await GetBulkRawDataAsync(udid);

        string productType = (FindDictValue(raw.DefaultDict, "ProductType") ?? "").Trim();
        string imei = (FindDictValue(raw.DefaultDict, "InternationalMobileEquipmentIdentity") ?? "").Trim();
        string serial = (FindDictValue(raw.DefaultDict, "SerialNumber") ?? "").Trim();
        string color = (FindDictValue(raw.DefaultDict, "DeviceEnclosureColor", "DeviceColor", "Color") ?? "").Trim();
        string iosVersion = (FindDictValue(raw.DefaultDict, "ProductVersion") ?? "").Trim();

        var data = new DeviceData
        {
            DeviceId = udid,
            ProductType = productType,
            Model = Mappers.MapModel(productType),
            Identifier = Parsers.ParseIdentifier(imei, serial),
            Color = !string.IsNullOrWhiteSpace(color) ? Mappers.MapColor(color) : "NOCOLOR",
            IosVersion = iosVersion,
            MotherboardSerialNumber = serial.StartsWith("ERROR:") ? "" : serial.Trim(),
        };

        // Storage from cached disk xml
        long diskBytes = 0;
        string? diskCap = FindDictValue(raw.DiskDict, "TotalDiskCapacity");
        if (long.TryParse(diskCap, out long db)) diskBytes = db;
        data.Storage = diskBytes > 0 ? Mappers.MapStorage(diskBytes) : await GetStorageAsync(udid);

        // Battery health & cycle count from cached ioreg & gestalt
        data.BatteryHealth = Parsers.ParseBatteryHealth(raw.IORegBattery);
        string? cycles = FindDictValue(raw.GestaltDict, "BatteryCycleCount");
        if (int.TryParse(cycles, out int cc)) data.BatteryCycleCount = cc;

        await PopulateHardwareSerialsAndChecksAsync(udid, data);

        // Security checks: iOS vs Android detection
        bool isIOS = productType.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || 
                     productType.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
                     productType.Contains("iPod", StringComparison.OrdinalIgnoreCase);

        if (isIOS)
        {
            data.ActivationLock = await SecurityServices.ActivationLockService.DetectAsync(udid);
            data.CarrierLockIOS = await SecurityServices.ActivationLockService.DetectCarrierLockAsync(udid);
            
            // Enhanced component verification with AST2
            var verification = await SecurityServices.ComponentVerificationService.VerifyComponentsAsync(udid, data);
            data.ComponentChecks = verification.ComponentChecks;
        }
        else
        {
            // Android device
            data.CarrierLockAndroid = await SecurityServices.FrpLockService.DetectCarrierLockAsync(udid);
        }

        // Optional: IMEI API integration for enhanced activation lock and carrier lock detection
        if (isIOS && !string.IsNullOrWhiteSpace(data.Identifier) && data.Identifier.Length >= 14)
        {
            var apiResult = await SecurityServices.ImeiApiService.CheckActivationLockAsync(data.Identifier);
            if (apiResult.Status != "Unknown" && apiResult.Status != "Error")
            {
                data.FmiVerificationSource = "Via Server (API)";
                // Override USB-detected activation lock with API result if available
                if (apiResult.Status.Contains("ON", StringComparison.OrdinalIgnoreCase) || 
                    apiResult.Status.Contains("Locked", StringComparison.OrdinalIgnoreCase))
                {
                    data.ActivationLock = SecurityServices.ActivationLockService.ActivationLockStatus.Locked;
                }
                else if (apiResult.Status.Contains("OFF", StringComparison.OrdinalIgnoreCase) || 
                         apiResult.Status.Contains("Clean", StringComparison.OrdinalIgnoreCase))
                {
                    data.ActivationLock = SecurityServices.ActivationLockService.ActivationLockStatus.Unlocked;
                }

                // Enhance carrier lock detection with API data
                if (!string.IsNullOrWhiteSpace(apiResult.CarrierLock))
                {
                    if (data.CarrierLockIOS == null)
                        data.CarrierLockIOS = new SecurityServices.ActivationLockService.CarrierLockStatus();
                    
                    data.CarrierLockIOS.IsCarrierLocked = apiResult.CarrierLock.Contains("Locked", StringComparison.OrdinalIgnoreCase);
                }

                // Populate blacklist status from API
                if (!string.IsNullOrWhiteSpace(apiResult.Blacklisted))
                {
                    data.Blacklist = new SecurityServices.BlacklistCheckService.BlacklistStatus
                    {
                        IsBlacklisted = apiResult.Blacklisted.Contains("Yes", StringComparison.OrdinalIgnoreCase) ||
                                       apiResult.Blacklisted.Contains("Blacklisted", StringComparison.OrdinalIgnoreCase),
                        Reason = apiResult.Message,
                        Source = apiResult.Source
                    };
                }
            }
        }

        // Optional: Blacklist check (GSMA provider stub)
        // Uncomment if GSMA_API_KEY is configured
        // if (string.IsNullOrWhiteSpace(data.Identifier) == false)
        // {
        //     var gsmaProvider = new SecurityServices.BlacklistCheckService.GsmaBlacklistProvider();
        //     data.Blacklist = await gsmaProvider.CheckAsync(data.Identifier);
        // }

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

    /// <summary>Queries OEM component serials and performs 3uTools-parity verification comparisons from in-memory bulk XML.</summary>
    public static async Task PopulateHardwareSerialsAndChecksAsync(string udid, DeviceData data)
    {
        // 1. Retrieve all raw domains and ioreg entries in a single parallel burst (cached)
        var raw = await GetBulkRawDataAsync(udid);

        var diagDict = raw.DiagDict;
        var gestaltDict = raw.GestaltDict;
        var defaultDict = raw.DefaultDict;

        // 2. Read ioreg plists directly from the bulk cache (zero extra process execution)
        string displayPlist = raw.IORegDisplay;
        string camPlist = raw.IORegCamera;
        string bioPlist = raw.IORegBio;

        // Live serials & addresses parsed strictly from in-memory dictionaries
        string displaySerial = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "DisplaySerial") ??
            Parsers.PlistString(displayPlist, "SerialNumber") ??
            FindDictValue(diagDict, "DisplaySerialNumber", "ScreenSerial", "LCDSerial") ??
            FindDictValue(gestaltDict, "DisplaySerialNumber", "ScreenSerial") ??
            FindDictValue(defaultDict, "DisplaySerialNumber"));

        string coverGlass = Parsers.CleanSerial(
            Parsers.PlistString(displayPlist, "CoverGlassSerial") ??
            FindDictValue(diagDict, "CoverGlassSerialNumber") ??
            FindDictValue(gestaltDict, "CoverGlassSerialNumber"));

        string frontCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "FrontCameraSerial") ??
            FindDictValue(diagDict, "FrontCameraSerialNumber", "FrontCameraSerial") ??
            FindDictValue(gestaltDict, "FrontCameraSerialNumber") ??
            FindDictValue(defaultDict, "FrontCameraSerialNumber"));

        string rearCam = Parsers.CleanSerial(
            Parsers.PlistString(camPlist, "RearCameraSerial") ??
            FindDictValue(diagDict, "RearCameraSerialNumber", "RearCameraSerial", "BackCameraSerialNumber") ??
            FindDictValue(gestaltDict, "RearCameraSerialNumber", "BackCameraSerialNumber") ??
            FindDictValue(defaultDict, "RearCameraSerialNumber"));

        string bioSerial = Parsers.CleanSerial(
            Parsers.PlistString(bioPlist, "SensorSerialNumber") ??
            Parsers.PlistString(bioPlist, "SerialNumber") ??
            FindDictValue(diagDict, "MesaSerialNumber", "TouchIDSerialNumber", "PearlSerialNumber") ??
            FindDictValue(gestaltDict, "MesaSerialNumber", "TouchIDSerialNumber", "PearlSerialNumber") ??
            FindDictValue(defaultDict, "MesaSerialNumber"));

        string mlbLive = Parsers.CleanSerial(
            FindDictValue(gestaltDict, "MLBSerialNumber", "BoardSerialNumber") ??
            FindDictValue(defaultDict, "MLBSerialNumber") ??
            data.MotherboardSerialNumber);

        string btMac = Parsers.CleanSerial(
            FindDictValue(gestaltDict, "BluetoothAddress") ??
            FindDictValue(defaultDict, "BluetoothAddress"));

        string wifiMac = Parsers.CleanSerial(
            FindDictValue(gestaltDict, "WifiAddress", "WiFiAddress") ??
            FindDictValue(defaultDict, "WifiAddress", "WiFiAddress"));

        string cellular = Parsers.CleanSerial(
            FindDictValue(gestaltDict, "CellularAddress") ??
            FindDictValue(defaultDict, "CellularAddress"));

        // Factory original serials from syscfg / lockdown in-memory dictionaries
        string origBatt = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalBatterySerialNumber", "FactoryBatterySerialNumber") ??
            FindDictValue(gestaltDict, "OriginalBatterySerialNumber") ??
            FindDictValue(defaultDict, "OriginalBatterySerialNumber"));

        string origDisplay = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber", "OriginalScreenSerial") ??
            FindDictValue(gestaltDict, "OriginalDisplaySerialNumber", "FactoryDisplaySerialNumber") ??
            FindDictValue(defaultDict, "OriginalDisplaySerialNumber"));

        string origFrontCam = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalFrontCameraSerialNumber", "FactoryFrontCameraSerialNumber") ??
            FindDictValue(gestaltDict, "OriginalFrontCameraSerialNumber") ??
            FindDictValue(defaultDict, "OriginalFrontCameraSerialNumber"));

        string origRearCam = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalRearCameraSerialNumber", "FactoryRearCameraSerialNumber") ??
            FindDictValue(gestaltDict, "OriginalRearCameraSerialNumber") ??
            FindDictValue(defaultDict, "OriginalRearCameraSerialNumber"));

        string origMlb = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalMLBSerialNumber", "FactoryMLBSerialNumber", "OriginalBoardSerialNumber") ??
            FindDictValue(gestaltDict, "OriginalMLBSerialNumber", "FactoryMLBSerialNumber") ??
            FindDictValue(defaultDict, "OriginalMLBSerialNumber"));

        string origBio = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalMesaSerialNumber", "FactoryMesaSerialNumber") ??
            FindDictValue(gestaltDict, "OriginalMesaSerialNumber") ??
            FindDictValue(defaultDict, "OriginalMesaSerialNumber"));

        string origBt = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalBluetoothAddress") ??
            FindDictValue(gestaltDict, "OriginalBluetoothAddress") ??
            FindDictValue(defaultDict, "OriginalBluetoothAddress"));

        string origWifi = Parsers.CleanSerial(
            FindDictValue(diagDict, "OriginalWifiAddress", "OriginalWiFiAddress") ??
            FindDictValue(gestaltDict, "OriginalWifiAddress", "OriginalWiFiAddress") ??
            FindDictValue(defaultDict, "OriginalWifiAddress"));

        // Populate device data model
        data.DisplaySerialNumber = displaySerial;
        data.CoverGlassSerialNumber = coverGlass;
        data.FrontCameraSerialNumber = frontCam;
        data.RearCameraSerialNumber = rearCam;
        data.OriginalBatterySerialNumber = origBatt;
        data.MotherboardSerialNumber = mlbLive;
        data.TouchIdFaceIdSerialNumber = bioSerial;
        data.BluetoothMacAddress = btMac;
        data.WifiMacAddress = wifiMac;
        data.CellularAddress = cellular;

        // Perform ComponentChecks (3uTools Parity layout)
        var checks = new List<ComponentStatus>();

        // 1. Moederbord (Logic Board)
        string safeMlbRead = !Parsers.IsUnreadable(mlbLive) ? mlbLive : (Parsers.IsUnreadable(data.Identifier) ? "Onbekend" : data.Identifier);
        string safeMlbOrig = !Parsers.IsUnreadable(origMlb) ? origMlb : safeMlbRead;
        checks.Add(new ComponentStatus
        {
            Name = "Moederbord (Logic Board)",
            SerialRead = safeMlbRead,
            SerialOriginal = safeMlbOrig,
            Status = Parsers.VerifyComponent(safeMlbRead, safeMlbOrig)
        });

        // 2. Batterij
        string safeBattRead = !Parsers.IsUnreadable(data.BatterySerialNumber) ? data.BatterySerialNumber : "Onbekend";
        string safeBattOrig = !Parsers.IsUnreadable(data.OriginalBatterySerialNumber) ? data.OriginalBatterySerialNumber : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Batterij",
            SerialRead = safeBattRead,
            SerialOriginal = safeBattOrig,
            Status = Parsers.VerifyComponent(safeBattRead, safeBattOrig)
        });

        // 3. Scherm (LCM)
        string safeDispRead = !Parsers.IsUnreadable(data.DisplaySerialNumber) ? data.DisplaySerialNumber : "Onbekend";
        string safeDispOrig = !Parsers.IsUnreadable(origDisplay) ? origDisplay : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Scherm (LCM)",
            SerialRead = safeDispRead,
            SerialOriginal = safeDispOrig,
            Status = Parsers.VerifyComponent(safeDispRead, safeDispOrig)
        });

        // 4. Camera Achter
        string safeRearRead = !Parsers.IsUnreadable(data.RearCameraSerialNumber) ? data.RearCameraSerialNumber : "Onbekend";
        string safeRearOrig = !Parsers.IsUnreadable(origRearCam) ? origRearCam : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Camera Achter",
            SerialRead = safeRearRead,
            SerialOriginal = safeRearOrig,
            Status = Parsers.VerifyComponent(safeRearRead, safeRearOrig)
        });

        // 5. Camera Voor
        string safeFrontRead = !Parsers.IsUnreadable(data.FrontCameraSerialNumber) ? data.FrontCameraSerialNumber : "Onbekend";
        string safeFrontOrig = !Parsers.IsUnreadable(origFrontCam) ? origFrontCam : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Camera Voor",
            SerialRead = safeFrontRead,
            SerialOriginal = safeFrontOrig,
            Status = Parsers.VerifyComponent(safeFrontRead, safeFrontOrig)
        });

        // 6. Touch ID / Face ID
        string safeBioRead = !Parsers.IsUnreadable(bioSerial) ? bioSerial : "Onbekend";
        string safeBioOrig = !Parsers.IsUnreadable(origBio) ? origBio : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Touch ID / Face ID",
            SerialRead = safeBioRead,
            SerialOriginal = safeBioOrig,
            Status = Parsers.VerifyComponent(safeBioRead, safeBioOrig)
        });

        // 7. Wi-Fi MAC Adres
        string safeWifiRead = !Parsers.IsUnreadable(wifiMac) ? wifiMac : "Onbekend";
        string safeWifiOrig = !Parsers.IsUnreadable(origWifi) ? origWifi : safeWifiRead;
        checks.Add(new ComponentStatus
        {
            Name = "Wi-Fi Adres",
            SerialRead = safeWifiRead,
            SerialOriginal = safeWifiOrig,
            Status = Parsers.VerifyComponent(safeWifiRead, safeWifiOrig)
        });

        // 8. Bluetooth Adres
        string safeBtRead = !Parsers.IsUnreadable(btMac) ? btMac : "Onbekend";
        string safeBtOrig = !Parsers.IsUnreadable(origBt) ? origBt : safeBtRead;
        checks.Add(new ComponentStatus
        {
            Name = "Bluetooth Adres",
            SerialRead = safeBtRead,
            SerialOriginal = safeBtOrig,
            Status = Parsers.VerifyComponent(safeBtRead, safeBtOrig)
        });

        // 9. Mobiel / Cellular Adres
        string safeCellRead = !Parsers.IsUnreadable(cellular) ? cellular : "Onbekend";
        checks.Add(new ComponentStatus
        {
            Name = "Cellular Adres",
            SerialRead = safeCellRead,
            SerialOriginal = safeCellRead,
            Status = Parsers.VerifyComponent(safeCellRead, safeCellRead)
        });

        data.ComponentChecks = checks;
    }

    public static string? FindDictValue(Dictionary<string, string> dict, params string[] keys)
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
        var raw = await GetBulkRawDataAsync(udid);
        string? rawStr = FindDictValue(raw.DiskDict, "TotalDiskCapacity");
        return long.TryParse(rawStr, out long bytes) ? Mappers.MapStorage(bytes) : "NOSTORAGE";
    }
}
