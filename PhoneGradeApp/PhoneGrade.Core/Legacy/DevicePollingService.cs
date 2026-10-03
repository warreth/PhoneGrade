using System;
using System.Threading.Tasks;
using PhoneGrade.Core.Diagnostics;

namespace PhoneGrade.Core.Legacy;

/// <summary>
/// Legacy device polling service.
/// This is the original background polling mechanism kept for manual scan fallback.
/// </summary>
public static class DevicePollingService
{
    /// <summary>
    /// Performs a single device scan using the legacy polling method.
    /// Returns the device count and diagnostic state.
    /// </summary>
    public static async Task<(Dictionary<string, string> Devices, DeviceService.ConnectionState State)> ScanOnceAsync()
    {
        SystemEventLogger.Info(LogSource.UsbDetector, "Manual scan: starting legacy device poll");

        try
        {
            var (devices, diagState) = await DeviceService.GetConnectedDevicesWithStateAsync();
            
            SystemEventLogger.Info(LogSource.UsbDetector, 
                $"Manual scan: found {devices.Count} device(s), state: {diagState}");
            
            return (devices, diagState);
        }
        catch (Exception ex)
        {
            SystemEventLogger.Error(LogSource.UsbDetector, $"Manual scan failed: {ex.Message}");
            return (new Dictionary<string, string>(), DeviceService.ConnectionState.NotFound);
        }
    }
}