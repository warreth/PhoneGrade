// Handles label generation
using static CommandExecution.CommandExecution;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

public static class LabelService
{
    static string templatePath = Path.GetFullPath("../Assets/my.dymo");
    public static string outputPath = Path.GetFullPath("../Assets/gen_label.dymo");
    public static void GenerateLabel(DeviceData data)
    {
        File.Copy(templatePath, outputPath, true);

        var content = File.ReadAllText(outputPath);

        string batteryHealth = data.BatteryHealth.Contains("X") ? data.BatteryHealth : data.BatteryHealth + "%";

        content = content
            .Replace("IDENTIFIER", data.Identifier)
            .Replace("MODEL", data.Model)
            .Replace("PCOLOR", data.Color)
            .Replace("BATTERY", batteryHealth)
            .Replace("QUALITY", data.Quality)
            .Replace("PAYM", data.PayMethod)
            .Replace("STORAGE", data.Storage);

        File.WriteAllText(outputPath, content);
    }

}

public static class OpenLabel
{
    public static async Task<string> OpenLabelFileAsync()
    {
        string outputPath = LabelService.outputPath;
        try
        {
            // Convert the relative path to an absolute path.
            string absolutePath = Path.GetFullPath(outputPath);

            // Check if the file exists.
            if (!File.Exists(absolutePath))
            {
                return $"Error: File not found at {absolutePath}";
            }

            // Create a ProcessStartInfo with UseShellExecute enabled.
            var psi = new ProcessStartInfo
            {
                FileName = absolutePath,
                UseShellExecute = true
            };

            // Start the process on a background thread.
            await Task.Run(() => Process.Start(psi));

            return "Successfully opened label file.";
        }
        catch (Exception ex)
        {
            // Return detailed error information.
            return $"Error: {ex.Message}\nStack Trace: {ex.StackTrace}";
        }
    }
}