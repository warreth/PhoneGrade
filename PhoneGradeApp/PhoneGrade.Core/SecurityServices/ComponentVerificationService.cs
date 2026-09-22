namespace PhoneGrade.Core.SecurityServices;

/// <summary>OEM component verification service for AST2 validation and serial cross-checks.</summary>
public static class ComponentVerificationService
{
    public class ComponentVerificationResult
    {
        public List<ComponentStatus> ComponentChecks { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    /// <summary>Verify components against factory originals and AST2 validation.</summary>
    public static async Task<ComponentVerificationResult> VerifyComponentsAsync(string udid, DeviceData device)
    {
        var result = new ComponentVerificationResult
        {
            ComponentChecks = device.ComponentChecks
        };

        try
        {
            // Cross-check component serials against factory originals
            foreach (var check in result.ComponentChecks)
            {
                if (check.Status == ComponentStatusType.Mismatch)
                {
                    result.Warnings.Add($"{check.Name}: Serial mismatch (live: {check.SerialRead}, original: {check.SerialOriginal})");
                }
                else if (check.Status == ComponentStatusType.Untrusted)
                {
                    result.Warnings.Add($"{check.Name}: Niet geverifieerd als origineel Apple onderdeel (Untrusted)");
                }
            }
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ComponentVerificationService", "VerifyComponentsAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Warnings.Add("Component verification incomplete: check failed");
        }

        await Task.CompletedTask;
        return result;
    }

    /// <summary>Parse AST2 diagnostic messages from syslog or diagnostics output.</summary>
    public static List<string> ParseAST2Messages(string diagnosticsOutput)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(diagnosticsOutput))
            return messages;

        string[] patterns = 
        [
            "Important Display Message",
            "Important Battery Message",
            "Important Camera Message",
            "Unable to verify this iPhone has a genuine",
            "Non-genuine part detected"
        ];

        foreach (var pattern in patterns)
        {
            if (diagnosticsOutput.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(pattern);
            }
        }

        return messages;
    }
}
