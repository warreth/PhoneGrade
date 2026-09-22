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

    private static bool IsTruthy(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim().ToLowerInvariant();
        return t is "true" or "1" or "yes" or "enabled" or "on" or "locked" or "y";
    }

    private static bool IsExplicitlyFalse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim().ToLowerInvariant();
        return t is "false" or "0" or "no" or "disabled" or "off" or "unlocked" or "n";
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
            if (IsTruthy(fmipEnabled)) return ActivationLockStatus.Locked;

            // 2. PurpleBuddy query for FMI status
            string? fmiActive = DeviceService.FindDictValue(raw.PurpleBuddyDict, "FindMyiPhoneActive", "FMIActive") ??
                                DeviceService.FindDictValue(raw.DefaultDict, "FindMyiPhoneActive", "FMIActive");
            if (IsTruthy(fmiActive)) return ActivationLockStatus.Locked;

            // 3. MobileGestalt query: FMIActive, FindMyDeviceState, TargetIsInternal
            string? gestaltFmi = DeviceService.FindDictValue(raw.GestaltDict, "FMIActive", "FindMyDeviceState", "TargetIsInternal");
            if (IsTruthy(gestaltFmi)) return ActivationLockStatus.Locked;

            // 4. If ANY source explicitly confirms FMI is OFF/disabled
            if (IsExplicitlyFalse(fmipEnabled) || IsExplicitlyFalse(fmiActive) || IsExplicitlyFalse(gestaltFmi))
            {
                return ActivationLockStatus.Unlocked;
            }

            // 5. ActivationState check:
            // "unactivated" implies locked/activation required.
            // IMPORTANT: "activated" DOES NOT mean FMI is OFF! Return Unknown so server check can be authoritative.
            string? activation = DeviceService.FindDictValue(raw.DefaultDict, "ActivationState") ??
                                 DeviceService.FindDictValue(raw.GestaltDict, "ActivationState");
            if (!string.IsNullOrWhiteSpace(activation))
            {
                string act = activation.Trim().ToLowerInvariant();
                if (act is "unactivated" or "unregistered")
                {
                    return ActivationLockStatus.Locked;
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