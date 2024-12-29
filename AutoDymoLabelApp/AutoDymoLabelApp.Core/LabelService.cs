// Handles label generation
using static CommandExecution.CommandExecution;
using System;
using System.IO;
using System.Runtime.InteropServices;

public static class LabelService
{
    static string templatePath = Path.GetFullPath("../Assets/my.dymo");
    public static string outputPath = Path.GetFullPath("../Assets/gen_label.dymo");
    public static void GenerateLabel(DeviceData data)
    {
        File.Copy(templatePath, outputPath, true);

        var content = File.ReadAllText(outputPath);
        
        content = content
            .Replace("IDENTIFIER", data.Identifier)
            .Replace("MODEL", data.Model)
            .Replace("PCOLOR", data.Color)
            .Replace("BATTERY", data.BatteryHealth)
            .Replace("QUALITY", data.Quality)
            .Replace("PAYM", data.PayMethod)
            .Replace("STORAGE", data.Storage);

        File.WriteAllText(outputPath, content);
    }
    
}

public static class OpenLabel
{
    public static string OpenLabelFile()
    {
        string outputPath = LabelService.outputPath;
        try
        {
            // Convert the relative path to an absolute path
            string absolutePath = Path.GetFullPath(outputPath);

            // Check if the file exists
            if (!File.Exists(absolutePath))
            {
                return $"Error: File not found at {absolutePath}";
            }

            // Determine the platform and execute the appropriate command
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Use 'start' command
                ExecuteCommand("cmd.exe", $"/c start \"\" \"{absolutePath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: Use 'open' command
                ExecuteCommand("open", $"\"{absolutePath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux: Use 'xdg-open' command
                ExecuteCommand("xdg-open", $"\"{absolutePath}\""); //TODO: Fix this
            }
            else
            {
                return "Error: Unsupported operating system.";
            }

            return "Successfully opened label file.";
        }
        catch (Exception ex)
        {
            // Return detailed error information
            return $"Error: {ex.Message}\nStack Trace: {ex.StackTrace}";
        }
    }
}