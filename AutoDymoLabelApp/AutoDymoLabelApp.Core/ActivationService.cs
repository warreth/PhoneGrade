namespace AutoDymoLabel.Core;

/// <summary>Bypasses the activation screen (ideviceactivation activate -b).</summary>
public static class ActivationService
{
    public static async Task<string> SkipActivationAsync(string udid)
    {
        var (output, exit) = await ToolRunner.RunAsync("ideviceactivation", $"-u {udid} activate -b", 60_000);
        if (output.Contains("drmHandshake"))
            return "Activation failed: no internet connection. Connect the device to Wi-Fi or ethernet and retry.";
        return exit == 0 && !output.StartsWith("ERROR:") ? "Device activated." : output;
    }
}
