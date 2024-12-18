using Parsing;
using Mappings;
using static CommandExecution.CommandExecution;

namespace DeviceService
{
    public static class DeviceService
    {
        /*public DeviceService()
        {
            //init
        }*/

        public static bool IsDeviceConnected()
        {
            if (ExecuteCommand("ideviceinfo", "").Contains("ERROR: No device found!")) { return false; }
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

        public static DeviceData GetDeviceData()
        {
            return new DeviceData
            {
                Identifier = GetIdentifier(),
                BatteryHealth = GetBatteryHealth(),
                Color = GetColor(),
                Storage = GetStorage(),
                Model = GetModel()
            };
        }

        private static string GetBatteryHealth()
        {
            // Attempt the first command
            string output1 = ExecuteCommand("idevicediagnostics", "ioregentry AppleARMPMUCharger");

            // Attempt the second command if the first one fails
            string output2 = ExecuteCommand("idevicediagnostics", "ioregentry AppleSmartBattery");

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

        

        private static string GetColor()
        {
            ColorMapper colorMapper = new();
            return colorMapper.MapColor(ExecuteCommand("ideviceinfo", "-k DeviceEnclosureColor"));
        }
        

        private static string GetStorage()
        {
            ParsingStorage.ParseStorage(ExecuteCommand("ideviceinfo", "-q com.apple.disk_usage"), out string totalDiskCapacityGB);
            return totalDiskCapacityGB;        
        }
        

        private static string GetModel()
        {
            ModelMapper modelMapper = new();
            return modelMapper.MapModel(ExecuteCommand("ideviceinfo", "-k ProductType"));
        }

        private static string GetImei()
        => ExecuteCommand("ideviceinfo", "-k InternationalMobileEquipmentIdentity"); 

        private static string GetSerialNumber()
        => ExecuteCommand("ideviceinfo", "-k SerialNumber");

        private static string GetIdentifier()
        => ParsingDeviceIdentifier.ParseDeviceIdentifier(GetImei(), GetSerialNumber(), out string identifier) ? identifier : "NOID";
    }
}