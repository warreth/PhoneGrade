using Parsing;
using Mappings;
using static CommandExecution.CommandExecution;
using System.Collections.Generic;
using System.Linq;
namespace DeviceService
{
    public static class DeviceService
    {
        /*public DeviceService()
        {
            //init
        }*/

        public static bool IsDeviceConnected() //TODO: Does this work ?
        {
            if (ExecuteCommand("ideviceinfo", "").Contains("ERROR")) { return false; }
            else return true;
        }
        public static bool IsDeviceTrusted()
        {
            if (ExecuteCommand("ideviceinfo", "").Contains("ERROR: Could not connect to lockdownd)")) { return false; }
            else return true;
        }
        public static bool IsActivated()
        {
            if (ExecuteCommand("ideviceinfo", "-k ActivationState").Contains("Activated")) { return true; }
            else return false;
        }

        public static Dictionary<string, string> GetConnectedDevices()
        {
            string output = ExecuteCommand("idevice_id", "-l");
            if (string.IsNullOrEmpty(output))
            {
                return new Dictionary<string, string>();
            }

            var deviceIds = output.Split('\n').Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var devices = new Dictionary<string, string>();

            foreach (var deviceId in deviceIds)
            {
                string deviceName = ExecuteCommand("ideviceinfo", $"-u {deviceId} -k DeviceName").Trim();
                string ModelName = GetModel(deviceId);
                string Key = $"{deviceName}: {ModelName}";
                devices[deviceId] = Key;
            }

            return devices;
        }
        public static DeviceData GetDeviceData(string deviceId)
        {
            return new DeviceData
            {
                Identifier = GetIdentifier(deviceId),
                BatteryHealth = GetBatteryHealth(deviceId),
                Color = GetColor(deviceId),
                Storage = GetStorage(deviceId),
                Model = GetModel(deviceId)
            };
        }

        private static string GetBatteryHealth(string deviceId)
        {
            // Attempt the first command
            string output1 = ExecuteCommand("idevicediagnostics", $"-u {deviceId} ioregentry AppleARMPMUCharger");

            // Attempt the second command if the first one fails
            string output2 = ExecuteCommand("idevicediagnostics", $"-u {deviceId} ioregentry AppleSmartBattery");
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

        

        private static string GetColor(string deviceId)
        {
            ColorMapper colorMapper = new();
            return colorMapper.MapColor(ExecuteCommand("ideviceinfo", $"-u {deviceId} -k DeviceEnclosureColor"));
        }
        

        private static string GetStorage(string deviceId)
        {
            ParsingStorage.ParseStorage(ExecuteCommand("ideviceinfo", $"-u {deviceId} -q com.apple.disk_usage"), out string totalDiskCapacityGB);
            return totalDiskCapacityGB;        
        }
        

        private static string GetModel(string deviceId)
        {
            ModelMapper modelMapper = new();
            return modelMapper.MapModel(ExecuteCommand("ideviceinfo", $"-u {deviceId} -k ProductType"));
        }

        private static string GetImei(string deviceId)
        => ExecuteCommand("ideviceinfo", $"-u {deviceId} -k InternationalMobileEquipmentIdentity"); 

        private static string GetSerialNumber(string deviceId)
        => ExecuteCommand("ideviceinfo", $"-u {deviceId} -k SerialNumber");

        private static string GetIdentifier(string deviceId)
        => ParsingDeviceIdentifier.ParseDeviceIdentifier(GetImei(deviceId), GetSerialNumber(deviceId), out string identifier) ? identifier : "NOID";
    }
}