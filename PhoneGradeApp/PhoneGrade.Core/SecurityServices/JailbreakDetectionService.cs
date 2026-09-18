namespace PhoneGrade.Core.SecurityServices;

/// <summary>iOS jailbreak detection via app listing, file presence, and SSH probe.</summary>
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

    /// <summary>Detect jailbreak via multiple methods: package managers, file presence, SSH.</summary>
    public static async Task<JailbreakStatus> DetectAsync(string udid)
    {
        var result = new JailbreakStatus();

        try
        {
            // Method 1: Check for jailbreak package managers
            var appList = await GetInstalledAppsAsync(udid);
            foreach (var jbApp in JailbreakIndicatorApps)
            {
                if (appList.Any(app => app.Contains(jbApp, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Evidence.Add($"Package: {jbApp}");
                    result.IsJailbroken = true;
                }
            }

            // Method 2: Check for common jailbreak file paths
            var fileEvidence = await CheckJailbreakFilesAsync(udid);
            result.Evidence.AddRange(fileEvidence);
            if (fileEvidence.Count > 0)
            {
                result.IsJailbroken = true;
            }

            // Method 3: SSH probe (high indicator if open)
            if (!result.IsJailbroken)
            {
                bool sshOpen = await ProbeSSHAsync(udid);
                if (sshOpen)
                {
                    result.Evidence.Add("SSH port 22 open");
                    result.IsJailbroken = true;
                }
            }

            result.Confidence = CalculateConfidence(result.Evidence);
        }
        catch (Exception ex)
        {
            ToolRunner.Log("JailbreakDetectionService", "DetectAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Confidence = "Unknown";
        }

        return result;
    }

    private static async Task<List<string>> GetInstalledAppsAsync(string udid)
    {
        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("ideviceinstaller", $"-u {udid} -l");
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Select(line => line.Trim()).ToList();
            }
        }
        catch
        {
            // Installation proxy not available
        }
        return [];
    }

    private static async Task<List<string>> CheckJailbreakFilesAsync(string udid)
    {
        var evidence = new List<string>();
        
        string[] jailbreakPaths = 
        [
            "/Applications/Cydia.app",
            "/Library/MobileSubstrate",
            "/usr/libexec/cydia"
        ];

        try
        {
            foreach (var path in jailbreakPaths)
            {
                var (output, _, exitCode) = await ToolRunner.ExecuteAsync("idevicefs", $"-u {udid} ls {path}");
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    evidence.Add($"Path accessible: {path}");
                }
            }
        }
        catch
        {
            // idevicefs not available or device doesn't expose filesystem
        }

        return evidence;
    }

    private static string CalculateConfidence(List<string> evidence)
    {
        if (evidence.Count == 0) return "Low";
        if (evidence.Any(e => e.Contains("Cydia") || e.Contains("Sileo"))) return "High";
        if (evidence.Count >= 2) return "Medium";
        return "Low";
    }

    /// <summary>Optional SSH probe on port 22.</summary>
    public static async Task<bool> ProbeSSHAsync(string udid)
    {
        try
        {
            var (output, _, exitCode) = await ToolRunner.ExecuteAsync("iproxy", $"2222 22 -u {udid}");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
