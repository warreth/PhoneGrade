namespace PhoneGrade.Core.SecurityServices;

/// <summary>Android Factory Reset Protection detection via adb.</summary>
public static class FrpLockService
{
    public enum FrpLockStatus { Locked, Unlocked, Unknown }

    public class CarrierLockStatus
    {
        public bool? IsCarrierLocked { get; set; }
        public string? CarrierName { get; set; }
        public string SIMState { get; set; } = "Unknown";
    }

    /// <summary>Detect FRP lock state from secure_frp_mode setting.</summary>
    public static async Task<FrpLockStatus> DetectAsync(string udid)
    {
        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("adb", $"-s {udid} shell settings get secure secure_frp_mode");

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                ToolRunner.Log("FrpLockService", "DetectAsync", exitCode, output, "");
                return FrpLockStatus.Unknown;
            }

            string frpMode = output.Trim();
            return frpMode switch
            {
                "1" => FrpLockStatus.Locked,
                "0" => FrpLockStatus.Unlocked,
                _ => FrpLockStatus.Unknown
            };
        }
        catch (Exception ex)
        {
            ToolRunner.Log("FrpLockService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            return FrpLockStatus.Unknown;
        }
    }

    /// <summary>Detect carrier lock and SIM state from getprop.</summary>
    public static async Task<CarrierLockStatus> DetectCarrierLockAsync(string udid)
    {
        var status = new CarrierLockStatus();

        try
        {
            var (simState, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {udid} shell getprop gsm.sim.state");
            if (!string.IsNullOrWhiteSpace(simState))
            {
                status.SIMState = simState.Trim();
            }

            var (carrier, _, _) = await ToolRunner.ExecuteAsync("adb", $"-s {udid} shell getprop ro.carrier");
            if (!string.IsNullOrWhiteSpace(carrier))
            {
                status.CarrierName = carrier.Trim();
                status.IsCarrierLocked = !carrier.Contains("unknown", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            ToolRunner.Log("FrpLockService", "DetectCarrierLockAsync", 1, ex.Message, ex.StackTrace ?? "");
        }

        return status;
    }
}
