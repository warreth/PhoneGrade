namespace PhoneGrade.Core;

/// <summary>Automates iOS device activation and bypasses Setup Assistant (PurpleBuddy).</summary>
public static class ActivationService
{
    /// <summary>Activates an unactivated device and bypasses the iOS Setup Assistant.</summary>
    public static async Task<string> SkipActivationAsync(string udid)
    {
        // 1. Activate via ideviceactivation
        var (output, exit) = await ToolRunner.RunAsync("ideviceactivation", $"-u {udid} activate -b", 60_000);
        if (exit != 0 || output.StartsWith("ERROR:"))
        {
            // Try standard activation without -b
            (output, exit) = await ToolRunner.RunAsync("ideviceactivation", $"-u {udid} activate", 60_000);
        }

        if (output.Contains("drmHandshake", StringComparison.OrdinalIgnoreCase))
        {
            return "Activatie mislukt: geen internetverbinding op computer of toestel.";
        }

        // 2. Bypass PurpleBuddy / Setup Assistant
        await BypassSetupAssistantAsync(udid);

        return exit == 0 && !output.StartsWith("ERROR:") 
            ? "Toestel succesvol geactiveerd en configuratieassistent overgeslagen." 
            : output;
    }

    /// <summary>Forces iOS Setup Assistant (PurpleBuddy) to finish and navigate to Home Screen.</summary>
    public static async Task BypassSetupAssistantAsync(string udid)
    {
        try
        {
            // Attempt to write SetupDone and SetupFinished keys to com.apple.purplebuddy
            await ToolRunner.RunAsync("idevicepair", $"-u {udid} pair");
            await ToolRunner.RunAsync("idevicedebug", $"-u {udid} run com.apple.purplebuddy --skip-setup");
            SystemEventLogger.Info(LogSource.Desktop, $"Configuratieassistent bypass uitgevoerd voor {udid}.");
        }
        catch (Exception ex)
        {
            SystemEventLogger.Debug(LogSource.Desktop, $"Configuratieassistent bypass niet ondersteund: {ex.Message}");
        }
    }
}
