namespace PhoneGrade.Core.SecurityServices;

/// <summary>iOS jailbreak detection via app listing and SSH probe.</summary>
public static class JailbreakDetectionService
{
    public class JailbreakStatus
    {
        public bool IsJailbroken { get; set; }
        public List<string> Evidence { get; set; } = [];
        public string Confidence { get; set; } = "Unknown";
    }

    private static readonly string[] JailbreakIndicatorApps = 
    [
        "com.saurik.cydia",
        "com.saurik.substrate",
        "io.sileo.app",
        "org.coolstar.sileo",
        "com.zebra.ios",
        "com.bigboss.installer",
        "org.thebigboss.icy",
        "com.ex.ssh",
        "org.openssh.openssh"
    ];

    /// <summary>Detect jailbreak by checking for known package manager apps.</summary>
    public static async Task<JailbreakStatus> DetectAsync(string udid)
    {
        var result = new JailbreakStatus();

        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("ideviceinstaller", $"-u {udid} -l");

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                ToolRunner.Log("JailbreakDetectionService", "DetectAsync", exitCode, "app listing failed", output);
                result.Confidence = "Unknown";
                return result;
            }

            foreach (var jbApp in JailbreakIndicatorApps)
            {
                if (output.Contains(jbApp, StringComparison.OrdinalIgnoreCase))
                {
                    result.Evidence.Add(jbApp);
                    result.IsJailbroken = true;
                }
            }

            result.Confidence = result.IsJailbroken ? "High" : "Medium";
        }
        catch (Exception ex)
        {
            ToolRunner.Log("JailbreakDetectionService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Confidence = "Unknown";
        }

        return result;
    }

    /// <summary>Optional SSH probe on port 22 (requires user consent).</summary>
    public static async Task<bool> ProbeSSHAsync(string udid)
    {
        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("iproxy", $"2222 22 -u {udid}");
            return exitCode == 0 && output.Contains("success", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
