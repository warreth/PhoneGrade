using Parsing;
using Mappings;
using static CommandExecution.CommandExecution;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeviceService
{
    public static class DeviceService
    {
        public static async Task<bool> IsDeviceConnectedAsync() //TODO: Fix thiss
        {
            var result = await ExecuteCommandAsync("ideviceinfo", "");
            return !result.Contains("ERROR");
        }

        public static async Task<bool> IsDeviceTrustedAsync()
        {
            var result = await ExecuteCommandAsync("ideviceinfo", "");
            return !result.Contains("ERROR: Could not connect to lockdownd");
        }

        public static async Task<bool> IsActivatedAsync()
        {
            var result = await ExecuteCommandAsync("ideviceinfo", "-k ActivationState");
            return !result.Contains("Unactivated");
        }

        public static async Task<Dictionary<string, string>> GetConnectedDevicesAsync()
        {
            string output = await ExecuteCommandAsync("idevice_id", "-l");
            if (string.IsNullOrEmpty(output))
            {
                return new Dictionary<string, string>();
            }

            var deviceIds = output.Split('\n').Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var devices = new Dictionary<string, string>();

            foreach (var deviceId in deviceIds)
            {
                string deviceName = (await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -k DeviceName")).Trim();
                string modelName = await GetModelAsync(deviceId);
                string key = $"{deviceName}: {modelName}";
                devices[deviceId] = key;
            }

            return devices;
        }

        public static async Task<DeviceData> GetDeviceDataAsync(string deviceId)
        {
            return new DeviceData
            {
                Identifier = await GetIdentifierAsync(deviceId),
                BatteryHealth = await GetBatteryHealthAsync(deviceId),
                Color = await GetColorAsync(deviceId),
                Storage = await GetStorageAsync(deviceId),
                Model = await GetModelAsync(deviceId),
                DeviceId = deviceId
            };
        }

        private static async Task<string> GetBatteryHealthAsync(string deviceId)
        {
            // Attempt the first command
            string output1 = await ExecuteCommandAsync("idevicediagnostics", $"-u {deviceId} ioregentry AppleARMPMUCharger");

            // Attempt the second command if the first one fails
            string output2 = await ExecuteCommandAsync("idevicediagnostics", $"-u {deviceId} ioregentry AppleSmartBattery");

            // Use the first successful output
            string? plistOutput = !string.IsNullOrEmpty(output1) ? output1
                            : !string.IsNullOrEmpty(output2) ? output2
                            : null;

            // Parse the battery health
            if (plistOutput != null && ParsingBatteryHealth.ParseBatteryHealth(plistOutput, out double? batteryHealth))
            {
                return $"{batteryHealth:F0}";
            }
            else
            {
                return "NOBATT";
            }
        }

        private static async Task<string> GetColorAsync(string deviceId)
        {
            ColorMapper colorMapper = new();
            string color = await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -k DeviceEnclosureColor");
            return colorMapper.MapColor(color);
        }

        private static async Task<string> GetStorageAsync(string deviceId)
        {
            string output = await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -q com.apple.disk_usage");
            ParsingStorage.ParseStorage(output, out string totalDiskCapacityGB);
            return totalDiskCapacityGB;
        }

        private static async Task<string> GetModelAsync(string deviceId)
        {
            ModelMapper modelMapper = new();
            string model = await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -k ProductType");
            return modelMapper.MapModel(model);
        }

        private static async Task<string> GetImeiAsync(string deviceId)
        {
            return await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -k InternationalMobileEquipmentIdentity");
        }

        private static async Task<string> GetSerialNumberAsync(string deviceId)
        {
            return await ExecuteCommandAsync("ideviceinfo", $"-u {deviceId} -k SerialNumber");
        }

        private static async Task<string> GetIdentifierAsync(string deviceId)
        {
            string imei = await GetImeiAsync(deviceId);
            string serialNumber = await GetSerialNumberAsync(deviceId);
            return ParsingDeviceIdentifier.ParseDeviceIdentifier(imei, serialNumber, out string identifier) ? identifier : "NOID";
        }
    }
}