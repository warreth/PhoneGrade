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

    /// <summary>Detect activation lock state from com.apple.fmip, MobileGestalt, PurpleBuddy, and ActivationState.</summary>
    public static async Task<ActivationLockStatus> DetectAsync(string udid)
    {
        try
        {
            // 1. Query com.apple.fmip domain for FmipEnabled (most authoritative local source)
            string fmipDomain = await DeviceService.GetDomainAsync(udid, "com.apple.fmip");
            if (!string.IsNullOrWhiteSpace(fmipDomain))
            {
                string? fmipEnabled = Parsers.KeyValue(fmipDomain, "FmipEnabled") ?? Parsers.KeyValue(fmipDomain, "FMIEnabled");
                if (fmipEnabled == "true" || fmipEnabled == "1")
                {
                    return ActivationLockStatus.Locked;
                }
                if (fmipEnabled == "false" || fmipEnabled == "0")
                {
                    return ActivationLockStatus.Unlocked;
                }
            }

            // 2. PurpleBuddy query for FMI status
            string pbDomain = await DeviceService.GetDomainAsync(udid, "com.apple.purplebuddy");
            if (!string.IsNullOrWhiteSpace(pbDomain))
            {
                string? fmiActive = Parsers.KeyValue(pbDomain, "FindMyiPhoneActive") ?? Parsers.KeyValue(pbDomain, "FMIActive");
                if (fmiActive == "true" || fmiActive == "1")
                {
                    return ActivationLockStatus.Locked;
                }
            }

            // 3. MobileGestalt query
            string gestalt = await DeviceService.GetDomainAsync(udid, "com.apple.mobile.gestalt");
            if (!string.IsNullOrWhiteSpace(gestalt))
            {
                string? fmi = Parsers.KeyValue(gestalt, "FMIActive") ?? Parsers.KeyValue(gestalt, "FindMyDeviceState");
                if (fmi == "true" || fmi == "1" || fmi == "Enabled")
                {
                    return ActivationLockStatus.Locked;
                }
            }

            // 4. Check ActivationState key
            string activation = await DeviceService.GetKeyAsync(udid, "ActivationState");
            if (!activation.StartsWith("ERROR:") && !string.IsNullOrWhiteSpace(activation))
            {
                activation = activation.Trim().ToLowerInvariant();
                if (activation == "unactivated")
                {
                    return ActivationLockStatus.Locked;
                }
                if (activation == "activated")
                {
                    // Note: 'activated' locally does not mean FMI is OFF on Apple server!
                    // Local state shows Unlocked, but server check may override.
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

    /// <summary>Detect carrier lock and SIM status from lockdownd.</summary>
    public static async Task<CarrierLockStatus> DetectCarrierLockAsync(string udid)
    {
        var status = new CarrierLockStatus();

        try
        {
            string simStatus = await DeviceService.GetKeyAsync(udid, "SIMStatus");
            if (!simStatus.StartsWith("ERROR:"))
            {
                simStatus = simStatus.Trim().ToLowerInvariant();
                status.SIMState = simStatus;
                status.SIMPresent = !simStatus.Contains("missing") && !simStatus.Contains("nosim");
            }

            string carrierBundle = await DeviceService.GetDomainAsync(udid, "com.apple.mobile.carrier_bundle");
            if (!string.IsNullOrWhiteSpace(carrierBundle))
            {
                string? carrierName = Parsers.KeyValue(carrierBundle, "CarrierName");
                if (!string.IsNullOrWhiteSpace(carrierName))
                {
                    status.CarrierName = carrierName;
                    status.IsCarrierLocked = true;
                }
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
