namespace PhoneGrade.Core.Android;

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
            if (simState.Length > 0) status.SIMState = FirstEntry(simState);

            // gsm.operator.alpha is the current Android answer. The other two are
            // the fallbacks: the SIM's own name for a phone with no service yet,
            // and ro.carrier for older builds that have nothing else. A handset
            // with no card answers the first one with a bare separator (","), and
            // a separator is not a network.
            string carrier = CleanCarrierName(await shell("getprop gsm.operator.alpha"));
            if (carrier.Length == 0)
                carrier = CleanCarrierName(await shell("getprop gsm.sim.operator.alpha"));
            if (carrier.Length == 0)
                carrier = CleanCarrierName(await shell("getprop ro.carrier"));

            if (carrier.Length > 0)
                status.CarrierName = carrier;

            status.IsCarrierLocked = null;
        }
        catch (Exception ex)
        {
            ToolRunner.Log("FrpLockService", "DetectCarrierLockAsync", 1, ex.Message, ex.StackTrace ?? "");
        }

        return status;
    }

    /// <summary>
    /// A property that names a network, with the separators an absent card
    /// leaves behind taken off. "unknown" is the other spelling absence uses.
    /// </summary>
    /// <remarks>
    /// A dual SIM handset answers with one entry per slot, separated by a comma
    /// ("NL KPN  ,vodafone NL"). The report has room for one network, and the
    /// first entry is the one the phone registers on by default; printing the
    /// whole string put "NL KPN  ,vodafone NL" on the label as if it were a
    /// carrier's name.
    /// </remarks>
    private static string CleanCarrierName(string raw)
    {
        string cleaned = FirstEntry(raw);
        if (cleaned.Length == 0 || cleaned.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            return "";
        return cleaned;
    }

    /// <summary>The first of the comma separated entries a dual SIM handset answers with.</summary>
    private static string FirstEntry(string raw)
    {
        foreach (string part in (raw ?? "").Split(','))
        {
            string cleaned = part.Trim();
            if (cleaned.Length > 0) return cleaned;
        }

        return "";
    }
}