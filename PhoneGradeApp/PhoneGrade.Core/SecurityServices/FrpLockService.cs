namespace PhoneGrade.Core.SecurityServices;

/// <summary>
/// Android Factory Reset Protection and carrier state, read over adb.
///
/// The shell command is injected for the same reason as in the Android reader:
/// it is the only way to run these two checks against what a handset actually
/// answers. Both properties are restricted on current Android, and an account
/// for what a phone does and does not answer is worth having before the check
/// decides anything about it.
/// </summary>
public static class FrpLockService
{
    public enum FrpLockStatus { Locked, Unlocked, Unknown }

    public class CarrierLockStatus
    {
        public bool? IsCarrierLocked { get; set; }
        public string? CarrierName { get; set; }
        public string SIMState { get; set; } = "Unknown";
    }

    /// <summary>Runs one adb shell command and returns its stdout.</summary>
    public delegate Task<string> ShellRunner(string command);

    /// <summary>Runs one adb shell command for a serial.</summary>
    public static ShellRunner DefaultShell(string serial) => command =>
        ToolRunner.ExecuteAsync("adb", $"-s {serial} shell {command}")
                   .ContinueWith(t => t.Result.Stdout);

    /// <summary>
    /// The Factory Reset Protection state, from the secure setting Android keeps
    /// for it. A key the handset will not hand over reads as unknown, which is
    /// not the same as switched off, and the two must not be merged.
    /// </summary>
    public static Task<FrpLockStatus> DetectAsync(string udid) =>
        DetectAsync(udid, DefaultShell(udid));

    public static async Task<FrpLockStatus> DetectAsync(string udid, ShellRunner shell)
    {
        try
        {
            string output = (await shell("settings get secure secure_frp_mode")).Trim();

            return output switch
            {
                "1" => FrpLockStatus.Locked,
                "0" => FrpLockStatus.Unlocked,
                _ => FrpLockStatus.Unknown,
            };
        }
        catch (Exception ex)
        {
            ToolRunner.Log("FrpLockService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            return FrpLockStatus.Unknown;
        }
    }

    /// <summary>
    /// The SIM state and the network the SIM belongs to.
    ///
    /// The lock state is deliberately left unset. <c>ro.carrier</c> and the
    /// <c>gsm.operator</c> family name the network a card is registered on, and a
    /// second hand handset with a Vodafone card in it reported "carrier locked"
    /// purely for carrying that card. That raised an error on a clean phone, while
    /// <c>ro.carrier</c> is empty from Android 10 onwards, so the real thing was
    /// never actually checked. Nothing the shell can read answers the question, so
    /// the field stays empty rather than carrying a guess.
    /// </summary>
    public static Task<CarrierLockStatus> DetectCarrierLockAsync(string udid) =>
        DetectCarrierLockAsync(udid, DefaultShell(udid));

    public static async Task<CarrierLockStatus> DetectCarrierLockAsync(string udid, ShellRunner shell)
    {
        var status = new CarrierLockStatus();

        try
        {
            string simState = (await shell("getprop gsm.sim.state")).Trim();
            if (simState.Length > 0) status.SIMState = simState;

            // gsm.operator.alpha is the current Android answer. ro.carrier is kept
            // as the fallback for older builds that have nothing else.
            string carrier = (await shell("getprop gsm.operator.alpha")).Trim();
            if (carrier.Length == 0 || carrier.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                carrier = (await shell("getprop ro.carrier")).Trim();

            if (carrier.Length > 0 && !carrier.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                status.CarrierName = carrier;

            status.IsCarrierLocked = null;
        }
        catch (Exception ex)
        {
            ToolRunner.Log("FrpLockService", "DetectCarrierLockAsync", 1, ex.Message, ex.StackTrace ?? "");
        }

        return status;
    }
}