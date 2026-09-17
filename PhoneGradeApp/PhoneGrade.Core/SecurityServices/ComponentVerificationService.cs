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
            // Query diagnostics for AST2 validation messages
            var (diagOutput, _, _) = await ToolRunner.ExecuteAsync("idevicediagnostics", 
                $"-u {udid} diagnostics IORegistry");

            if (!string.IsNullOrWhiteSpace(diagOutput))
            {
                // Check for Apple Service Toolkit (AST2) validation failures
                if (diagOutput.Contains("Important Display Message", StringComparison.OrdinalIgnoreCase) ||
                    diagOutput.Contains("Unable to verify this iPhone has a genuine Apple display", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add("AST2: Display niet geverifieerd als origineel Apple onderdeel");
                    
                    var displayCheck = result.ComponentChecks.FirstOrDefault(c => c.Name == "Scherm (LCM)");
                    if (displayCheck != null)
                    {
                        var updatedCheck = new ComponentStatus 
                        {
                            Name = displayCheck.Name,
                            SerialRead = displayCheck.SerialRead,
                            SerialOriginal = displayCheck.SerialOriginal,
                            Status = ComponentStatusType.Untrusted 
                        };
                        result.ComponentChecks[result.ComponentChecks.IndexOf(displayCheck)] = updatedCheck;
                    }
                }

                if (diagOutput.Contains("Important Battery Message", StringComparison.OrdinalIgnoreCase) ||
                    diagOutput.Contains("Unable to verify this iPhone has a genuine Apple battery", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add("AST2: Batterij niet geverifieerd als origineel Apple onderdeel");
                    
                    var batteryCheck = result.ComponentChecks.FirstOrDefault(c => c.Name == "Batterij");
                    if (batteryCheck != null)
                    {
                        var updatedCheck = new ComponentStatus 
                        {
                            Name = batteryCheck.Name,
                            SerialRead = batteryCheck.SerialRead,
                            SerialOriginal = batteryCheck.SerialOriginal,
                            Status = ComponentStatusType.Untrusted 
                        };
                        result.ComponentChecks[result.ComponentChecks.IndexOf(batteryCheck)] = updatedCheck;
                    }
                }

                if (diagOutput.Contains("Important Camera Message", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add("AST2: Camera niet geverifieerd als origineel Apple onderdeel");
                }
            }

            // Cross-check component serials against factory originals (already done in DeviceService)
            foreach (var check in result.ComponentChecks)
            {
                if (check.Status == ComponentStatusType.Mismatch)
                {
                    result.Warnings.Add($"{check.Name}: Serial mismatch (live: {check.SerialRead}, original: {check.SerialOriginal})");
                }
            }
        }
        catch (Exception ex)
        {
            ToolRunner.Log("ComponentVerificationService", "VerifyComponentsAsync", 1, ex.Message, ex.StackTrace ?? "");
            result.Warnings.Add("Component verification incomplete: diagnostic query failed");
        }

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
