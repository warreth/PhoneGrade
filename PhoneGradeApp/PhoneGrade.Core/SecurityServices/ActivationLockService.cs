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

    /// <summary>Detect activation lock state from lockdownd ActivationState key.</summary>
    public static async Task<ActivationLockStatus> DetectAsync(string udid)
    {
        try
        {
            string activation = await DeviceService.GetKeyAsync(udid, "ActivationState");
            if (activation.StartsWith("ERROR:") || string.IsNullOrWhiteSpace(activation))
            {
                ToolRunner.Log("ActivationLockService", "DetectAsync", 0, "lockdownd unavailable", activation);
                return ActivationLockStatus.Unknown;
            }

            activation = activation.Trim().ToLowerInvariant();
            return activation switch
            {
                "activated" => ActivationLockStatus.Unlocked,
                "unactivated" => ActivationLockStatus.Locked,
                _ => ActivationLockStatus.Unknown
            };
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
