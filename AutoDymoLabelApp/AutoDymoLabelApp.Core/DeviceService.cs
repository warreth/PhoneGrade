namespace AutoDymoLabel.Core;

/// <summary>High-level device operations via the bundled libimobiledevice tools.</summary>
public static class DeviceService
{
    /// <summary>All connected devices: UDID → "Name: Model" for display.</summary>
    public static async Task<Dictionary<string, string>> GetConnectedDevicesAsync()
    {
        var (output, _) = await ToolRunner.RunAsync("idevice_id", "-l");
        if (!output.StartsWith("ERROR:") && output != "NO OUTPUT")
        {
            var devices = new Dictionary<string, string>();
            foreach (var udid in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string id = udid.Trim();
                if (id.Length == 0) continue;
                string name = (await GetKeyAsync(id, "DeviceName")).Trim();
                string model = Mappers.MapModel(await GetKeyAsync(id, "ProductType"));
                devices[id] = string.IsNullOrWhiteSpace(name) ? model : $"{name} ({model})";
            }
            return devices;
        }

        // idevice_id unavailable — fall back to a full-blown tool that reports connectivity.
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

    /// <summary>Device state summary used by the auto-flow and UI.</summary>
    public enum ConnectionState { Connected, NotTrusted, NotActivated, NotFound, ToolsMissing }

    public static async Task<ConnectionState> GetConnectionStateAsync(string? udid = null)
    {
        var (devices, _) = await ListUdidsSafeAsync();
        if (devices.Length == 0) return ConnectionState.NotFound;
        if (udid != null && !devices.Contains(udid)) return ConnectionState.NotFound;

        string info = await GetKeyAsync(udid ?? devices[0], "ProductType");
        if (info.Contains("Could not connect to lockdownd"))
            return ConnectionState.NotTrusted;
        if (info.StartsWith("ERROR:") || info == "NO OUTPUT")
            return ConnectionState.ToolsMissing;
        string activation = await GetKeyAsync(udid ?? devices[0], "ActivationState");
        return activation.Contains("Unactivated") ? ConnectionState.NotActivated : ConnectionState.Connected;
    }

    private static async Task<(string[] Udids, string Raw)> ListUdidsSafeAsync()
    {
        var (output, _) = await ToolRunner.RunAsync("idevice_id", "-l");
        if (output.StartsWith("ERROR:") || output == "NO OUTPUT") return ([], output);
        return (output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray(), output);
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
        };

        data.Storage = await GetStorageAsync(udid);
        data.BatteryHealth = await GetBatteryHealthAsync(udid);
        return data;
    }

    private static async Task<string> GetBatteryHealthAsync(string udid)
    {
        var (plist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleSmartBattery");
        if (plist.StartsWith("ERROR:")) return "NOBATT";
        string health = Parsers.ParseBatteryHealth(plist);
        if (health != "NOBATT") return health;

        // Older devices expose the charger entry instead.
        (plist, _) = await ToolRunner.RunAsync("idevicediagnostics", $"-u {udid} ioregentry AppleARMPMUCharger");
        return plist.StartsWith("ERROR:") ? "NOBATT" : Parsers.ParseBatteryHealth(plist);
    }

    private static async Task<string> GetStorageAsync(string udid)
    {
        var (output, _) = await ToolRunner.RunAsync("ideviceinfo", $"-u {udid} -q com.apple.disk_usage");
        string? raw = Parsers.KeyValue(output, "TotalDiskCapacity");
        return long.TryParse(raw, out long bytes) ? Mappers.MapStorage(bytes) : "NOSTORAGE";
    }
}
