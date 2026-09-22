namespace PhoneGrade.Core.SecurityServices;

/// <summary>iOS activation lock detection via lockdownd queries.</summary>
public static class ActivationLockService
{
    public enum ActivationLockStatus { Locked, Unlocked, Unknown }
    public enum SIMStatusCode { Present, Missing, Unknown }

    public class CarrierLockStatus
    {
        public bool? IsCarrierLocked { get; set; }
        public string? CarrierName { get; set; }
        public bool SIMPresent { get; set; } = true;
        public string SIMState { get; set; } = "Unknown";
    }

    /// <summary>Detect activation lock state from in-memory bulk XML dictionaries (single-pass extraction).</summary>
    public static async Task<ActivationLockStatus> DetectAsync(string udid, DeviceService.DeviceRawData? raw = null)
    {
        try
        {
            raw ??= await DeviceService.GetBulkRawDataAsync(udid);

            // 1. Query com.apple.fmip domain for FmipEnabled (most authoritative local source)
            string? fmipEnabled = DeviceService.FindDictValue(raw.FmipDict, "FmipEnabled", "FMIEnabled") ??
                                  DeviceService.FindDictValue(raw.DefaultDict, "FmipEnabled", "FMIEnabled");
            if (!string.IsNullOrWhiteSpace(fmipEnabled))
            {
                if (fmipEnabled == "true" || fmipEnabled == "1") return ActivationLockStatus.Locked;
                if (fmipEnabled == "false" || fmipEnabled == "0") return ActivationLockStatus.Unlocked;
            }

            // 2. PurpleBuddy query for FMI status
            string? fmiActive = DeviceService.FindDictValue(raw.PurpleBuddyDict, "FindMyiPhoneActive", "FMIActive") ??
                                DeviceService.FindDictValue(raw.DefaultDict, "FindMyiPhoneActive", "FMIActive");
            if (fmiActive == "true" || fmiActive == "1")
            {
                return ActivationLockStatus.Locked;
            }

            // 3. MobileGestalt query
            string? fmi = DeviceService.FindDictValue(raw.GestaltDict, "FMIActive", "FindMyDeviceState");
            if (fmi == "true" || fmi == "1" || fmi == "Enabled")
            {
                return ActivationLockStatus.Locked;
            }

            // 4. Check ActivationState key
            string? activation = DeviceService.FindDictValue(raw.DefaultDict, "ActivationState") ??
                                 DeviceService.FindDictValue(raw.GestaltDict, "ActivationState");
            if (!string.IsNullOrWhiteSpace(activation))
            {
                activation = activation.Trim().ToLowerInvariant();
                if (activation == "unactivated")
                {
                    return ActivationLockStatus.Locked;
                }
                if (activation == "activated")
                {
                    return ActivationLockStatus.Unlocked;
                }
            }

            return ActivationLockStatus.Unknown;
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ActivationLockService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            return ActivationLockStatus.Unknown;
        }
    }

    /// <summary>Detect carrier lock and SIM status from in-memory bulk XML dictionaries.</summary>
    public static async Task<CarrierLockStatus> DetectCarrierLockAsync(string udid, DeviceService.DeviceRawData? raw = null)
    {
        var status = new CarrierLockStatus();

        try
        {
            raw ??= await DeviceService.GetBulkRawDataAsync(udid);

            string? simStatus = DeviceService.FindDictValue(raw.DefaultDict, "SIMStatus") ??
                                DeviceService.FindDictValue(raw.GestaltDict, "SIMStatus");
            if (!string.IsNullOrWhiteSpace(simStatus))
            {
                simStatus = simStatus.Trim().ToLowerInvariant();
                status.SIMState = simStatus;
                status.SIMPresent = !simStatus.Contains("missing") && !simStatus.Contains("nosim");
            }

            string? carrierName = DeviceService.FindDictValue(raw.DefaultDict, "CarrierName", "CarrierBundleName");
            if (!string.IsNullOrWhiteSpace(carrierName))
            {
                status.CarrierName = carrierName;
                status.IsCarrierLocked = true;
            }

            if (!status.SIMPresent)
            {
                status.IsCarrierLocked = null;
            }
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ActivationLockService", "DetectCarrierLockAsync", 1, ex.Message, ex.StackTrace ?? "");
        }

        return status;
    }
}
