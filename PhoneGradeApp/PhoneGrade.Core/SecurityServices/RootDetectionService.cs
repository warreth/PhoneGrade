namespace PhoneGrade.Core.SecurityServices;

/// <summary>Android root detection via su command, package checks, and system properties.</summary>
public static class RootDetectionService
{
    public class RootStatus
    {
        public bool IsRooted { get; set; }
        public List<string> Evidence { get; set; } = [];
        public string Method { get; set; } = "Unknown";
    }

    private static readonly string[] RootIndicatorPackages = 
    [
        "com.topjohnwu.magisk",
        "com.koushikdutta.superuser",
        "eu.chainfire.supersu",
        "com.noshufou.android.su",
        "com.thirdparty.superuser"
    ];

    /// <summary>Detect root via multiple methods: su command, root apps, system properties.</summary>
    public static async Task<RootStatus> DetectAsync(string udid)
    {
        var result = new RootStatus();

        try
        {
            // Method 1: Test su command
            var (suOutput, _, suExit) = await ToolRunner.ExecuteAsync("adb", 
                $"-s {udid} shell su -c 'echo ROOT_DETECTED'");

            if (suExit == 0 && suOutput.Contains("ROOT_DETECTED"))
            {
                result.IsRooted = true;
                result.Evidence.Add("su command succeeded");
                result.Method = "su";
            }

            // Method 2: Check for root management apps
            var (packages, _, pkgExit) = await ToolRunner.ExecuteAsync("adb", 
                $"-s {udid} shell pm list packages");

            if (pkgExit == 0 && !string.IsNullOrWhiteSpace(packages))
            {
                foreach (var rootPkg in RootIndicatorPackages)
                {
                    if (packages.Contains(rootPkg, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsRooted = true;
                        result.Evidence.Add($"Root app: {rootPkg}");
                        result.Method = "package";
                    }
                }
            }

            // Method 3: Check ro.debuggable and ro.secure properties
            var (debuggable, _, _) = await ToolRunner.ExecuteAsync("adb", 
                $"-s {udid} shell getprop ro.debuggable");
            
            var (secure, _, _) = await ToolRunner.ExecuteAsync("adb", 
                $"-s {udid} shell getprop ro.secure");

            if (debuggable.Trim() == "1")
            {
                result.Evidence.Add("ro.debuggable=1");
            }

            if (secure.Trim() == "0")
            {
                result.IsRooted = true;
                result.Evidence.Add("ro.secure=0");
                result.Method = "property";
            }

            if (result.Evidence.Count == 0)
            {
                result.Method = "None";
            }
        }
        catch (Exception ex)
        {
            ToolRunner.Log("RootDetectionService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Method = "Error";
        }

        return result;
    }
}
